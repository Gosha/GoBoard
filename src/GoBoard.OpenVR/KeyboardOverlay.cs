using System.Diagnostics;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using Valve.VR;

namespace GoBoard.Vr;

internal sealed class KeyboardOverlay(CVRSystem system, CVROverlay overlay, ulong handle, OverlayGraphics graphics) : IDisposable
{
    private readonly WindowsKeyboard output = new();
    private readonly KeyAudio audio = new();
    private KeyboardState keyboard;
    private readonly OverlayPointers pointers = new();
    private readonly TrackedDevicePose_t[] devices = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private bool enabled, faulted, resizing;
    private string theme = BoardThemes.Default;
    private EffectSettings effects = new();
    private readonly AnimatedKeyboardRenderer renderer = new();
    private string status;
    private string targetStatus;
    private KeyboardGeometry geometry;
    private double lastErrorTime;
    private readonly ulong left = SourcePath("/user/hand/left"), right = SourcePath("/user/hand/right");
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private KeyboardState State => keyboard ??= new KeyboardState(output);

    public void ApplySettings(BoardSettings settings, bool resized)
    {
        effects = settings.Effects;
        theme = BoardThemes.Normalize(settings.Theme);
        if (resized) Cancel();
        if (geometry != settings.Geometry)
        {
            Cancel();
            geometry = settings.Geometry;
            State.SetLayout(WindowsLayoutProvider.Get(output.Target.Layout, geometry), Now);
            targetStatus = State.Layout.Notice;
            if (!faulted) status = targetStatus;
        }
        audio.Apply(settings);
    }

    public void PreviewSound(BoardSettings settings)
    {
        audio.Apply(settings);
        audio.Click();
    }

    public void BeginFrame(bool active, uint? grabbingController = null, bool isResizing = false)
    {
        if (resizing != isResizing)
        {
            resizing = isResizing;
            Cancel();
            State.SetResizing(resizing, Now);
        }
        var target = WindowsKeyboard.Foreground();
        if (target != output.Target)
        {
            Cancel(clearFocus: false);
            output.Target = target;
            State.SetLayout(WindowsLayoutProvider.Get(target.Layout, geometry), Now);
            targetStatus = State.Layout.Notice;
            Console.WriteLine($"Windows input layout: {State.Layout.Name}, HKL {unchecked((uint)(long)target.Layout):X8}.");
            if (!faulted) status = targetStatus;
        }
        if (!active && enabled) Cancel(clearFocus: false);
        enabled = active;
        State.SetGrabOwner(grabbingController, Now);
        if (faulted)
        {
            // Retry failed key-ups; never accept another press with unresolved ownership.
            try { output.ReleaseAll(); faulted = false; }
            catch { return; }
        }
        if (Now - lastErrorTime > 4) status = targetStatus;
    }

    public void Process(VREvent_t e)
    {
        var now = Now;
        var time = now - e.eventAgeSeconds;
        var device = Controller(e.trackedDeviceIndex);
        var type = (EVREventType)e.eventType;
        if (type is not (EVREventType.VREvent_FocusEnter or EVREventType.VREvent_FocusLeave or
            EVREventType.VREvent_MouseMove or EVREventType.VREvent_MouseButtonDown or EVREventType.VREvent_MouseButtonUp)) return;
        var focusEvent = type is EVREventType.VREvent_FocusEnter or EVREventType.VREvent_FocusLeave;
        if (focusEvent) device ??= FocusDevice(e.data.overlay.devicePath);
        var slot = focusEvent ? e.data.overlay.cursorIndex : e.data.mouse.cursorIndex;
        var pointer = pointers.Resolve(slot, device, type == EVREventType.VREvent_FocusEnter);
        if (!pointer.HasValue)
        {
            if (type is EVREventType.VREvent_MouseButtonDown or EVREventType.VREvent_MouseButtonUp)
                Console.WriteLine($"Keyboard edge ignored: no unambiguous controller for slot {slot}, raw device {e.trackedDeviceIndex}.");
            return;
        }
        device = pointer;
        try
        {
            switch (type)
            {
                case EVREventType.VREvent_FocusEnter:
                    State.Enter(pointer.Value, device, time);
                    break;
                case EVREventType.VREvent_FocusLeave:
                    State.Leave(pointer.Value, device, time);
                    break;
                case EVREventType.VREvent_MouseMove when e.eventAgeSeconds <= 0.20f:
                    // This controller-identified event was delivered to this
                    // overlay. The global hover query cannot identify its hand.
                    State.ObserveMotion(pointer.Value, device.Value, e.data.mouse.x, e.data.mouse.y, time, now, enabled);
                    break;
                case EVREventType.VREvent_MouseButtonDown when enabled && !faulted && e.data.mouse.button == (uint)EVRMouseButton.Left:
                    if (!State.Press(pointer.Value, device, e.data.mouse.x, e.data.mouse.y, time, now))
                        Console.WriteLine($"Keyboard down rejected: controller {device}, laser slot {slot}, age {e.eventAgeSeconds:F3}s.");
                    else audio.Click();
                    break;
                case EVREventType.VREvent_MouseButtonUp when e.data.mouse.button == (uint)EVRMouseButton.Left:
                    if (State.Up(pointer.Value, device, time)) audio.Click(released: true);
                    break;
            }
        }
        catch (Exception ex) { Failed(ex); }
    }

    public void EndFrame()
    {
        if (!enabled) return;
        if (!faulted)
        {
            try
            {
                // Focus can change between event draining and key-repeat processing.
                if (WindowsKeyboard.Foreground() != output.Target) Cancel(clearFocus: false);
                if (State.HasHeldKeys)
                {
                    system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, devices);
                    foreach (var device in State.FocusedDevices)
                        if (device >= devices.Length || !devices[device].bDeviceIsConnected || !devices[device].bPoseIsValid)
                            State.LoseDevice(device, Now);
                    State.Tick(Now);
                }
            }
            catch (Exception ex) { Failed(ex); }
        }
        var shift = State.Shift || WindowsKeyboard.PhysicalShift;
        var altGr = State.AltGr || WindowsKeyboard.PhysicalAltGr ||
            (State.Mode(0x1d) != ModifierMode.Idle && State.Mode(0x38) != ModifierMode.Idle);
        var caps = WindowsKeyboard.CapsLock;
        var scrollLock = WindowsKeyboard.ScrollLock;
        using var bitmap = renderer.Render(State, shift, status, altGr, caps, scrollLock, theme, effects, Now);
        if (bitmap == null) return;
        graphics.Upload(overlay, handle, bitmap);
    }

    private void Cancel(bool clearFocus = true)
    {
        try { State.Cancel(Now, clearFocus); output.ReleaseAll(); }
        catch (Exception ex) { faulted = true; status = ex.Message; lastErrorTime = Now; Console.Error.WriteLine(status); }
    }
    private void Failed(Exception ex)
    {
        Console.Error.WriteLine($"Keyboard input: {ex.Message}");
        Cancel(clearFocus: false);
        status = ex.Message;
        lastErrorTime = Now;
    }
    private uint? Controller(uint device) => device < OpenVR.k_unMaxTrackedDeviceCount &&
        system.GetTrackedDeviceClass(device) == ETrackedDeviceClass.Controller ? device : null;
    private uint? FocusDevice(ulong path)
    {
        var role = path != 0 && path == left ? ETrackedControllerRole.LeftHand :
            path != 0 && path == right ? ETrackedControllerRole.RightHand : ETrackedControllerRole.Invalid;
        return role == ETrackedControllerRole.Invalid ? null : Controller(system.GetTrackedDeviceIndexForControllerRole(role));
    }
    private static ulong SourcePath(string name)
    {
        ulong path = 0;
        return OpenVR.Input.GetInputSourceHandle(name, ref path) == EVRInputError.None ? path : 0;
    }
    public void Dispose() { Cancel(); renderer.Dispose(); output.Dispose(); audio.Dispose(); }
}
