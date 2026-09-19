using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Presentation.Skia;
using Valve.VR;

namespace GoBoard.Vr;

internal sealed class ResizeHandle : IDisposable
{
    private readonly CVRSystem system;
    private readonly CVROverlay overlay;
    private readonly OverlayGraphics graphics;
    private readonly SettingsStore settings;
    private readonly ResizeSession session;
    private readonly GrabInput input = new(OverlayGeometry.ResizeSize, OverlayGeometry.ResizeSize,
        retainCaptureOnLeave: true, hitTest: OverlayGeometry.ResizeHit);
    private readonly OverlayPointers pointers = new();
    private readonly TrackedDevicePose_t[] devices = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private readonly ulong left = SourcePath("/user/hand/left"), right = SourcePath("/user/hand/right");
    private GrabInput.Press committedPress;
    private bool enabled;
    private double acceptAfter;
    private int drawnState = -1;
    private ulong handle = OpenVR.k_ulOverlayHandleInvalid;
    public ulong Handle => handle;
    public bool Active => session.Active;
    public float Scale => session.Scale;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public ResizeHandle(CVRSystem system, CVROverlay overlay, OverlayGraphics graphics, SettingsStore settings)
    {
        this.system = system; this.overlay = overlay; this.graphics = graphics; this.settings = settings;
        session = new(settings);
        try
        {
            Check(overlay.CreateOverlay("goboard.app.resize", "GoBoard resize", ref handle), "Create resize handle");
            Check(overlay.SetOverlayWidthInMeters(handle, OverlayGeometry.ResizeSizeInMeters), "Set resize handle width");
            Check(overlay.SetOverlayFlag(handle, VROverlayFlags.VisibleInDashboard, true), "Allow resize handle alongside dashboard");
            Check(overlay.SetOverlayFlag(handle, VROverlayFlags.NoBackside, false), "Enable resize handle backside");
            var mouseScale = new HmdVector2_t { v0 = OverlayGeometry.ResizeSize, v1 = OverlayGeometry.ResizeSize };
            Check(overlay.SetOverlayMouseScale(handle, ref mouseScale), "Set resize pointer dimensions");
            var masks = OverlayGeometry.ResizeMaskTargets.Select(bounds => new VROverlayIntersectionMaskPrimitive_t
            {
                m_nPrimitiveType = EVROverlayIntersectionMaskPrimitiveType.OverlayIntersectionPrimitiveType_Rectangle,
                m_Primitive = new VROverlayIntersectionMaskPrimitive_Data_t
                {
                    m_Rectangle = new IntersectionMaskRectangle_t
                    {
                        m_flTopLeftX = bounds.X, m_flTopLeftY = bounds.Y,
                        m_flWidth = bounds.Width, m_flHeight = bounds.Height
                    }
                }
            }).ToArray();
            // The generated binding exposes the first primitive by reference;
            // pin the complete contiguous array while native code reads both.
            var pinned = GCHandle.Alloc(masks, GCHandleType.Pinned);
            try
            {
                Check(overlay.SetOverlayIntersectionMask(handle, ref masks[0], (uint)masks.Length,
                    (uint)Marshal.SizeOf<VROverlayIntersectionMaskPrimitive_t>()), "Set corner resize target");
            }
            finally { pinned.Free(); }
            Check(overlay.SetOverlayInputMethod(handle, VROverlayInputMethod.Mouse), "Enable resize input");
            Check(overlay.SetOverlayFlag(handle, VROverlayFlags.MultiCursor, true), "Enable both resize pointers");
            Draw(0);
        }
        catch { Dispose(); throw; }
    }

    public void Update(bool visible, Matrix4x4 panel, bool canStart = true)
    {
        if (session.Synchronize()) End();
        var interactive = visible && canStart;
        if (!interactive) End();
        if (interactive != enabled) { acceptAfter = Now; enabled = interactive; }
        if (interactive)
            system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, devices);

        var released = false;
        var e = new VREvent_t();
        while (overlay.PollNextOverlayEvent(handle, ref e, (uint)Marshal.SizeOf<VREvent_t>()))
        {
            var type = (EVREventType)e.eventType;
            if (type == EVREventType.VREvent_ImageFailed) throw new InvalidOperationException("SteamVR failed to load the resize handle.");
            var now = Now;
            var time = now - e.eventAgeSeconds;
            if (!interactive || !double.IsFinite(time) || time <= acceptAfter || time > now + .001) continue;
            if (type is not (EVREventType.VREvent_FocusEnter or EVREventType.VREvent_FocusLeave or
                EVREventType.VREvent_MouseMove or EVREventType.VREvent_MouseButtonDown or EVREventType.VREvent_MouseButtonUp)) continue;
            var focus = type is EVREventType.VREvent_FocusEnter or EVREventType.VREvent_FocusLeave;
            uint? device = Controller(e.trackedDeviceIndex);
            if (focus) device ??= FocusDevice(e.data.overlay.devicePath);
            var pointer = pointers.Resolve(focus ? e.data.overlay.cursorIndex : e.data.mouse.cursorIndex,
                device, type == EVREventType.VREvent_FocusEnter);
            if (!pointer.HasValue) continue;
            device = pointer;
            switch (type)
            {
                case EVREventType.VREvent_FocusEnter:
                    input.Enter(pointer.Value, device, time);
                    break;
                case EVREventType.VREvent_FocusLeave:
                    // The resize owns its controller until release, even outside the small target.
                    input.Leave(pointer.Value, device, time);
                    break;
                case EVREventType.VREvent_MouseMove:
                    input.ObserveMotion(pointer.Value, device.Value, e.data.mouse.x, e.data.mouse.y, time, now, true);
                    break;
                case EVREventType.VREvent_MouseButtonDown when e.data.mouse.button == (uint)EVRMouseButton.Left:
                    input.Down(pointer.Value, device, e.data.mouse.x, e.data.mouse.y, time, now);
                    break;
                case EVREventType.VREvent_MouseButtonUp when e.data.mouse.button == (uint)EVRMouseButton.Left:
                    var before = input.Active;
                    input.Up(pointer.Value, device, time);
                    released |= committedPress != null && before == committedPress && input.Active == null;
                    break;
            }
        }

        if (Active)
        {
            // Tracking loss cancels, whereas a verified owner release commits.
            var tracked = TryPose(committedPress.Device, out var hand);
            var triggerAvailable = Trigger(committedPress.Device, out var held);
            var result = session.Track(panel, tracked ? hand : null, triggerAvailable, held,
                released, input.Active == committedPress);
            if (result != ResizeResult.Active)
            {
                if (result == ResizeResult.SaveFailed) Console.Error.WriteLine(settings.Error);
                Console.WriteLine(result == ResizeResult.Completed ? $"Resize finished: {settings.Current.SizePercent}%." :
                    "Resize cancelled; saved size restored.");
                End();
            }
        }
        else if (interactive && input.Active is { } press)
        {
            // Drain all events before capture: a queued down/up must never start a drag.
            var triggerAvailable = Trigger(press.Device, out var held);
            if (TryPose(press.Device, out var hand) &&
                session.Begin(panel, hand, press.X, press.Y, triggerAvailable, held))
            {
                committedPress = press;
                Console.WriteLine($"Resize started: controller {press.Device}; keyboard center fixed, trigger polling {triggerAvailable}.");
            }
            else End();
        }
        Draw(Active ? 2 : interactive && input.HasFocus ? 1 : 0);
    }

    private void End()
    {
        var wasActive = Active;
        session.Cancel();
        input.Reset();
        committedPress = null;
        acceptAfter = Now;
        if (wasActive) Console.WriteLine("Resize cancelled; saved size restored.");
    }

    private uint? Controller(uint device) => device < devices.Length &&
        system.GetTrackedDeviceClass(device) == ETrackedDeviceClass.Controller ? device : null;

    private uint? FocusDevice(ulong path)
    {
        var role = path != 0 && path == left ? ETrackedControllerRole.LeftHand :
            path != 0 && path == right ? ETrackedControllerRole.RightHand : ETrackedControllerRole.Invalid;
        return role == ETrackedControllerRole.Invalid ? null : Controller(system.GetTrackedDeviceIndexForControllerRole(role));
    }

    private bool TryPose(uint device, out Matrix4x4 pose)
    {
        pose = default;
        return Controller(device).HasValue && devices[device].bDeviceIsConnected && devices[device].bPoseIsValid &&
            OpenVrPose.TryRigid(devices[device].mDeviceToAbsoluteTracking, out pose);
    }

    private bool Trigger(uint device, out bool held)
    {
        var state = new VRControllerState_t();
        var valid = system.GetControllerState(device, ref state, (uint)Marshal.SizeOf<VRControllerState_t>());
        held = (state.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_SteamVR_Trigger)) != 0;
        return valid;
    }

    private static ulong SourcePath(string name)
    {
        ulong path = 0;
        return OpenVR.Input.GetInputSourceHandle(name, ref path) == EVRInputError.None ? path : 0;
    }

    private void Draw(int state)
    {
        if (drawnState == state) return;
        using var bitmap = ResizeHandleRenderer.Render(state);
        graphics.Upload(overlay, handle, bitmap);
        drawnState = state;
    }

    private static void Check(EVROverlayError error, string operation)
    {
        if (error != EVROverlayError.None) throw new InvalidOperationException($"{operation}: {error}");
    }

    public void Dispose()
    {
        session.Cancel();
        if (handle != OpenVR.k_ulOverlayHandleInvalid)
        {
            overlay.DestroyOverlay(handle);
            handle = OpenVR.k_ulOverlayHandleInvalid;
        }
    }
}
