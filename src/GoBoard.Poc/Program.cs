using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using GoBoard.Poc;
using SkiaSharp;
using Valve.VR;

return Run(args);

static int Run(string[] args)
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
        if (args.Length == 1 && args[0] == "--self-test") { RelativePose.Verify(); GrabPose.Verify(); GrabInput.Verify(); OverlayPointers.Verify(); KeyboardChecks.Verify(); return 0; }
        if (args.Length == 1 && args[0] == "--input-check") { KeyboardInputCheck.Run(); return 0; }
        if (args.Length == 1 && args[0] == "--shell-check") { KeyboardInputCheck.Run(shell: true); return 0; }
        if (args.Length == 1 && args[0] == "--layout-check") { KeyboardInputCheck.Run(layouts: true); return 0; }
        string renderPath = null;
        string stopFile = null;
        double seconds = double.PositiveInfinity;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--render" when i + 1 < args.Length: renderPath = args[++i]; break;
                case "--stop-file" when i + 1 < args.Length: stopFile = args[++i]; break;
                case "--seconds" when i + 1 < args.Length:
                    seconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    if (!double.IsFinite(seconds) || seconds <= 0) throw new ArgumentException("Seconds must be positive and finite.");
                    break;
                default: throw new ArgumentException("Usage: GoBoard.Poc [--seconds N] [--stop-file PATH] [--render PATH] | --self-test | --input-check | --shell-check | --layout-check");
            }
        }

        using var panel = Panel.Render();
        if (renderPath != null)
        {
            using var image = SKImage.FromBitmap(panel);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(renderPath);
            png.SaveTo(file);
            Console.WriteLine($"Rendered {panel.Width}x{panel.Height} panel to {renderPath}");
            return 0;
        }

        if (!OpenVR.IsRuntimeInstalled()) throw new InvalidOperationException("SteamVR is not installed or its runtime path is not registered.");
        var error = EVRInitError.None;
        var system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
        if (error != EVRInitError.None) throw new InvalidOperationException($"OpenVR initialization failed: {error}. Start SteamVR and connect your headset.");
        initialized = true;
        graphics = new OverlayGraphics();
        var overlay = OpenVR.Overlay ?? throw new InvalidOperationException("SteamVR did not provide the OpenVR overlay interface.");
        Check(overlay.CreateOverlay("goboard.poc", "GoBoard", ref handle), "Create independent overlay (is another copy running?)");
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
        Check(overlay.CreateOverlay("goboard.poc.grab", "GoBoard grab", ref grabHandle), "Create grab handle");
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
        var follower = new DashboardFollower(overlay, handle, grabHandle, grab);
        using var keyboard = new KeyboardOverlay(system, overlay, handle, graphics);
        Console.WriteLine($"Independent overlay: {transformType}; 10 cm grab line with an 18 x 6 cm hover target. Controller-relative movement; no smoothing.");
        Console.WriteLine($"Panel: {Panel.WidthInMeters * 100:F1} x {Panel.HeightInMeters * 100:F1} cm, {panel.Width}x{panel.Height} texture; input coordinates remain {Panel.LayoutWidth}x{Panel.LayoutHeight}.");
        Console.WriteLine($"Headset connected: {system.IsTrackedDeviceConnected(OpenVR.k_unTrackedDeviceIndex_Hmd)}.");
        Console.WriteLine("Open the SteamVR menu: GoBoard appears separately below it and follows dashboard movement.");
        Console.WriteLine("Point at the line below GoBoard and hold the trigger to move it. Release to keep its new dashboard-relative position.");
        Console.WriteLine("No dashboard tab is created. Ctrl+C or the stop script closes GoBoard.");
        Console.WriteLine("Focus a text field in SteamVR Desktop. Ctrl/Alt/AltGr/Shift: arm, lock, clear. Win: first click arms a shortcut; second taps Windows and clears.");

        var timer = Stopwatch.StartNew();
        var vrEvent = new VREvent_t();
        var eventSize = (uint)Marshal.SizeOf<VREvent_t>();
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
            var visible = follower.Update();
            keyboard.BeginFrame(visible && !cancel.IsCancellationRequested,
                grab.ActiveGrab != null ? grab.Controller : null);
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


