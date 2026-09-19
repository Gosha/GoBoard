using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using Valve.VR;

namespace GoBoard.Vr;

// Separate launcher, palette and grab overlays. The main keyboard retains its
// dimensions. A moved palette keeps its own keyboard-relative pose.
internal sealed class ShortcutOverlays : IDisposable
{
    private readonly CVRSystem system;
    private readonly CVROverlay overlay;
    private readonly OverlayGraphics graphics;
    private readonly ulong main;
    private ulong button, panel, grip;
    private readonly KeyboardOverlay keyboard;
    private readonly GrabHandle grab;
    private readonly ShortcutLauncherState toggle = new();
    private readonly OverlayPointers pointers = new();
    private readonly TrackedDevicePose_t[] devices = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private readonly ulong left, right;
    private BoardSettings settings;
    private bool visible, panelVisible, placed, wasGrabbed;
    private int drawnToggle = -1;
    private Matrix4x4 panelOffset;
    private float scale = 1;
    private readonly Dictionary<ulong, (uint Device, ETrackingUniverseOrigin Origin, Matrix4x4 Pose)> transforms = new();
    private readonly Dictionary<ulong, float> widths = new();
    public uint? GrabOwner => grab?.ActiveGrab != null ? grab.Controller : null;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private float PanelWidth => keyboard.State.Width * ProgrammableKeys.MetersPerUnit * scale;
    private float PanelHeight => keyboard.State.Height * ProgrammableKeys.MetersPerUnit * scale;
    private Matrix4x4 GripOffset => Matrix4x4.CreateTranslation(0, -(PanelHeight / 2 + .025f), .002f);

    public ShortcutOverlays(CVRSystem system, CVROverlay overlay, OverlayGraphics graphics, ulong main, WindowsKeyboard output)
    {
        this.system = system; this.overlay = overlay; this.graphics = graphics; this.main = main;
        left = SourcePath("/user/hand/left"); right = SourcePath("/user/hand/right");
        try
        {
            Create("goboard.app.shortcuts.button", "GoBoard shortcuts button", 44, 44, .05f, ref button);
            var defaults = new ProgrammableKeySettings();
            Create("goboard.app.shortcuts", "GoBoard shortcuts", ProgrammableKeys.Width(defaults), ProgrammableKeys.Height(defaults), .2f, ref panel);
            Create("goboard.app.shortcuts.grab", "GoBoard shortcuts grab", GrabHandle.LayoutWidth, GrabHandle.LayoutHeight, GrabHandle.Width, ref grip);
            keyboard = new(system, overlay, panel, graphics, shortcutsOnly: true, sharedOutput: output);
            grab = new(system, overlay, grip, graphics);
            grab.Update(false, default);
        }
        catch { Dispose(); throw; }
    }
    private void Create(string key, string name, int width, int height, float meters, ref ulong handle)
    {
        Check(overlay.CreateOverlay(key, name, ref handle), "Create shortcut overlay");
        Check(overlay.SetOverlayWidthInMeters(handle, meters), "Set shortcut width");
        Check(overlay.SetOverlayFlag(handle, VROverlayFlags.VisibleInDashboard, true), "Show shortcut in dashboard");
        Check(overlay.SetOverlayFlag(handle, VROverlayFlags.NoBackside, false), "Shortcut backside");
        Check(overlay.SetOverlayFlag(handle, VROverlayFlags.MultiCursor, true), "Shortcut pointers");
        var mouse = new HmdVector2_t { v0 = width, v1 = height };
        Check(overlay.SetOverlayMouseScale(handle, ref mouse), "Shortcut coordinates");
        Check(overlay.SetOverlayInputMethod(handle, VROverlayInputMethod.Mouse), "Shortcut input");
    }
    public void ApplySettings(BoardSettings next)
    {
        if (settings == next) return;
        var reset = settings == null || settings.PositionResetId != next.PositionResetId;
        if (settings == null || settings.ProgrammableKeys != next.ProgrammableKeys || settings.SizePercent != next.SizePercent || reset)
        { grab.Update(false, default); toggle.Reset(Now, collapse: !next.ProgrammableKeys.Enabled); }
        settings = next; scale = next.Scale;
        keyboard.ApplySettings(next, resized: true);
        if (reset) placed = false;
        drawnToggle = -1;
    }
    public void Update(bool show, float currentScale, uint? mainGrab, bool resizing)
    {
        if (settings == null) return;
        scale = currentScale;
        show &= settings.ProgrammableKeys.Enabled;
        if (visible != show) { toggle.Reset(Now); visible = show; SetVisible(button, show); }
        if (!show)
        {
            ProcessButton(false); grab.Update(false, default);
            if (panelVisible) { SetVisible(panel, false); SetVisible(grip, false); panelVisible = false; }
            keyboard.BeginFrame(false);
            var hiddenEvent = new VREvent_t();
            while (overlay.PollNextOverlayEvent(panel, ref hiddenEvent, (uint)Marshal.SizeOf<VREvent_t>())) { }
            return;
        }
        var buttonSize = ProgrammableKeys.ToggleSize * ProgrammableKeys.MetersPerUnit * scale;
        var mainWidth = OverlayGeometry.PanelWidthInMeters * scale;
        var mainHeight = OverlayGeometry.PanelHeightInMeters * scale;
        var buttonOffset = Matrix4x4.CreateTranslation(-(mainWidth + buttonSize) / 2 - .012f,
            (mainHeight - buttonSize) / 2, .002f);
        SetWidth(button, buttonSize);
        Relative(button, main, buttonOffset);
        SetWidth(panel, PanelWidth);
        if (!placed)
        {
            panelOffset = Matrix4x4.CreateTranslation(buttonOffset.M41 - buttonSize / 2 - PanelWidth / 2 - .012f,
                mainHeight / 2 - PanelHeight / 2, .002f);
            placed = true;
        }
        ProcessButton(show && !resizing && !mainGrab.HasValue && GrabOwner == null);
        var expanded = show && toggle.Expanded;
        if (panelVisible != expanded)
        {
            panelVisible = expanded;
            SetVisible(panel, expanded); SetVisible(grip, expanded);
            if (!expanded) grab.Update(false, default);
        }
        var raw = new HmdMatrix34_t();
        var world = Matrix4x4.Identity;
        var poseValid = expanded && overlay.GetTransformForOverlayCoordinates(main, ETrackingUniverseOrigin.TrackingUniverseStanding,
            new HmdVector2_t { v0 = OverlayGeometry.PanelWidth / 2f, v1 = OverlayGeometry.PanelHeight / 2f }, ref raw) == EVROverlayError.None &&
            OpenVrPose.TryRigid(raw, out world);
        var moved = grab.Update(poseValid, panelOffset * world, interactive: !resizing && !mainGrab.HasValue);
        if (moved.HasValue && Matrix4x4.Invert(world, out var inverse)) panelOffset = moved.Value * inverse;
        if (grab.ActiveGrab is { } capture)
        {
            var pose = OpenVrPose.ToOpenVr(capture.ControllerOffset);
            Check(overlay.SetOverlayTransformTrackedDeviceRelative(panel, grab.Controller, ref pose), "Attach shortcut palette");
            pose = OpenVrPose.ToOpenVr(GripOffset * capture.ControllerOffset);
            Check(overlay.SetOverlayTransformTrackedDeviceRelative(grip, grab.Controller, ref pose), "Attach shortcut grip");
            transforms.Remove(panel); transforms.Remove(grip);
            wasGrabbed = true;
        }
        else
        {
            Relative(panel, main, panelOffset);
            Relative(grip, panel, GripOffset);
            if (wasGrabbed) { toggle.Reset(Now); wasGrabbed = false; }
        }
        keyboard.BeginFrame(expanded && poseValid, GrabOwner ?? mainGrab, resizing);
        var e = new VREvent_t();
        while (overlay.PollNextOverlayEvent(panel, ref e, (uint)Marshal.SizeOf<VREvent_t>()))
        {
            if ((EVREventType)e.eventType == EVREventType.VREvent_ImageFailed) throw new InvalidOperationException("Shortcut palette texture failed.");
            keyboard.Process(e);
        }
        keyboard.EndFrame();
        if (drawnToggle != toggle.Revision && show)
        {
            using var bitmap = ShortcutLauncherRenderer.Render(toggle.Expanded, toggle.Hovered, settings.Theme);
            graphics.Upload(overlay, button, bitmap); drawnToggle = toggle.Revision;
        }
    }
    private void ProcessButton(bool interactive)
    {
        if (!interactive) toggle.Reset(Now);
        if (interactive) system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, devices);
        if (toggle.CapturedDevice is { } captured && (!devices[captured].bDeviceIsConnected || !devices[captured].bPoseIsValid)) toggle.Reset(Now);
        var e = new VREvent_t();
        while (overlay.PollNextOverlayEvent(button, ref e, (uint)Marshal.SizeOf<VREvent_t>()))
        {
            var type = (EVREventType)e.eventType;
            if (type == EVREventType.VREvent_ImageFailed) throw new InvalidOperationException("Shortcut button texture failed.");
            if (!interactive) continue;
            var focus = type is EVREventType.VREvent_FocusEnter or EVREventType.VREvent_FocusLeave;
            if (!focus && type is not (EVREventType.VREvent_MouseMove or EVREventType.VREvent_MouseButtonDown or EVREventType.VREvent_MouseButtonUp)) continue;
            uint? device = e.trackedDeviceIndex < devices.Length && system.GetTrackedDeviceClass(e.trackedDeviceIndex) == ETrackedDeviceClass.Controller ? e.trackedDeviceIndex : null;
            if (focus && !device.HasValue)
            {
                var role = e.data.overlay.devicePath == left && left != 0 ? ETrackedControllerRole.LeftHand : e.data.overlay.devicePath == right && right != 0 ? ETrackedControllerRole.RightHand : ETrackedControllerRole.Invalid;
                if (role != ETrackedControllerRole.Invalid)
                {
                    var id = system.GetTrackedDeviceIndexForControllerRole(role);
                    if (id < devices.Length) device = id;
                }
            }
            var pointer = pointers.Resolve(focus ? e.data.overlay.cursorIndex : e.data.mouse.cursorIndex, device, type == EVREventType.VREvent_FocusEnter);
            if (!pointer.HasValue || type == EVREventType.VREvent_FocusEnter) continue;
            var down = type == EVREventType.VREvent_MouseButtonDown; var up = type == EVREventType.VREvent_MouseButtonUp;
            if ((down || up) && e.data.mouse.button != (uint)EVRMouseButton.Left) continue;
            var now = Now;
            toggle.Process(pointer.Value, pointer.Value, e.data.mouse.x, e.data.mouse.y, now - e.eventAgeSeconds, now, down, up,
                type == EVREventType.VREvent_FocusLeave);
        }
    }
    private void Relative(ulong child, ulong parent, Matrix4x4 offset)
    {
        var type = VROverlayTransformType.VROverlayTransform_Absolute;
        Check(overlay.GetOverlayTransformType(parent, ref type), "Read shortcut parent transform");
        var raw = new HmdMatrix34_t();
        var origin = ETrackingUniverseOrigin.TrackingUniverseStanding;
        var device = OpenVR.k_unTrackedDeviceIndexInvalid;
        if (type == VROverlayTransformType.VROverlayTransform_TrackedDeviceRelative)
            Check(overlay.GetOverlayTransformTrackedDeviceRelative(parent, ref device, ref raw), "Read shortcut parent controller");
        else Check(overlay.GetOverlayTransformAbsolute(parent, ref origin, ref raw), "Read shortcut parent pose");
        if (!OpenVrPose.TryRigid(raw, out var parentPose)) throw new InvalidOperationException("Invalid shortcut parent pose.");
        var transform = (device, origin, offset * parentPose);
        if (transforms.TryGetValue(child, out var previous) && previous == transform) return;
        var pose = OpenVrPose.ToOpenVr(transform.Item3);
        Check(device == OpenVR.k_unTrackedDeviceIndexInvalid ? overlay.SetOverlayTransformAbsolute(child, origin, ref pose) :
            overlay.SetOverlayTransformTrackedDeviceRelative(child, device, ref pose), "Place floating shortcut overlay");
        transforms[child] = transform;
    }
    private void SetWidth(ulong handle, float width)
    {
        if (widths.GetValueOrDefault(handle) == width) return;
        Check(overlay.SetOverlayWidthInMeters(handle, width), "Scale shortcuts"); widths[handle] = width;
    }
    private void SetVisible(ulong handle, bool value) => Check(value ? overlay.ShowOverlay(handle) : overlay.HideOverlay(handle), "Shortcut visibility");
    private static ulong SourcePath(string name) { ulong path = 0; return OpenVR.Input.GetInputSourceHandle(name, ref path) == EVRInputError.None ? path : 0; }
    private static void Check(EVROverlayError result, string operation) { if (result != EVROverlayError.None) throw new InvalidOperationException($"{operation}: {result}"); }
    public void Dispose()
    {
        keyboard?.Dispose();
        foreach (var handle in new[] { grip, panel, button }) if (handle != 0 && handle != OpenVR.k_ulOverlayHandleInvalid) overlay.DestroyOverlay(handle);
        grip = panel = button = 0;
    }
}
