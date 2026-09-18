using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;
using SteamVR.StereoDashboard;
using Valve.VR;

const string overlayKey = "goboard.experiment.stereo-dashboard";
ulong main = OpenVR.k_ulOverlayHandleInvalid;
ulong thumbnail = OpenVR.k_ulOverlayHandleInvalid;
bool initialized = false;
using var cancel = new CancellationTokenSource();
ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancel.Cancel(); };
Console.CancelKeyPress += onCancel;
try
{
    string renderPath = null;
    string stopFile = null;
    double seconds = double.PositiveInfinity;
    bool show = false;
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--render" when i + 1 < args.Length: renderPath = args[++i]; break;
            case "--stop-file" when i + 1 < args.Length: stopFile = args[++i]; break;
            case "--show": show = true; break;
            case "--seconds" when i + 1 < args.Length:
                seconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
                if (!double.IsFinite(seconds) || seconds <= 0) throw new ArgumentException("Seconds must be positive and finite.");
                break;
            default: throw new ArgumentException("Usage: SteamVR.StereoDashboard [--show] [--seconds N] [--stop-file PATH] [--render PATH]");
        }
    }

    using var panel = StereoPanel.Render();
    if (renderPath != null)
    {
        using var image = SKImage.FromBitmap(panel);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(renderPath);
        png.SaveTo(file);
        Console.WriteLine($"Rendered left | right stereo pair ({panel.Width}x{panel.Height}) to {renderPath}");
        return 0;
    }

    if (!OpenVR.IsRuntimeInstalled()) throw new InvalidOperationException("SteamVR is not installed.");
    var error = EVRInitError.None;
    var system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
    if (error != EVRInitError.None) throw new InvalidOperationException($"OpenVR: {error}. Start SteamVR and connect the headset.");
    initialized = true;
    var overlay = OpenVR.Overlay ?? throw new InvalidOperationException("OpenVR overlay interface is unavailable.");
    Check(overlay.CreateDashboardOverlay(overlayKey, "Stereo Experiment", ref main, ref thumbnail), "Create dashboard tab (is another copy running?)");
    Check(overlay.SetOverlayWidthInMeters(main, 1.2f), "Set panel width");
    Check(overlay.SetOverlayFlag(main, VROverlayFlags.SideBySide_Parallel, true), "Enable left/right stereo");
    Check(overlay.SetOverlayFlag(main, VROverlayFlags.SideBySide_Crossed, false), "Disable reversed eyes");
    // Full SBS uses square pixels; SteamVR accounts for the two-eye packing.
    Check(overlay.SetOverlayTexelAspect(main, 1f), "Set square pixels");
    Check(overlay.SetOverlayInputMethod(main, VROverlayInputMethod.None), "Set display-only input");
    Check(overlay.SetOverlayRaw(main, panel.GetPixels(), (uint)panel.Width, (uint)panel.Height, 4), "Upload stereo RGBA texture");
    using var icon = StereoPanel.RenderThumbnail();
    Check(overlay.SetOverlayRaw(thumbnail, icon.GetPixels(), (uint)icon.Width, (uint)icon.Height, 4), "Upload dashboard icon");

    bool stereo = false;
    Check(overlay.GetOverlayFlag(main, VROverlayFlags.SideBySide_Parallel, ref stereo), "Read stereo flag");
    var transform = VROverlayTransformType.VROverlayTransform_Invalid;
    Check(overlay.GetOverlayTransformType(main, ref transform), "Read transform type");
    if (!stereo || transform != VROverlayTransformType.VROverlayTransform_DashboardTab)
        throw new InvalidOperationException($"Unexpected configuration: stereo={stereo}, transform={transform}.");
    Console.WriteLine($"Dashboard tab created. Stereo={stereo}; transform={transform}; headset connected={system.IsTrackedDeviceConnected(0)}.");
    Console.WriteLine("Select Stereo Experiment in the SteamVR dashboard. Targets should appear near / flat / far.");
    Console.WriteLine("Only L should be visible through the left eye, and only R through the right eye. Ctrl+C or stop.ps1 exits.");
    if (show) overlay.ShowDashboard(overlayKey);

    var timer = Stopwatch.StartNew();
    var vrEvent = new VREvent_t();
    uint eventSize = (uint)Marshal.SizeOf<VREvent_t>();
    bool textureVerified = false;
    bool? lastActive = null;
    while (!cancel.IsCancellationRequested && timer.Elapsed.TotalSeconds < seconds && (stopFile == null || !File.Exists(stopFile)))
    {
        while (system.PollNextEvent(ref vrEvent, eventSize)) HandleEvent(vrEvent, system, cancel);
        foreach (ulong handle in new[] { main, thumbnail })
            while (overlay.PollNextOverlayEvent(handle, ref vrEvent, eventSize)) HandleEvent(vrEvent, system, cancel);
        if (!textureVerified)
        {
            uint width = 0, height = 0;
            var result = overlay.GetOverlayTextureSize(main, ref width, ref height);
            if (result == EVROverlayError.None && width == panel.Width && height == panel.Height)
            {
                textureVerified = true;
                Console.WriteLine($"SteamVR texture verified: {width}x{height}, {StereoPanel.EyeWidth}x{StereoPanel.Height} per eye.");
            }
            else if (timer.Elapsed.TotalSeconds > 2)
                throw new InvalidOperationException($"Texture did not become ready: {result}, {width}x{height}.");
        }
        bool active = overlay.IsActiveDashboardOverlay(main);
        if (lastActive != active) { Console.WriteLine($"Dashboard tab active: {active}."); lastActive = active; }
        cancel.Token.WaitHandle.WaitOne(100);
    }
    if (!textureVerified) throw new InvalidOperationException("Stopped before SteamVR confirmed the texture dimensions.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Stereo experiment: {ex.Message}");
    return 1;
}
finally
{
    if (initialized)
    {
        // Dashboard thumbnails belong to the main overlay and cannot be destroyed
        // independently (ThumbnailCantBeDestroyed). Removing the main removes both.
        if (main != OpenVR.k_ulOverlayHandleInvalid)
            Console.WriteLine($"Dashboard cleanup: {OpenVR.Overlay.DestroyOverlay(main)}.");
        OpenVR.Shutdown();
    }
    Console.CancelKeyPress -= onCancel;
}

static void Check(EVROverlayError error, string operation)
{
    if (error != EVROverlayError.None) throw new InvalidOperationException($"{operation}: {error}");
}

static void HandleEvent(VREvent_t vrEvent, CVRSystem system, CancellationTokenSource cancel)
{
    switch ((EVREventType)vrEvent.eventType)
    {
        case EVREventType.VREvent_ImageLoaded: Console.WriteLine("SteamVR confirmed an overlay image loaded."); break;
        case EVREventType.VREvent_ImageFailed: throw new InvalidOperationException("SteamVR failed to load an overlay image.");
        case EVREventType.VREvent_OverlayClosed: cancel.Cancel(); break;
        case EVREventType.VREvent_Quit: system.AcknowledgeQuit_Exiting(); cancel.Cancel(); break;
    }
}
