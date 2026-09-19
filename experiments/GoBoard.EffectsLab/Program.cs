using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.EffectsLab;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Contains("--benchmark")) { TransitionChecks.Benchmark(); return 0; }
            if (args.Length == 2 && args[0] == "--pointer-benchmark") { PointerChecks.Benchmark(Path.GetFullPath(args[1])); return 0; }
            if (args.Length == 2 && args[0] == "--pointer-smoke") return PointerChecks.Desktop(Path.GetFullPath(args[1]));
            if (args.Contains("--self-test")) { SelfTest.Run(); TransitionChecks.Run(); PointerChecks.Run(); return 0; }
            if (args.Length == 2 && args[0] == "--transition-gallery") { TransitionChecks.Gallery(Path.GetFullPath(args[1])); return 0; }
            if (args.Length == 2 && args[0] == "--gallery") { SelfTest.Gallery(Path.GetFullPath(args[1])); return 0; }
            using var form = new LabForm();
            if (args.Length == 2 && args[0] == "--animation-smoke")
            {
                var folder = Path.GetFullPath(args[1]); Directory.CreateDirectory(folder);
                var frames = new List<(double Time, double Cost)>();
                var started = false;
                form.View.FrameDrawn += (time, cost, animating) => { if (started && animating) frames.Add((time, cost)); };
                using var timer = new System.Windows.Forms.Timer { Interval = 500 };
                timer.Tick += (_, _) =>
                {
                    if (!started)
                    {
                        started = true; timer.Interval = 1300;
                        form.View.Options.TransitionMs = 700;
                        form.View.TriggerKey("Shift");
                        return;
                    }
                    timer.Stop();
                    try
                    {
                        SelfTest.Require(frames.Count >= 10, "700 ms transition presents at least ten intermediate desktop frames");
                        SelfTest.Require(frames[^1].Time - frames[0].Time > .5, "Intermediate frames span the transition rather than skipping to its end");
                        SelfTest.Require(!form.View.Renderer.Transitions.Animating(form.View.Now), "Desktop transition settles after its configured duration");
                        var costs = frames.Select(f => f.Cost).Order().ToArray();
                        var report = $"PASS: {frames.Count} intermediate desktop frames over {(frames[^1].Time - frames[0].Time) * 1000:F0} ms; median paint {costs[costs.Length / 2]:F2} ms; max {costs.Max():F2} ms.";
                        Console.WriteLine(report); File.WriteAllText(Path.Combine(folder, "animation-smoke.txt"), report);
                    }
                    catch (Exception ex) { Console.Error.WriteLine(ex); File.WriteAllText(Path.Combine(folder, "animation-smoke-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
                    form.Close();
                };
                form.Shown += (_, _) => timer.Start(); Application.Run(form); return Environment.ExitCode;
            }
            if (args.Length == 2 && args[0] == "--smoke")
            {
                var folder = Path.GetFullPath(args[1]); Directory.CreateDirectory(folder);
                using var timer = new System.Windows.Forms.Timer { Interval = 700 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        var ctrl = SelfTest.Center(form.View.Session, "Ctrl");
                        form.View.ExerciseMouse(ctrl, true);
                        SelfTest.Require(form.View.Session.Keyboard.Mode(0x1d) == ModifierMode.OneShot, "Desktop mouse maps to Ctrl");
                        form.View.ExerciseMouse(SelfTest.Center(form.View.Session, "Alt"), true);
                        form.View.ExerciseMouse(SelfTest.Center(form.View.Session, "s"), true);
                        SelfTest.Require(form.View.Session.Strokes.Last().Chord.SequenceEqual(new ushort[] { 0x1d, 0x38 }), "Desktop Ctrl+Alt+S");
                        form.View.ExerciseMouse(ctrl, true); form.View.ExerciseMouse(ctrl, true);
                        form.View.ExerciseMouse(SelfTest.Center(form.View.Session, "Alt"), true);
                        form.View.ExerciseMouse(SelfTest.Center(form.View.Session, "e"), false);
                        form.View.TriggerKey("Shift");
                        form.Refresh();
                        SelfTest.Require(form.View.Session.Keyboard.Shift && form.View.Renderer.Transitions.Contains("a"), "Desktop Shift starts a character transition");
                        using var capture = new Bitmap(form.Width, form.Height); form.DrawToBitmap(capture, form.ClientRectangle with { Width = form.Width, Height = form.Height });
                        capture.Save(Path.Combine(folder, "desktop-effects-lab.png"));
                        File.WriteAllText(Path.Combine(folder, "smoke.txt"), "PASS: real WinForms mouse routing; Ctrl+Alt+S; locked Ctrl; one-shot Alt; Shift legend transition; paint and capture.");
                    }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "smoke-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
                    form.Close();
                };
                form.Shown += (_, _) => timer.Start();
                Application.Run(form); return Environment.ExitCode;
            }
            Application.Run(form); return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex); return 1;
        }
    }
}

internal static class SelfTest
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
    public static SKPoint Center(LocalSession session, string id)
    {
        var b = session.Keyboard.Layout.Keys.First(k => k.Id == id).Bounds;
        return new(b.X + b.Width - Math.Min(22, b.Width / 2), b.Y + b.Height / 2);
    }
    public static void Run()
    {
        var session = new LocalSession(); var effects = new Effects(); var options = new EffectOptions { LeaveMs = 200 };
        var e = Center(session, "e"); var r = Center(session, "r");
        effects.Move(e, session.Keyboard.Layout.Keys, options, 1);
        Require(effects.Trails["e"].Value(1) == 1, "Instant entry");
        effects.Move(r, session.Keyboard.Layout.Keys, options, 2);
        Require(effects.Trails["e"].Value(2.1) > 0 && effects.Trails["e"].Value(2.1) < 1 && effects.Trails["r"].Value(2.1) == 1, "Old key fades while new key is immediately highlighted");
        effects.Move(e, session.Keyboard.Layout.Keys, options, 2.11);
        Require(effects.Trails["e"].Value(2.11) == 1, "Reentry restores full hover");
        effects.Leave(options, 3);
        Require(effects.Trails["e"].Value(3.21) == 0 && effects.Presence.Value(3.21) == 0 && !effects.Animating(3.21), "Leave settles fully");
        options.EnterMs = 100; effects.Move(e, session.Keyboard.Layout.Keys, options, 4);
        Require(effects.Trails["e"].Value(4) == 0 && effects.Trails["e"].Value(4.05) > 0 && effects.Trails["e"].Value(4.11) == 1, "Optional gradual entry");
        options.LeaveMs = 0; effects.Leave(options, 4.2);
        Require(effects.Trails["e"].Value(4.2) == 0, "Zero-duration exit");
        var eBounds = session.Keyboard.Layout.Keys.First(k => k.Id == "e").Bounds;
        effects.Move(new(eBounds.X + eBounds.Width + 1, e.Y), session.Keyboard.Layout.Keys, options, 4.3);
        Require(effects.Hover == null, "Two-unit key gap never steals a neighboring key");
        var enter = session.Keyboard.Layout.Keys.First(k => k.Id == "Enter");
        var cutout = new SKPoint(enter.Bounds.X + 1, enter.Bounds.Y + enter.CutoutTop + 1);
        effects.Move(cutout, session.Keyboard.Layout.Keys, options, 5);
        Require(effects.Hover?.Id != "Enter", "ISO Enter cutout is not clickable as Enter");
        effects.Move(e, session.Keyboard.Layout.Keys, options, 6); effects.Down(6, options); effects.Up(); effects.Prune(7);
        Require(effects.Pulses.Count == 0 && effects.Pressed == null, "Released click pulse expires");
        var time = 10.0;
        void Click(string id) { session.Click(Center(session, id), time); time += .1; }
        Click("Ctrl"); Click("Alt"); Click("s");
        Require(session.Strokes.Last().Chord.SequenceEqual(new ushort[] { 0x1d, 0x38 }) && session.Keyboard.Mode(0x1d) == ModifierMode.Idle && session.Keyboard.Mode(0x38) == ModifierMode.Idle, "One-shot Ctrl+Alt+S consumes both modifiers");
        Click("Ctrl"); Click("Ctrl"); Click("s"); Click("s");
        Require(session.Keyboard.Mode(0x1d) == ModifierMode.Locked && session.Strokes.TakeLast(2).All(s => s.Chord.SequenceEqual(new ushort[] { 0x1d })), "Locked Ctrl survives two S clicks");
        Click("Ctrl"); Require(session.Keyboard.Mode(0x1d) == ModifierMode.Idle, "Third Ctrl click clears lock");
        session.SetLayout(false, time); effects.Reset();
        Require(!session.Keyboard.Layout.Iso && effects.Hover == null && effects.Trails.Count == 0, "Layout switch clears old geometry and effects");
        using var renderer = new LabRenderer(); using var bitmap = new SKBitmap(850, 282); using var canvas = new SKCanvas(bitmap);
        foreach (var swedish in new[] { true, false })
        {
            session.SetLayout(swedish, time += 1);
            foreach (var preset in EffectOptions.Presets)
            {
                options.Preset(preset); effects.Move(Center(session, "e"), session.Keyboard.Layout.Keys, options, time); effects.Down(time, options);
                renderer.Draw(canvas, new(0, 0, 850, 282), session, effects, options, time + .05);
            }
        }
        Require(bitmap.GetPixel(0, 0).Alpha == 255, "All presets render on both layouts with Skia");
        options = new(); effects.Reset();
        effects.Move(Center(session, "e"), session.Keyboard.Layout.Keys, options, 30);
        effects.Down(30, options); effects.Leave(options, 30.1); effects.Prune(31);
        renderer.Draw(canvas, new(0, 0, 850, 282), session, effects, options, 31);
        using var baseline = new SKBitmap(850, 282); using var baselineCanvas = new SKCanvas(baseline);
        renderer.Draw(baselineCanvas, new(0, 0, 850, 282), session, effects, options, 31, baselineOnly: true);
        Require(bitmap.Bytes.SequenceEqual(baseline.Bytes), "Settled effects return pixel-for-pixel to baseline");
    }
    public static void Gallery(string folder)
    {
        Directory.CreateDirectory(folder);
        using var renderer = new LabRenderer();
        var session = new LocalSession(); var effects = new Effects(); var options = new EffectOptions();
        session.Click(Center(session, "Ctrl"), 1); session.Click(Center(session, "Ctrl"), 2); session.Click(Center(session, "Alt"), 3);
        foreach (var preset in EffectOptions.Presets)
        {
            effects.Reset(); options.Preset(preset);
            effects.Move(Center(session, "w"), session.Keyboard.Layout.Keys, options, 4);
            effects.Move(Center(session, "e"), session.Keyboard.Layout.Keys, options, 4.08); effects.Down(4.08, options);
            renderer.Save(Path.Combine(folder, preset.Replace(' ', '-').ToLowerInvariant() + ".png"), session, effects, options, 4.13);
        }
    }
}
