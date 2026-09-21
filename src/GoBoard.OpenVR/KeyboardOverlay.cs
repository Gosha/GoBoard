using System.Diagnostics;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using SkiaSharp;
using Valve.VR;

namespace GoBoard.Vr;

internal sealed class KeyboardOverlay(CVRSystem system, CVROverlay overlay, ulong handle, OverlayGraphics graphics,
    bool shortcutsOnly = false, WindowsKeyboard sharedOutput = null) : IDisposable
{
    private readonly WindowsKeyboard output = sharedOutput ?? new();
    private InputTarget observedTarget;
    private readonly KeyAudio audio = new();
    private KeyboardState keyboard;
    private readonly OverlayPointers pointers = new();
    private readonly TrackedDevicePose_t[] devices = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private bool enabled, faulted, resizing, geometryApplied;
    private string theme = BoardThemes.Default;
    private EffectSettings effects = new();
    private readonly AnimatedKeyboardRenderer renderer = new();
    private string status;
    private string targetStatus;
    private KeyboardGeometry geometry;
    private double lastErrorTime;
    private readonly ulong left = SourcePath("/user/hand/left"), right = SourcePath("/user/hand/right");
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    internal KeyboardState State => keyboard ??= new KeyboardState(output, shortcutsOnly);
    private double acceptAfter;
    // SteamVR's OpenGL sharing keeps the first submitted texture dimensions.
    // Keep the palette raster fixed and use texel aspect to display its logical
    // proportions. Full UV bounds retain the usual top/bottom orientation.
    internal static readonly SKImageInfo ShortcutTextureInfo = new(
        ProgrammableKeys.Width(new() { Columns = ProgrammableKeySettings.MaxColumns }) * Panel.RasterScale,
        ProgrammableKeys.Height(new() { Rows = ProgrammableKeySettings.MaxRows }) * Panel.RasterScale,
        SKColorType.Rgba8888, SKAlphaType.Unpremul);
    // Keep both the raster and physical overlay dimensions fixed. Center the
    // narrower keyboard in transparent padding so toggles only publish pixels,
    // never stretch the previous frame while SteamVR applies separate setters.
    internal static readonly SKImageInfo MainTextureInfo = new(
        OverlayGeometry.Width(true) * Panel.RasterScale, Panel.LayoutHeight * Panel.RasterScale,
        SKColorType.Rgba8888, SKAlphaType.Unpremul);
    private SKImageInfo TextureInfo => shortcutsOnly ? ShortcutTextureInfo : MainTextureInfo;
    internal static SKRect MainContentBounds(KeyboardState state)
    {
        var width = state.Width * Panel.RasterScale;
        var left = (MainTextureInfo.Width - width) / 2f;
        return new(left, 0, left + width, MainTextureInfo.Height);
    }
    internal static (float X, float Y) MainPointerPosition(float x, float y, KeyboardState state) =>
        ((x - MainContentBounds(state).Left) / Panel.RasterScale, y / Panel.RasterScale);

    internal static (float X, float Y) ShortcutPointerPosition(float x, float y, KeyboardState state) =>
        (x * state.Width / ShortcutTextureInfo.Width, y * state.Height / ShortcutTextureInfo.Height);

    public void ApplySettings(BoardSettings settings, bool resized)
    {
        effects = settings.Effects;
        theme = BoardThemes.Normalize(settings.Theme);
        if (resized) Cancel();
        if (!geometryApplied || (shortcutsOnly ? State.Shortcuts != settings.ProgrammableKeys : State.NumpadEnabled != settings.NumpadEnabled))
        {
            Cancel();
            acceptAfter = Now;
            if (shortcutsOnly) State.SetShortcuts(settings.ProgrammableKeys, Now);
            else State.SetNumpad(settings.NumpadEnabled, Now);
            ApplyGeometry();
        }
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

    private void ApplyGeometry()
    {
        // SteamVR applies texel aspect to ray intersection using the mouse
        // scale's aspect too. Match the fixed raster here, then convert
        // event coordinates to the current logical grid in Process.
        var scale = new HmdVector2_t { v0 = TextureInfo.Width, v1 = TextureInfo.Height };
        var bounds = shortcutsOnly ? new SKRect(0, 0, scale.v0, scale.v1) : MainContentBounds(State);
        var mask = new VROverlayIntersectionMaskPrimitive_t
        {
            m_nPrimitiveType = EVROverlayIntersectionMaskPrimitiveType.OverlayIntersectionPrimitiveType_Rectangle,
            m_Primitive = new VROverlayIntersectionMaskPrimitive_Data_t
            { m_Rectangle = new IntersectionMaskRectangle_t
                { m_flTopLeftX = bounds.Left, m_flTopLeftY = bounds.Top, m_flWidth = bounds.Width, m_flHeight = bounds.Height } }
        };
        var error = overlay.SetOverlayMouseScale(handle, ref scale);
        if (error != EVROverlayError.None) throw new InvalidOperationException($"Resize pointer coordinates: {error}");
        error = overlay.SetOverlayIntersectionMask(handle, ref mask, 1, (uint)Marshal.SizeOf<VROverlayIntersectionMaskPrimitive_t>());
        if (error != EVROverlayError.None) throw new InvalidOperationException($"Resize input region: {error}");
        error = overlay.SetOverlayTexelAspect(handle,
            shortcutsOnly ? State.Width * (float)TextureInfo.Height / (State.Height * TextureInfo.Width) : 1);
        if (error != EVROverlayError.None) throw new InvalidOperationException($"Resize keyboard proportions: {error}");
        geometryApplied = true;
    }

    public void PreviewSound(BoardSettings settings)
    {
        audio.Apply(settings);
        audio.Preview();
    }

    public void CancelPending() => Cancel();
    public void ReportError(string error) { status = error; lastErrorTime = Now; }

    public void BeginFrame(bool active, uint? grabbingController = null, bool isResizing = false)
    {
        if (resizing != isResizing)
        {
            resizing = isResizing;
            Cancel();
            State.SetResizing(resizing, Now);
        }
        var target = WindowsKeyboard.Foreground();
        if (target != observedTarget)
        {
            Cancel(clearFocus: false);
            output.Target = target;
            observedTarget = target;
            State.SetLayout(WindowsLayoutProvider.Get(target.Layout, geometry), Now);
            targetStatus = State.Layout.Notice;
            Console.WriteLine($"Windows input layout: {State.Layout.Name}, HKL {unchecked((uint)(long)target.Layout):X8}.");
            if (!faulted) status = targetStatus;
        }
        if (active != enabled) { Cancel(); acceptAfter = Now; }
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
        if (time <= acceptAfter) return;
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
        var (x, y) = shortcutsOnly ? ShortcutPointerPosition(e.data.mouse.x, e.data.mouse.y, State)
            : MainPointerPosition(e.data.mouse.x, e.data.mouse.y, State);
        try
        {
            switch (type)
            {
                case EVREventType.VREvent_FocusEnter:
                    State.Enter(pointer.Value, device, time);
                    break;
                case EVREventType.VREvent_FocusLeave:
                    State.Leave(pointer.Value, device, time);
                    audio.Cancel(pointer.Value);
                    break;
                case EVREventType.VREvent_MouseMove when e.eventAgeSeconds <= 0.20f:
                    // This controller-identified event was delivered to this
                    // overlay. The global hover query cannot identify its hand.
                    State.ObserveMotion(pointer.Value, device.Value, x, y, time, now, enabled);
                    break;
                case EVREventType.VREvent_MouseButtonDown when enabled && !faulted && e.data.mouse.button == (uint)EVRMouseButton.Left:
                    if (!State.Press(pointer.Value, device, x, y, time, now))
                        Console.WriteLine($"Keyboard down rejected: controller {device}, laser slot {slot}, age {e.eventAgeSeconds:F3}s.");
                    else audio.Click(pointerId: pointer.Value,
                        key: State.Hit(x, y));
                    break;
                case EVREventType.VREvent_MouseButtonUp when e.data.mouse.button == (uint)EVRMouseButton.Left:
                    if (State.Up(pointer.Value, device, time)) audio.Click(released: true, pointerId: pointer.Value);
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
                        { State.LoseDevice(device, Now); audio.Cancel(device); }
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
        State.SetNumLock(WindowsKeyboard.NumLock);
        if (State.SetShortcutStatus(status, Now))
        {
            audio.Cancel();
            acceptAfter = Now;
            ApplyGeometry();
        }
        using var bitmap = renderer.Render(State, shift, status, altGr, caps, scrollLock, theme, effects, Now,
            TextureInfo, shortcutsOnly ? null : MainContentBounds(State));
        if (bitmap == null) return;
        graphics.Upload(overlay, handle, bitmap);
    }

    private void Cancel(bool clearFocus = true)
    {
        audio.Cancel();
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
    public void Dispose() { Cancel(); renderer.Dispose(); if (sharedOutput == null) output.Dispose(); audio.Dispose(); }
}
