using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Presentation.Skia;
using Valve.VR;

namespace GoBoard.Vr;

internal sealed class GrabHandle(CVRSystem system, CVROverlay overlay, ulong handle, OverlayGraphics graphics)
{
    public const int LayoutWidth = OverlayGeometry.GrabWidth, LayoutHeight = OverlayGeometry.GrabHeight;
    public const float Width = OverlayGeometry.GrabWidthInMeters;
    private readonly TrackedDevicePose_t[] devices = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private readonly GrabInput input = new();
    private readonly OverlayPointers pointers = new();
    private GrabInput.Press committedPress;
    private readonly ulong leftPath = InputPath("/user/hand/left");
    private readonly ulong rightPath = InputPath("/user/hand/right");
    private GrabPose grab;
    private uint owner;
    private bool watchTrigger;
    private int drawnState = -1;
    private readonly Dictionary<(uint Slot, uint Device), double> lastMotionLog = new();
    private Matrix4x4? displayedPanel;
    private bool interactiveLastFrame;
    private double acceptAfter;
    private readonly float displayFrequency = ReadDisplayProperty(system, ETrackedDeviceProperty.Prop_DisplayFrequency_Float);
    private readonly float vsyncToPhotons = ReadDisplayProperty(system, ETrackedDeviceProperty.Prop_SecondsFromVsyncToPhotons_Float);
    private readonly bool traceGrab = Environment.GetEnvironmentVariable("GOBOARD_TRACE_GRAB") == "1";
    public GrabPose ActiveGrab => grab;
    public uint Controller => owner;

    public Matrix4x4? Update(bool visible, Matrix4x4 panel, bool interactive = true)
    {
        interactive &= visible;
        if (interactive != interactiveLastFrame)
        {
            acceptAfter = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            interactiveLastFrame = interactive;
        }
        if (!interactive) { End("input suspended"); input.Reset(); }
        if (visible) system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, PredictionSeconds(), devices);
        else { End("dashboard hidden"); input.Reset(); displayedPanel = null; }

        Matrix4x4? result = grab != null && TryPose(owner, out var currentHand) ? grab.Update(currentHand) : null;
        var e = new VREvent_t();
        while (overlay.PollNextOverlayEvent(handle, ref e, (uint)Marshal.SizeOf<VREvent_t>()))
        {
            var type = (EVREventType)e.eventType;
            if (type == EVREventType.VREvent_ImageFailed) throw new InvalidOperationException("SteamVR failed to load the grab handle.");
            if (!interactive) continue;
            if (type is not (EVREventType.VREvent_FocusEnter or EVREventType.VREvent_FocusLeave or
                EVREventType.VREvent_MouseMove or EVREventType.VREvent_MouseButtonDown or EVREventType.VREvent_MouseButtonUp)) continue;
            var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            var time = now - e.eventAgeSeconds;
            if (!double.IsFinite(time) || time <= acceptAfter) continue;
            var device = EventDevice(e);
            var focusEvent = type is EVREventType.VREvent_FocusEnter or EVREventType.VREvent_FocusLeave;
            if (focusEvent) device ??= FocusDevice(e.data.overlay.devicePath);
            var slot = focusEvent ? e.data.overlay.cursorIndex : e.data.mouse.cursorIndex;
            var pointer = pointers.Resolve(slot, device, type == EVREventType.VREvent_FocusEnter);
            if (traceGrab && type == EVREventType.VREvent_MouseMove &&
                (!lastMotionLog.TryGetValue((slot, e.trackedDeviceIndex), out var last) || now - last > .5))
            {
                Console.WriteLine($"Grab motion: slot {slot}, raw device {e.trackedDeviceIndex}, resolved {pointer?.ToString() ?? "unknown"}, point {e.data.mouse.x:F1},{e.data.mouse.y:F1}, age {e.eventAgeSeconds:F3}s.");
                lastMotionLog[(slot, e.trackedDeviceIndex)] = now;
            }
            if (!pointer.HasValue)
            {
                if (type is EVREventType.VREvent_MouseButtonDown or EVREventType.VREvent_MouseButtonUp)
                    Console.WriteLine($"Grab edge ignored: no unambiguous controller for slot {slot}, raw device {e.trackedDeviceIndex}.");
                continue;
            }
            device = pointer;
            switch (type)
            {
                case EVREventType.VREvent_FocusEnter:
                    input.Enter(pointer.Value, device, time);
                    if (traceGrab) Console.WriteLine($"Grab focus entered: cursor {e.data.overlay.cursorIndex}, device {device?.ToString() ?? "unknown"}, raw device {e.trackedDeviceIndex}, path {e.data.overlay.devicePath}.");
                    break;
                case EVREventType.VREvent_FocusLeave:
                    input.Leave(pointer.Value, device, time);
                    if (traceGrab) Console.WriteLine($"Grab focus left: cursor {e.data.overlay.cursorIndex}, device {device?.ToString() ?? "unknown"}.");
                    break;
                case EVREventType.VREvent_MouseButtonDown when e.data.mouse.button == (uint)EVRMouseButton.Left:
                    var rejection = input.Down(pointer.Value, device, e.data.mouse.x, e.data.mouse.y, time, now);
                    if (traceGrab || rejection != null) Console.WriteLine($"Grab down: cursor {e.data.mouse.cursorIndex}, raw device {e.trackedDeviceIndex}, age {e.eventAgeSeconds:F3}s, point {e.data.mouse.x:F1},{e.data.mouse.y:F1}; {rejection ?? "pending target validation"}.");
                    break;
                case EVREventType.VREvent_MouseButtonUp when e.data.mouse.button == (uint)EVRMouseButton.Left:
                    input.Up(pointer.Value, device, time);
                    if (traceGrab) Console.WriteLine($"Grab up: cursor {e.data.mouse.cursorIndex}, raw device {e.trackedDeviceIndex}, age {e.eventAgeSeconds:F3}s.");
                    break;
                case EVREventType.VREvent_MouseMove:
                    input.ObserveMotion(pointer.Value, device.Value, e.data.mouse.x, e.data.mouse.y, time, now, visible);
                    break;
            }
        }

        // Controller-specific enter/motion/leave/up events own focus. The
        // overlay-wide hover query can change when the OTHER hand types and
        // must not clear this hand's focus or cancel its captured grab.
        // Drain the whole queue first so a queued owner leave/release wins.
        if (input.Active == null) End("pointer released or focus lost");
        else if (committedPress != input.Active)
        {
            var press = input.Active;
            if (!TryPose(press.Device, out var hand)) End("press has no tracked controller");
            else if (Trigger(press.Device, out var held) && !held) End("trigger already released");
            else
            {
                owner = press.Device;
                committedPress = press;
                grab = new GrabPose(result ?? displayedPanel ?? panel, hand);
                watchTrigger = Trigger(owner, out held) && held;
                Console.WriteLine($"Grab started: controller {owner}, cursor {press.Cursor}, verified handle focus, trigger polling {watchTrigger}.");
            }
        }

        if (grab != null)
        {
            if (!TryPose(owner, out var hand)) End("controller tracking lost");
            else
            {
                var valid = Trigger(owner, out var held);
                if (watchTrigger && (!valid || !held)) End("trigger released or unavailable");
                else
                {
                    watchTrigger |= valid && held;
                    result = grab.Update(hand);
                }
            }
        }
        var state = grab != null ? 2 : input.HasFocus && interactive ? 1 : 0;
        if (drawnState != state)
        {
            using var bitmap = GrabHandleRenderer.Render(state);
            graphics.Upload(overlay, handle, bitmap);
            drawnState = state;
        }
        if (visible) displayedPanel = result ?? panel;
        return result;
    }

    private uint? EventDevice(VREvent_t e) => IsController(e.trackedDeviceIndex) ? e.trackedDeviceIndex : null;

    private uint? FocusDevice(ulong path)
    {
        // A focus event may identify its hand through devicePath instead of the
        // legacy trackedDeviceIndex. Never guess from the global dashboard device.
        var role = path != 0 && path == leftPath ? ETrackedControllerRole.LeftHand :
            path != 0 && path == rightPath ? ETrackedControllerRole.RightHand : ETrackedControllerRole.Invalid;
        if (role == ETrackedControllerRole.Invalid) return null;
        var device = system.GetTrackedDeviceIndexForControllerRole(role);
        return IsController(device) ? device : null;
    }

    private static ulong InputPath(string path)
    {
        ulong value = 0;
        return OpenVR.Input.GetInputSourceHandle(path, ref value) == EVRInputError.None ? value : 0;
    }

    private float PredictionSeconds()
    {
        float since = 0;
        ulong frame = 0;
        if (displayFrequency <= 0 || !system.GetTimeSinceLastVsync(ref since, ref frame)) return 0;
        // Only for capture/release snapshots. The compositor owns held movement.
        return Math.Clamp(1 / displayFrequency + vsyncToPhotons - since, 0, 0.05f);
    }

    private static float ReadDisplayProperty(CVRSystem system, ETrackedDeviceProperty property)
    {
        var error = ETrackedPropertyError.TrackedProp_Success;
        var value = system.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, property, ref error);
        return error == ETrackedPropertyError.TrackedProp_Success && float.IsFinite(value) && value >= 0 ? value : 0;
    }

    private bool IsController(uint device) => device < devices.Length && system.GetTrackedDeviceClass(device) == ETrackedDeviceClass.Controller;

    private bool TryPose(uint device, out Matrix4x4 pose)
    {
        pose = default;
        return IsController(device) && devices[device].bDeviceIsConnected && devices[device].bPoseIsValid &&
            OpenVrPose.TryRigid(devices[device].mDeviceToAbsoluteTracking, out pose);
    }

    private bool Trigger(uint device, out bool held)
    {
        var state = new VRControllerState_t();
        var valid = system.GetControllerState(device, ref state, (uint)Marshal.SizeOf<VRControllerState_t>());
        held = (state.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_SteamVR_Trigger)) != 0;
        return valid;
    }

    private void End(string reason)
    {
        if (grab != null) Console.WriteLine($"Grab ended: {reason}; dashboard-relative placement retained.");
        grab = null;
        committedPress = null;
        input.Cancel();
        watchTrigger = false;
    }

    private static void Check(EVROverlayError error, string operation)
    {
        if (error != EVROverlayError.None) throw new InvalidOperationException($"{operation}: {error}");
    }
}

