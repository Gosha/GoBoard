using System.Diagnostics;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Presentation.Skia;
using Valve.VR;

namespace GoBoard.Vr;

internal sealed class SettingsOverlay : IDisposable
{
    private readonly CVROverlay overlay;
    private readonly CVRSystem system;
    private readonly OverlayGraphics graphics;
    private readonly SettingsStore store;
    private readonly Action<BoardSettings> audition;
    private ulong handle, thumbnail;
    private readonly SettingsPointerState pointers = new();
    private readonly OverlayPointers identities = new();
    private BoardSettings drawn;
    private int drawnHover = -1;
    private string drawnError, actionError;
    private bool active;
    private double activeSince;
    private readonly ulong left = InputPath("/user/hand/left"), right = InputPath("/user/hand/right");
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public SettingsOverlay(CVRSystem system, CVROverlay overlay, OverlayGraphics graphics, SettingsStore store, Action<BoardSettings> audition)
    {
        this.system = system; this.overlay = overlay; this.graphics = graphics; this.store = store; this.audition = audition;
        try
        {
            Check(overlay.CreateDashboardOverlay("goboard.app.settings", "GoBoard Settings", ref handle, ref thumbnail), "Create settings tab");
            Check(overlay.SetOverlayWidthInMeters(handle, 1.35f), "Set settings width");
            var scale = new HmdVector2_t { v0 = SettingsControls.Width, v1 = SettingsControls.Height };
            Check(overlay.SetOverlayMouseScale(handle, ref scale), "Set settings pointer scale");
            Check(overlay.SetOverlayInputMethod(handle, VROverlayInputMethod.Mouse), "Enable settings input");
            Check(overlay.SetOverlayFlag(handle, VROverlayFlags.MultiCursor, true), "Enable both settings pointers");
            using var icon = SettingsPanel.Icon();
            graphics.Upload(overlay, thumbnail, icon);
            Draw();
        }
        catch { Dispose(); throw; }
    }

    public void Update()
    {
        var visible = overlay.IsDashboardVisible() && overlay.IsActiveDashboardOverlay(handle);
        if (visible != active)
        {
            pointers.Reset();
            activeSince = Now;
            active = visible;
        }
        var e = new VREvent_t();
        while (overlay.PollNextOverlayEvent(handle, ref e, (uint)Marshal.SizeOf<VREvent_t>()))
        {
            if (!active) continue;
            var type = (EVREventType)e.eventType;
            var focus = type is EVREventType.VREvent_FocusEnter or EVREventType.VREvent_FocusLeave;
            if (!focus && type is not (EVREventType.VREvent_MouseMove or EVREventType.VREvent_MouseButtonDown or EVREventType.VREvent_MouseButtonUp)) continue;
            var now = Now;
            var time = now - e.eventAgeSeconds;
            if (time < activeSince) continue;
            uint? device = Controller(e.trackedDeviceIndex);
            if (focus && !device.HasValue)
            {
                var path = e.data.overlay.devicePath;
                var role = path != 0 && path == left ? ETrackedControllerRole.LeftHand :
                    path != 0 && path == right ? ETrackedControllerRole.RightHand : ETrackedControllerRole.Invalid;
                if (role != ETrackedControllerRole.Invalid) device = Controller(system.GetTrackedDeviceIndexForControllerRole(role));
            }
            var identity = identities.Resolve(focus ? e.data.overlay.cursorIndex : e.data.mouse.cursorIndex, device,
                type == EVREventType.VREvent_FocusEnter);
            if (!identity.HasValue || type == EVREventType.VREvent_FocusEnter) continue;
            var down = type == EVREventType.VREvent_MouseButtonDown;
            var up = type == EVREventType.VREvent_MouseButtonUp;
            if ((down || up) && e.data.mouse.button != (uint)EVRMouseButton.Left) continue;
            var action = pointers.Process(identity.Value, e.data.mouse.x, SettingsControls.Height - e.data.mouse.y,
                time, now, down, up, type == EVREventType.VREvent_FocusLeave);
            if (action.HasValue && SettingsControls.Enabled(action.Value, store.Current))
            {
                var saved = store.Update(s => SettingsControls.Enabled(action.Value, s) ? SettingsControls.Apply(action.Value, s) : s);
                actionError = saved ? null : store.Error;
                if (saved && action.Value is SettingsAction.Wood or SettingsAction.Thud or SettingsAction.ToggleSound or SettingsAction.Quieter or SettingsAction.Louder)
                    audition(store.Current);
            }
        }
        // Thumbnail events must be drained too, even though the shell activates the tab.
        while (overlay.PollNextOverlayEvent(thumbnail, ref e, (uint)Marshal.SizeOf<VREvent_t>())) { }
        if (active) Draw();
    }

    private void Draw()
    {
        var hover = SettingsControls.All.Aggregate(0, (mask, c) => mask | (pointers.Hovered(c.Action) ? 1 << (int)c.Action : 0));
        var error = actionError ?? store.Error;
        if (drawn == store.Current && drawnHover == hover && drawnError == error) return;
        using var bitmap = SettingsPanel.Render(store.Current, pointers, error);
        graphics.Upload(overlay, handle, bitmap);
        drawn = store.Current; drawnHover = hover; drawnError = error;
    }

    private uint? Controller(uint device) => device < OpenVR.k_unMaxTrackedDeviceCount &&
        system.GetTrackedDeviceClass(device) == ETrackedDeviceClass.Controller ? device : null;
    private static ulong InputPath(string name)
    {
        ulong path = 0;
        return OpenVR.Input.GetInputSourceHandle(name, ref path) == EVRInputError.None ? path : 0;
    }
    private static void Check(EVROverlayError result, string operation)
    {
        if (result != EVROverlayError.None) throw new InvalidOperationException($"{operation}: {result}");
    }
    public void Dispose()
    {
        // SteamVR owns the dashboard thumbnail through the main handle.
        if (handle != 0) { overlay.DestroyOverlay(handle); handle = 0; }
    }
}
