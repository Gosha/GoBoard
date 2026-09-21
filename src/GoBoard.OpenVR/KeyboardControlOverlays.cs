using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using Valve.VR;

namespace GoBoard.Vr;

internal sealed class KeyboardControlOverlays : IDisposable
{
    private sealed class Button(KeyboardAction action)
    {
        public readonly KeyboardAction Action = action;
        public ulong Handle;
        public readonly FloatingButtonState Input = new();
        public readonly OverlayPointers Pointers = new();
        public (int Revision, bool Enabled, string Theme)? Drawn;
        public (uint Device, ETrackingUniverseOrigin Origin, Matrix4x4 Pose)? Transform;
        public float Width;
        public bool Visible;
    }
    private readonly CVRSystem system;
    private readonly CVROverlay overlay;
    private readonly OverlayGraphics graphics;
    private readonly ulong main;
    private readonly Action<KeyboardAction> activate;
    private readonly Button[] buttons = MainKeyboardControls.Actions.Select(a => new Button(a)).ToArray();
    private readonly TrackedDevicePose_t[] devices = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private readonly ulong left, right;
    private BoardSettings applied;
    private InputTarget target;
    private float scale;
    private bool visible;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public KeyboardControlOverlays(CVRSystem system, CVROverlay overlay, OverlayGraphics graphics, ulong main, Action<KeyboardAction> activate)
    {
        this.system = system; this.overlay = overlay; this.graphics = graphics; this.main = main; this.activate = activate;
        left = SourcePath("/user/hand/left"); right = SourcePath("/user/hand/right");
        try
        {
            foreach (var b in buttons)
            {
                var key = b.Action == KeyboardAction.ToggleNumpad ? "numpad" : "reset-position";
                Check(overlay.CreateOverlay("goboard.app.controls." + key, MainKeyboardControls.Name(b.Action), ref b.Handle), "Create keyboard control");
                Check(overlay.SetOverlayFlag(b.Handle, VROverlayFlags.VisibleInDashboard, true), "Show control with dashboard");
                Check(overlay.SetOverlayFlag(b.Handle, VROverlayFlags.NoBackside, false), "Control backside");
                Check(overlay.SetOverlayFlag(b.Handle, VROverlayFlags.MultiCursor, true), "Control pointers");
                var mouse = new HmdVector2_t { v0 = MainKeyboardControls.Size, v1 = MainKeyboardControls.Size };
                Check(overlay.SetOverlayMouseScale(b.Handle, ref mouse), "Control coordinates");
                Check(overlay.SetOverlayInputMethod(b.Handle, VROverlayInputMethod.Mouse), "Control input");
            }
        }
        catch { Dispose(); throw; }
    }

    public void Update(BoardSettings settings, bool show, bool interactive, float currentScale)
    {
        var foreground = WindowsKeyboard.Foreground();
        if (applied != settings || scale != currentScale || target != foreground || visible != show || !interactive)
            Reset();
        applied = settings; scale = currentScale; target = foreground;
        foreach (var b in buttons)
        {
            var showButton = show && MainKeyboardControls.Visible(b.Action, settings);
            if (showButton)
            {
                var width = MainKeyboardControls.WidthInMeters(b.Action, scale);
                if (b.Width != width) { Check(overlay.SetOverlayWidthInMeters(b.Handle, width), "Size control"); b.Width = width; }
                Place(b, MainKeyboardControls.Offset(b.Action, scale));
                Draw(b);
            }
            if (b.Visible != showButton)
            {
                b.Input.Reset(Now);
                Check(showButton ? overlay.ShowOverlay(b.Handle) : overlay.HideOverlay(b.Handle), "Control visibility");
                b.Visible = showButton;
            }
        }
        visible = show;
        interactive &= show;
        if (interactive) system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, devices);
        foreach (var b in buttons)
        {
            if (b.Input.CapturedDevice is { } owner && !Tracked(owner)) b.Input.Reset(Now);
            var e = new VREvent_t();
            while (overlay.PollNextOverlayEvent(b.Handle, ref e, (uint)Marshal.SizeOf<VREvent_t>()))
            {
                var type = (EVREventType)e.eventType;
                if (type == EVREventType.VREvent_ImageFailed) throw new InvalidOperationException("Keyboard control texture failed.");
                if (!interactive || !b.Visible) continue;
                var focus = type is EVREventType.VREvent_FocusEnter or EVREventType.VREvent_FocusLeave;
                if (!focus && type is not (EVREventType.VREvent_MouseMove or EVREventType.VREvent_MouseButtonDown or EVREventType.VREvent_MouseButtonUp)) continue;
                uint? device = Controller(e.trackedDeviceIndex);
                if (focus && !device.HasValue)
                {
                    var role = e.data.overlay.devicePath == left && left != 0 ? ETrackedControllerRole.LeftHand :
                        e.data.overlay.devicePath == right && right != 0 ? ETrackedControllerRole.RightHand : ETrackedControllerRole.Invalid;
                    if (role != ETrackedControllerRole.Invalid) device = Controller(system.GetTrackedDeviceIndexForControllerRole(role));
                }
                var pointer = b.Pointers.Resolve(focus ? e.data.overlay.cursorIndex : e.data.mouse.cursorIndex, device,
                    type == EVREventType.VREvent_FocusEnter);
                if (!pointer.HasValue || type == EVREventType.VREvent_FocusEnter) continue;
                if (!Tracked(pointer.Value)) { b.Input.Reset(Now); continue; }
                var down = type == EVREventType.VREvent_MouseButtonDown;
                var up = type == EVREventType.VREvent_MouseButtonUp;
                if ((down || up) && e.data.mouse.button != (uint)EVRMouseButton.Left) continue;
                var now = Now;
                if (b.Input.Process(pointer.Value, pointer.Value, e.data.mouse.x, e.data.mouse.y, now - e.eventAgeSeconds, now,
                    down, up, type == EVREventType.VREvent_FocusLeave))
                {
                    Reset();
                    activate(b.Action);
                    return; // Geometry may change; remaining queued input is rejected next frame.
                }
            }
            if (b.Visible) Draw(b);
        }
    }

    private void Reset() { foreach (var b in buttons) b.Input.Reset(Now); }
    private bool Tracked(uint device) => device < devices.Length && devices[device].bDeviceIsConnected && devices[device].bPoseIsValid;
    private uint? Controller(uint device) => device < devices.Length && system.GetTrackedDeviceClass(device) == ETrackedDeviceClass.Controller ? device : null;
    private void Draw(Button b)
    {
        var signature = (b.Input.Revision, b.Action == KeyboardAction.ToggleNumpad && applied.NumpadEnabled, applied.Theme);
        if (b.Drawn == signature) return;
        using var bitmap = MainKeyboardControlRenderer.Render(b.Action, signature.Item2, b.Input.Hovered, b.Input.Pressed, applied.Theme);
        graphics.Upload(overlay, b.Handle, bitmap);
        b.Drawn = signature;
    }

    private void Place(Button b, Matrix4x4 offset)
    {
        var type = VROverlayTransformType.VROverlayTransform_Absolute;
        Check(overlay.GetOverlayTransformType(main, ref type), "Read keyboard transform");
        var raw = new HmdMatrix34_t();
        var origin = ETrackingUniverseOrigin.TrackingUniverseStanding;
        var device = OpenVR.k_unTrackedDeviceIndexInvalid;
        if (type == VROverlayTransformType.VROverlayTransform_TrackedDeviceRelative)
            Check(overlay.GetOverlayTransformTrackedDeviceRelative(main, ref device, ref raw), "Read keyboard controller");
        else Check(overlay.GetOverlayTransformAbsolute(main, ref origin, ref raw), "Read keyboard pose");
        if (!OpenVrPose.TryRigid(raw, out var parent)) throw new InvalidOperationException("Invalid keyboard pose.");
        var transform = (device, origin, offset * OverlayGeometry.PanelFromTexture(scale) * parent);
        if (b.Transform == transform) return;
        var pose = OpenVrPose.ToOpenVr(transform.Item3);
        Check(device == OpenVR.k_unTrackedDeviceIndexInvalid ? overlay.SetOverlayTransformAbsolute(b.Handle, origin, ref pose) :
            overlay.SetOverlayTransformTrackedDeviceRelative(b.Handle, device, ref pose), "Place keyboard control");
        b.Transform = transform;
    }
    private static ulong SourcePath(string name) { ulong path = 0; return OpenVR.Input.GetInputSourceHandle(name, ref path) == EVRInputError.None ? path : 0; }
    private static void Check(EVROverlayError error, string operation) { if (error != EVROverlayError.None) throw new InvalidOperationException($"{operation}: {error}"); }
    public void Dispose()
    {
        Reset();
        foreach (var b in buttons)
        {
            if (b.Handle != 0 && b.Handle != OpenVR.k_ulOverlayHandleInvalid) overlay.DestroyOverlay(b.Handle);
            b.Handle = 0;
        }
    }
}
