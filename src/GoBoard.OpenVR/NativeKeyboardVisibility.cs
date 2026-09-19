using Valve.VR;

namespace GoBoard.Vr;

internal interface INativeKeyboardOverlays
{
    EVROverlayError FindOverlay(string key, ref ulong handle);
    bool IsOverlayVisible(ulong handle);
}

// These keys are SteamVR compatibility details, like DashboardFollower's bar
// key, not public SDK constants. Query current visibility rather than relying
// on open/close events: GoBoard can start after the keyboard was opened.
internal sealed class NativeKeyboardVisibility(INativeKeyboardOverlays overlays)
{
    internal const string SystemKeyboardKey = "system.keyboard";
    internal const string GamepadKeyboardKey = "valve.steam.gamepadui.keyboard";

    public NativeKeyboardVisibility(CVROverlay overlay) : this(new NativeOverlays(overlay)) { }

    public bool IsVisible() => IsVisible(SystemKeyboardKey) || IsVisible(GamepadKeyboardKey);

    private bool IsVisible(string key)
    {
        // Resolve each time: SteamVR can destroy/recreate keyboard overlays.
        // A missing keyboard (including one not created yet) is not visible.
        ulong handle = OpenVR.k_ulOverlayHandleInvalid;
        return overlays.FindOverlay(key, ref handle) == EVROverlayError.None &&
            handle != OpenVR.k_ulOverlayHandleInvalid && overlays.IsOverlayVisible(handle);
    }

    private sealed class NativeOverlays(CVROverlay overlay) : INativeKeyboardOverlays
    {
        public EVROverlayError FindOverlay(string key, ref ulong handle) => overlay.FindOverlay(key, ref handle);
        public bool IsOverlayVisible(ulong handle) => overlay.IsOverlayVisible(handle);
    }
}
