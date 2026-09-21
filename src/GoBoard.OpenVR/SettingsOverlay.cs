using System.Diagnostics;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Presentation.Skia;
using GoBoard.Platform.Windows;
using Valve.VR;

namespace GoBoard.Vr;

internal sealed class SettingsOverlay : IDisposable
{
    private readonly CVROverlay overlay;
    private readonly CVRSystem system;
    private readonly OverlayGraphics graphics;
    private readonly SettingsEditor editor;
    private readonly Action<BoardSettings> audition;
    private ulong handle, thumbnail;
    private readonly OverlayPointers identities = new();
    private readonly AutostartController autostart = new();
    private AutostartState drawnAutostart;
    private BoardSettings drawn;
    private int drawnHover = -1;
    private string drawnError;
    private bool active;
    private double activeSince;
    private readonly ulong left = InputPath("/user/hand/left"), right = InputPath("/user/hand/right");
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public bool CloseRequested { get; private set; }

    public SettingsOverlay(CVRSystem system, CVROverlay overlay, OverlayGraphics graphics, SettingsStore store, Action<BoardSettings> audition)
    {
        this.system = system; this.overlay = overlay; this.graphics = graphics; this.audition = audition;
        editor = new(store, geometry => WindowsLayoutProvider.Get(WindowsKeyboard.Foreground().Layout, geometry));
        try
        {
            Check(overlay.CreateDashboardOverlay("goboard.app.settings", "GoBoard Settings", ref handle, ref thumbnail), "Create settings tab");
            Check(overlay.SetOverlayWidthInMeters(handle, 1.35f), "Set settings width");
            var scale = new HmdVector2_t { v0 = SettingsControls.Width, v1 = SettingsControls.Height };
            Check(overlay.SetOverlayMouseScale(handle, ref scale), "Set settings pointer scale");
            Check(overlay.SetOverlayInputMethod(handle, VROverlayInputMethod.Mouse), "Enable settings input");
            Check(overlay.SetOverlayFlag(handle, VROverlayFlags.MultiCursor, true), "Enable both settings pointers");
            Check(overlay.SetOverlayFlag(handle, VROverlayFlags.EnableControlBarClose, true), "Enable dashboard Close action");
            using var icon = SettingsPanel.Icon();
            graphics.Upload(overlay, thumbnail, icon);
            Draw();
        }
        catch { Dispose(); throw; }
    }

    public void Update()
    {
        editor.Refresh();
        var visible = overlay.IsDashboardVisible() && overlay.IsActiveDashboardOverlay(handle);
        if (visible != active)
        {
            editor.Reset();
            activeSince = Now;
            active = visible;
        }
        var e = new VREvent_t();
        if (active) autostart.Update(Now);
        while (overlay.PollNextOverlayEvent(handle, ref e, (uint)Marshal.SizeOf<VREvent_t>()))
        {
            var type = (EVREventType)e.eventType;
            // The dashboard hover action also works while another tab is selected.
            if (type == EVREventType.VREvent_OverlayClosed) { CloseRequested = true; return; }
            if (!active) continue;
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
            var effect = editor.Process(identity.Value, e.data.mouse.x, SettingsControls.Height - e.data.mouse.y,
                time, now, down, up, type == EVREventType.VREvent_FocusLeave);
            if (effect == SettingsEditorEffect.ToggleAutostart) autostart.Toggle();
            else if (effect == SettingsEditorEffect.AuditionSound) audition(editor.Current);
        }
        // Thumbnail events must be drained too, even though the shell activates the tab.
        while (overlay.PollNextOverlayEvent(thumbnail, ref e, (uint)Marshal.SizeOf<VREvent_t>()))
        {
            if ((EVREventType)e.eventType == EVREventType.VREvent_OverlayClosed) { CloseRequested = true; return; }
        }
        if (active) Draw();
    }

    private void Draw()
    {
        var hover = editor.Pointers.Revision;
        var error = editor.Error;
        if (drawn == editor.Current && drawnHover == hover && drawnError == error && drawnAutostart == autostart.State) return;
        using var bitmap = SettingsPanel.Render(editor.Current, editor.Pointers, error, autostart: autostart.State);
        graphics.Upload(overlay, handle, bitmap);
        drawn = editor.Current; drawnHover = hover; drawnError = error;
        drawnAutostart = autostart.State;
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
