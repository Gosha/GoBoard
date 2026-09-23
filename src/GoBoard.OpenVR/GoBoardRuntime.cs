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
    RuntimeSession session = null;
    using var cancel = new CancellationTokenSource();
    ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancel.Cancel(); };
    Console.CancelKeyPress += onCancel;
    try
    {
        if (args.Length == 1 && args[0] == "--self-test") { RelativePose.Verify(); GrabPose.Verify(); GrabInput.Verify(); OverlayPointers.Verify(); return 0; }
        if (args is ["--render-benchmark"]) { PanelPreview.Benchmark(); return 0; }
        if (args is ["--effects-benchmark"]) { EffectsBenchmark.Run(); return 0; }
        if (args is ["--gpu-render-check"]) return GpuRenderCheck.Run();
        if (args is ["--gpu-overlay-check"]) return GpuOverlayCheck.Run();
        if (args is ["--shortcut-resize-check"]) return ShortcutResizeCheck.Run();
        if (args is ["--numpad-resize-check"]) return ShortcutResizeCheck.Run(numpad: true);
        if (args is ["--inactivity-overlay-check"]) return ShortcutResizeCheck.Run(numpad: true, verifyInactivity: true);
        if (args.Length == 1 && args[0] == "--input-check") { KeyboardInputCheck.Run(); return 0; }
        if (args.Length == 1 && args[0] == "--shell-check") { KeyboardInputCheck.Run(shell: true); return 0; }
        if (args.Length == 1 && args[0] == "--layout-check") { KeyboardInputCheck.Run(layouts: true); return 0; }
        if (args.Length == 1 && args[0] == "--ime-check") { KeyboardInputCheck.Run(ime: true); return 0; }
        string renderPath = null;
        string settingsRenderPath = null;
        string previewLayout = "us", previewState = "idle";
        string previewTheme = BoardThemes.Default;
        bool previewOptions = false;
        bool previewShortcuts = false;
        bool previewNumpad = false;
        bool previewControls = false;
        bool shortcutsSettings = false;
        bool shortcutPresets = false;
        SettingsPage? previewPage = null;
        string stopFile = null;
        double seconds = double.PositiveInfinity;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--render" when i + 1 < args.Length: renderPath = args[++i]; break;
                case "--render-settings" when i + 1 < args.Length: settingsRenderPath = args[++i]; break;
                case "--settings-page" when i + 1 < args.Length:
                    previewPage = Enum.Parse<SettingsPage>(args[++i], ignoreCase: true); break;
                case "--shortcuts": previewShortcuts = true; break;
                case "--numpad": previewNumpad = true; break;
                case "--controls": previewControls = true; break;
                case "--render-shortcut-settings" when i + 1 < args.Length: settingsRenderPath = args[++i]; shortcutsSettings = true; break;
                case "--render-shortcut-presets" when i + 1 < args.Length: settingsRenderPath = args[++i]; shortcutsSettings = shortcutPresets = true; break;
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
                default: throw new ArgumentException("Usage: GoBoard [--seconds N] [--stop-file PATH] | --settings | --render-settings PATH | --render-desktop-settings PATH | --render PATH [--layout us|sv|uk|de|fr|us-intl|ja|KLID] [--state idle|hover|pressed|oneshot|locked|shift|caps|scrolllock|altgr|unsupported|error|reference] [--theme steam-soft|steam-flat] | --self-test | --input-check | --shell-check | --layout-check | --ime-check");
            }
        }

        if (settingsRenderPath != null && (renderPath != null || previewOptions)) throw new ArgumentException("--render-settings cannot be combined with keyboard preview options.");
        if (previewPage.HasValue && settingsRenderPath == null) throw new ArgumentException("--settings-page requires --render-settings.");
        if (previewOptions && renderPath == null) throw new ArgumentException("--layout, --state and --theme require --render; live layouts follow Windows automatically; use Settings for live theme selection.");
        if (previewShortcuts && renderPath == null) throw new ArgumentException("--shortcuts requires --render.");
        if (previewControls && (renderPath == null || previewShortcuts)) throw new ArgumentException("--controls requires a main keyboard --render preview.");
        if (previewNumpad && renderPath == null) throw new ArgumentException("--numpad requires --render.");
        var previewId = previewLayout switch
        {
            "us" => "00000409", "sv" => "0000041d", "uk" => "00000809", "de" => "00000407",
            "fr" => "0000040c", "us-intl" => "00020409", _ => previewLayout
        };
        var settingsPointers = new SettingsPointerState();
        if (shortcutsSettings)
        {
            var b = SettingsControls.Tabs.Single(c => c.Action == SettingsAction.ShortcutsTab).Bounds;
            settingsPointers.Process(0, b.X + 10, b.Y + 10, 1, 1, down: true);
            settingsPointers.Process(0, b.X + 10, b.Y + 10, 1.1, 1.1, up: true);
        }
        if (shortcutPresets)
        {
            var b = SettingsControls.Shortcuts.Single(c => c.Action == SettingsAction.ChooseShortcutPreset).Bounds;
            settingsPointers.Process(0, b.X + 10, b.Y + 10, 1.2, 1.2, down: true);
            settingsPointers.Process(0, b.X + 10, b.Y + 10, 1.3, 1.3, up: true);
            settingsPointers.Reset();
        }
        if (previewPage.HasValue) settingsPointers.SelectPage(previewPage.Value);
        using var panel = settingsRenderPath != null ? SettingsPanel.Render(new BoardSettings(), settingsPointers) :
            renderPath == null ? PanelPreview.Render("us", "idle", numpad: true) : previewLayout == "ja" ? PanelPreview.Render("ja", previewState, previewTheme, shortcuts: previewShortcuts, numpad: previewNumpad) :
            PanelPreview.Render(WindowsLayoutProvider.FromKlid(0, previewId), previewState, previewTheme, shortcuts: previewShortcuts, numpad: previewNumpad);
        renderPath ??= settingsRenderPath;
        if (renderPath != null)
        {
            var fullRenderPath = Path.GetFullPath(renderPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullRenderPath)!);
            using var controlsPreview = previewControls ? MainKeyboardControlRenderer.Compose(panel, previewNumpad, previewTheme) : null;
            using var image = SKImage.FromBitmap(controlsPreview ?? panel);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(fullRenderPath);
            png.SaveTo(file);
            Console.WriteLine($"Rendered {image.Width}x{image.Height} panel to {fullRenderPath}");
            return 0;
        }

        session = RuntimeSession.TryStart();
        if (session == null) { Console.WriteLine("GoBoard is already running in this Windows session."); return 0; }
        session.StartLogging();

        if (!OpenVR.IsRuntimeInstalled()) throw new InvalidOperationException("SteamVR is not installed or its runtime path is not registered.");
        var error = EVRInitError.None;
        var system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
        if (error != EVRInitError.None) throw new InvalidOperationException($"OpenVR initialization failed: {error}. Start SteamVR and connect your headset.");
        initialized = true;
        SteamVrApplication.IdentifyCurrentProcess();
        graphics = new OverlayGraphics();
        var overlay = OpenVR.Overlay ?? throw new InvalidOperationException("SteamVR did not provide the OpenVR overlay interface.");
        Check(overlay.CreateOverlay("goboard.app", "GoBoard", ref handle), "Create independent overlay (is another copy running?)");
        Check(overlay.SetOverlayWidthInMeters(handle, OverlayGeometry.WidthInMeters(true)), "Set width");
        Check(overlay.SetOverlayFlag(handle, VROverlayFlags.VisibleInDashboard, true), "Allow panel alongside dashboard");


        Check(overlay.SetOverlayFlag(handle, VROverlayFlags.NoBackside, false), "Enable SteamVR backside surface");
        var mouseScale = new HmdVector2_t { v0 = KeyboardOverlay.MainTextureInfo.Width, v1 = KeyboardOverlay.MainTextureInfo.Height };
        Check(overlay.SetOverlayMouseScale(handle, ref mouseScale), "Set exact pointer dimensions");
        var mask = new VROverlayIntersectionMaskPrimitive_t
        {
            m_nPrimitiveType = EVROverlayIntersectionMaskPrimitiveType.OverlayIntersectionPrimitiveType_Rectangle,
            m_Primitive = new VROverlayIntersectionMaskPrimitive_Data_t
            {
                m_Rectangle = new IntersectionMaskRectangle_t
                {
                    m_flTopLeftX = 0, m_flTopLeftY = 0,
                    m_flWidth = mouseScale.v0, m_flHeight = mouseScale.v1
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
        var nativeKeyboard = new NativeKeyboardVisibility(overlay);
        using var output = new WindowsKeyboard();
        using var keyboard = new KeyboardOverlay(system, overlay, handle, graphics, sharedOutput: output);
        var inactivity = new KeyboardInactivity();
        using var shortcuts = new ShortcutOverlays(system, overlay, graphics, handle, output);
        using var controls = new KeyboardControlOverlays(system, overlay, graphics, handle, action =>
        {
            if (!settings.Update(s => MainKeyboardControls.Apply(action, s)))
                keyboard.ReportError(settings.Error);
            else keyboard.CancelPending();
        });
        var appliedSettings = settings.Current;
        follower.SetNumpad(appliedSettings.NumpadEnabled);
        follower.SetScale(appliedSettings.Scale);
        keyboard.ApplySettings(appliedSettings, false);
        shortcuts.ApplySettings(appliedSettings);
        using var settingsOverlay = new SettingsOverlay(system, overlay, graphics, settings, keyboard.PreviewSound);
        Console.WriteLine($"Independent overlay: {transformType}; 15 cm grab line with an 18 x 6 cm hover target. Controller-relative movement; no smoothing.");
        Console.WriteLine($"Panel: {OverlayGeometry.WidthInMeters(appliedSettings.NumpadEnabled) * appliedSettings.Scale * 100:F1} x {Panel.HeightInMeters * appliedSettings.Scale * 100:F1} cm, {panel.Width}x{panel.Height} fixed texture.");
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
        while (!cancel.IsCancellationRequested && !session.StopRequested && timer.Elapsed.TotalSeconds < seconds && (stopFile == null || !File.Exists(stopFile)))
        {
            var loopStarted = graphics.Timings.Start();
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
            // Exit before any further input or resize commits; normal disposal releases owned keys.
            if (settingsOverlay.CloseRequested) break;
            if (appliedSettings != settings.Current)
            {
                inactivity.Wake();
                var resetPosition = appliedSettings.PositionResetId != settings.Current.PositionResetId;
                if (resetPosition) follower.ResetPosition();
                follower.SetNumpad(settings.Current.NumpadEnabled);
                keyboard.ApplySettings(settings.Current, appliedSettings.SizePercent != settings.Current.SizePercent || resetPosition);
                shortcuts.ApplySettings(settings.Current);
                appliedSettings = settings.Current;
            }
            // Use the ordinary hidden path for input, grabs, resize and shortcuts.
            // Settings remains independently accessible in the dashboard.
            keyboard.SuppressInput(inactivity.Dormant, inactivity.CanRevealFromKeyboard);
            var visible = follower.Update(shortcuts.GrabOwner.HasValue,
                nativeKeyboardVisible: nativeKeyboard.IsVisible(), inactivity: inactivity);
            var interactive = visible && !inactivity.Dormant && !cancel.IsCancellationRequested;
            shortcuts.Update(interactive, resize.Scale, grab.ActiveGrab != null ? grab.Controller : null, resize.Active);
            keyboard.BeginFrame(interactive,
                grab.ActiveGrab != null ? grab.Controller : shortcuts.GrabOwner, resize.Active);
            controls.Update(appliedSettings, interactive,
                grab.ActiveGrab == null && !shortcuts.GrabOwner.HasValue && !resize.Active, resize.Scale);
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
            inactivity.Update(appliedSettings.Inactivity, timer.Elapsed.TotalSeconds, visible,
                keyboard.Engaged || controls.Engaged || shortcuts.Engaged || resize.Hovered,
                grab.Hovered || keyboard.RevealHovered, grab.ActiveGrab != null || resize.Active || shortcuts.GrabOwner.HasValue);
            graphics.Timings.End("loop.work", loopStarted);
            // Synchronize input/following with SteamVR instead of adding a fixed
            // sleep after each update. Held transforms are tracked by SteamVR.
            if (visible)
            {
                var syncStarted = graphics.Timings.Start();
                var sync = overlay.WaitFrameSync(20);
                if (sync != EVROverlayError.None) cancel.Token.WaitHandle.WaitOne(1);
                graphics.Timings.End("loop.frame-sync", syncStarted);
            }
            else cancel.Token.WaitHandle.WaitOne(100);
            graphics.Timings.End("loop.total", loopStarted);
            graphics.Timings.Report();
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
        session?.Dispose();
    }
}

static void Check(EVROverlayError error, string operation)
{
    if (error != EVROverlayError.None) throw new InvalidOperationException($"{operation}: {error}");
}
}
