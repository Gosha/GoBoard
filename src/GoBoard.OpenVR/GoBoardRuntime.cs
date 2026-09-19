using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using SkiaSharp;
using Valve.VR;

namespace GoBoard.Vr;

public static class GoBoardRuntime
{
public static int Run(string[] args)
{
    ulong handle = OpenVR.k_ulOverlayHandleInvalid;
    ulong grabHandle = OpenVR.k_ulOverlayHandleInvalid;
    bool initialized = false;
    OverlayGraphics graphics = null;
    using var cancel = new CancellationTokenSource();
    ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancel.Cancel(); };
    Console.CancelKeyPress += onCancel;
    try
    {
        if (args.Length == 1 && args[0] == "--self-test") { RelativePose.Verify(); GrabPose.Verify(); GrabInput.Verify(); OverlayPointers.Verify(); return 0; }
        if (args is ["--render-benchmark"]) { PanelPreview.Benchmark(); return 0; }
        if (args is ["--effects-benchmark"]) { EffectsBenchmark.Run(); return 0; }
        if (args.Length == 1 && args[0] == "--input-check") { KeyboardInputCheck.Run(); return 0; }
        if (args.Length == 1 && args[0] == "--shell-check") { KeyboardInputCheck.Run(shell: true); return 0; }
        if (args.Length == 1 && args[0] == "--layout-check") { KeyboardInputCheck.Run(layouts: true); return 0; }
        if (args.Length == 1 && args[0] == "--ime-check") { KeyboardInputCheck.Run(ime: true); return 0; }
        string renderPath = null;
        string settingsRenderPath = null;
        string previewLayout = "us", previewState = "idle";
        string previewTheme = BoardThemes.Default;
        bool previewOptions = false;
        string stopFile = null;
        double seconds = double.PositiveInfinity;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--render" when i + 1 < args.Length: renderPath = args[++i]; break;
                case "--render-settings" when i + 1 < args.Length: settingsRenderPath = args[++i]; break;
                case "--layout" when i + 1 < args.Length: previewLayout = args[++i]; previewOptions = true; break;
                case "--state" when i + 1 < args.Length: previewState = args[++i]; previewOptions = true; break;
                case "--theme" when i + 1 < args.Length:
                    previewTheme = args[++i]; previewOptions = true;
                    if (previewTheme is not (BoardThemes.SteamSoft or BoardThemes.SteamFlat))
                        throw new ArgumentException("Preview theme must be steam-soft or steam-flat.");
                    break;
                case "--stop-file" when i + 1 < args.Length: stopFile = args[++i]; break;
                case "--seconds" when i + 1 < args.Length:
                    seconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    if (!double.IsFinite(seconds) || seconds <= 0) throw new ArgumentException("Seconds must be positive and finite.");
                    break;
                default: throw new ArgumentException("Usage: GoBoard [--desktop] [--seconds N] [--stop-file PATH] | --settings | --render-desktop PATH | --desktop-input-check | --render-settings PATH | --render-desktop-settings PATH | --render PATH [--layout us|sv|uk|de|fr|us-intl|ja|KLID] [--state idle|hover|pressed|oneshot|locked|shift|caps|scrolllock|altgr|unsupported|error|reference] [--theme steam-soft|steam-flat] | --self-test | --input-check | --shell-check | --layout-check | --ime-check");
            }
        }

        if (settingsRenderPath != null && (renderPath != null || previewOptions)) throw new ArgumentException("--render-settings cannot be combined with keyboard preview options.");
        if (previewOptions && renderPath == null) throw new ArgumentException("--layout, --state and --theme require --render; live layouts follow Windows automatically; use Settings for live theme selection.");
        var previewId = previewLayout switch
        {
            "us" => "00000409", "sv" => "0000041d", "uk" => "00000809", "de" => "00000407",
            "fr" => "0000040c", "us-intl" => "00020409", _ => previewLayout
        };
        using var panel = settingsRenderPath != null ? SettingsPanel.Render(new BoardSettings()) :
            renderPath == null ? Panel.Render() : previewLayout == "ja" ? PanelPreview.Render("ja", previewState, previewTheme) :
            PanelPreview.Render(WindowsLayoutProvider.FromKlid(0, previewId), previewState, previewTheme);
        renderPath ??= settingsRenderPath;
        if (renderPath != null)
        {
            var fullRenderPath = Path.GetFullPath(renderPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullRenderPath)!);
            using var image = SKImage.FromBitmap(panel);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(fullRenderPath);
            png.SaveTo(file);
            Console.WriteLine($"Rendered {panel.Width}x{panel.Height} panel to {fullRenderPath}");
            return 0;
        }

        if (!OpenVR.IsRuntimeInstalled()) throw new InvalidOperationException("SteamVR is not installed or its runtime path is not registered.");
        var error = EVRInitError.None;
        var system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
        if (error != EVRInitError.None) throw new InvalidOperationException($"OpenVR initialization failed: {error}. Start SteamVR and connect your headset.");
        initialized = true;
        graphics = new OverlayGraphics();
        var overlay = OpenVR.Overlay ?? throw new InvalidOperationException("SteamVR did not provide the OpenVR overlay interface.");
        Check(overlay.CreateOverlay("goboard.app", "GoBoard", ref handle), "Create independent overlay (is another copy running?)");
        Check(overlay.SetOverlayWidthInMeters(handle, Panel.WidthInMeters), "Set width");
        Check(overlay.SetOverlayFlag(handle, VROverlayFlags.VisibleInDashboard, true), "Allow panel alongside dashboard");


        Check(overlay.SetOverlayFlag(handle, VROverlayFlags.NoBackside, false), "Enable SteamVR backside surface");
        var mouseScale = new HmdVector2_t { v0 = Panel.LayoutWidth, v1 = Panel.LayoutHeight };
        Check(overlay.SetOverlayMouseScale(handle, ref mouseScale), "Set exact pointer dimensions");
        var mask = new VROverlayIntersectionMaskPrimitive_t
        {
            m_nPrimitiveType = EVROverlayIntersectionMaskPrimitiveType.OverlayIntersectionPrimitiveType_Rectangle,
            m_Primitive = new VROverlayIntersectionMaskPrimitive_Data_t
            {
                m_Rectangle = new IntersectionMaskRectangle_t
                {
                    m_flTopLeftX = 0, m_flTopLeftY = 0,
                    m_flWidth = Panel.LayoutWidth, m_flHeight = Panel.LayoutHeight
                }
            }
        };
        Check(overlay.SetOverlayIntersectionMask(handle, ref mask, 1, (uint)Marshal.SizeOf<VROverlayIntersectionMaskPrimitive_t>()), "Restrict input to panel pixels");
        Check(overlay.SetOverlayInputMethod(handle, VROverlayInputMethod.Mouse), "Enable dashboard pointer input");
        Check(overlay.SetOverlayFlag(handle, VROverlayFlags.MultiCursor, true), "Enable both keyboard pointers");
        graphics.Upload(overlay, handle, panel);
        var transformType = VROverlayTransformType.VROverlayTransform_Absolute;
        Check(overlay.GetOverlayTransformType(handle, ref transformType), "Verify independent overlay");
        if (transformType == VROverlayTransformType.VROverlayTransform_DashboardTab)
            throw new InvalidOperationException("The keyboard panel must not be a dashboard tab.");
        Check(overlay.CreateOverlay("goboard.app.grab", "GoBoard grab", ref grabHandle), "Create grab handle");
        Check(overlay.SetOverlayWidthInMeters(grabHandle, GrabHandle.Width), "Set grab handle width");
        Check(overlay.SetOverlayFlag(grabHandle, VROverlayFlags.VisibleInDashboard, true), "Allow grab handle alongside dashboard");
        Check(overlay.SetOverlayFlag(grabHandle, VROverlayFlags.NoBackside, false), "Enable SteamVR grab handle backside");
        var grabScale = new HmdVector2_t { v0 = GrabHandle.LayoutWidth, v1 = GrabHandle.LayoutHeight };
        Check(overlay.SetOverlayMouseScale(grabHandle, ref grabScale), "Set grab pointer dimensions");
        mask.m_Primitive.m_Rectangle.m_flWidth = grabScale.v0;
        mask.m_Primitive.m_Rectangle.m_flHeight = grabScale.v1;
        Check(overlay.SetOverlayIntersectionMask(grabHandle, ref mask, 1, (uint)Marshal.SizeOf<VROverlayIntersectionMaskPrimitive_t>()), "Set padded grab target");
        Check(overlay.SetOverlayInputMethod(grabHandle, VROverlayInputMethod.Mouse), "Enable grab pointer input");
        Check(overlay.SetOverlayFlag(grabHandle, VROverlayFlags.MultiCursor, true), "Enable both grab pointers");
        var grab = new GrabHandle(system, overlay, grabHandle, graphics);
        grab.Update(false, default);
        var settings = new SettingsStore();
        using var resize = new ResizeHandle(system, overlay, graphics, settings);
        var follower = new DashboardFollower(overlay, handle, grabHandle, grab, resize);
        using var keyboard = new KeyboardOverlay(system, overlay, handle, graphics);
        var appliedSettings = settings.Current;
        follower.SetScale(appliedSettings.Scale);
        keyboard.ApplySettings(appliedSettings, false);
        using var settingsOverlay = new SettingsOverlay(system, overlay, graphics, settings, keyboard.PreviewSound);
        Console.WriteLine($"Independent overlay: {transformType}; 15 cm grab line with an 18 x 6 cm hover target. Controller-relative movement; no smoothing.");
        Console.WriteLine($"Panel: {Panel.WidthInMeters * 100:F1} x {Panel.HeightInMeters * 100:F1} cm, {panel.Width}x{panel.Height} texture; input coordinates remain {Panel.LayoutWidth}x{Panel.LayoutHeight}.");
        Console.WriteLine($"Headset connected: {system.IsTrackedDeviceConnected(OpenVR.k_unTrackedDeviceIndex_Hmd)}.");
        Console.WriteLine("Open the SteamVR menu: GoBoard appears separately below it and follows dashboard movement.");
        Console.WriteLine("Point at the line below GoBoard and hold the trigger to move it. Release to keep its new dashboard-relative position.");
        Console.WriteLine("Hold the bottom-right corner grip and drag to resize around the keyboard center (50–150%). Release to save.");
        Console.WriteLine("Open GoBoard Settings in the dashboard, or run GoBoard --settings on desktop. Ctrl+C or the stop script closes GoBoard.");
        Console.WriteLine($"Settings: {settings.FilePath}");
        Console.WriteLine("Focus a text field in SteamVR Desktop. Ctrl/Alt/AltGr/Shift: arm, lock, clear. Win: first click arms a shortcut; second taps Windows and clears.");

        var timer = Stopwatch.StartNew();
        var vrEvent = new VREvent_t();
        var eventSize = (uint)Marshal.SizeOf<VREvent_t>();
        double nextSettingsRead = 0;
        string settingsError = null;
        while (!cancel.IsCancellationRequested && timer.Elapsed.TotalSeconds < seconds && (stopFile == null || !File.Exists(stopFile)))
        {
            while (system.PollNextEvent(ref vrEvent, eventSize))
            {
                if ((EVREventType)vrEvent.eventType == EVREventType.VREvent_Quit)
                {
                    system.AcknowledgeQuit_Exiting();
                    cancel.Cancel();
                }
            }
            // Do not process a resize release (and save it) after shutdown begins.
            if (cancel.IsCancellationRequested) break;
            if (timer.Elapsed.TotalSeconds >= nextSettingsRead)
            {
                settings.Reload();
                nextSettingsRead = timer.Elapsed.TotalSeconds + .5;
                if (settings.Error != settingsError && settings.Error != null) Console.Error.WriteLine(settings.Error);
                settingsError = settings.Error;
            }
            settingsOverlay.Update();
            if (appliedSettings != settings.Current)
            {
                var resetPosition = appliedSettings.PositionResetId != settings.Current.PositionResetId;
                if (resetPosition) follower.ResetPosition();
                keyboard.ApplySettings(settings.Current, appliedSettings.SizePercent != settings.Current.SizePercent || resetPosition);
                appliedSettings = settings.Current;
            }
            var visible = follower.Update();
            keyboard.BeginFrame(visible && !cancel.IsCancellationRequested,
                grab.ActiveGrab != null ? grab.Controller : null, resize.Active);
            while (overlay.PollNextOverlayEvent(handle, ref vrEvent, eventSize))
            {
                var type = (EVREventType)vrEvent.eventType;
                if (type == EVREventType.VREvent_ImageFailed) throw new InvalidOperationException("SteamVR failed to load the overlay image.");
                if (type == EVREventType.VREvent_OverlayClosed) cancel.Cancel();
                if (type == EVREventType.VREvent_Quit)
                {
                    system.AcknowledgeQuit_Exiting();
                    cancel.Cancel();
                }
                if (!cancel.IsCancellationRequested) keyboard.Process(vrEvent);
            }
            if (cancel.IsCancellationRequested) break;
            keyboard.EndFrame();
            // Synchronize input/following with SteamVR instead of adding a fixed
            // sleep after each update. Held transforms are tracked by SteamVR.
            if (visible)
            {
                var sync = overlay.WaitFrameSync(20);
                if (sync != EVROverlayError.None) cancel.Token.WaitHandle.WaitOne(1);
            }
            else cancel.Token.WaitHandle.WaitOne(100);
        }
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"GoBoard: {ex.Message}");
        return 1;
    }
    finally
    {
        if (initialized)
        {
            if (grabHandle != OpenVR.k_ulOverlayHandleInvalid)
                Console.WriteLine($"Grab handle cleanup: {OpenVR.Overlay.DestroyOverlay(grabHandle)}.");
            if (handle != OpenVR.k_ulOverlayHandleInvalid)
            {
                var result = OpenVR.Overlay.DestroyOverlay(handle);
                Console.WriteLine($"Overlay cleanup: {result}.");
            }
            OpenVR.Shutdown();
        }
        graphics?.Dispose();
        Console.CancelKeyPress -= onCancel;
    }
}

static void Check(EVROverlayError error, string operation)
{
    if (error != EVROverlayError.None) throw new InvalidOperationException($"{operation}: {error}");
}
}
