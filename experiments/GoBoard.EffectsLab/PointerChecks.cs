using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.EffectsLab;

internal static class PointerChecks
{
    private sealed record Result(string Preset, double MedianMs, double P95Ms, double MaxMs, long BytesPerFrame, string PixelHash);
    private static readonly string[] Cases = ["Baseline", "Soft afterglow", "Pointer spotlight", "Proximity edges", "Ripple only", "Press flash only", "Click feedback", "Combined", "Combined stress"];

    private static EffectOptions Options(string name)
    {
        var options = new EffectOptions { Transition = TransitionStyle.None };
        options.Preset(name == "Combined stress" ? "Combined" : name);
        if (name == "Ripple only") options.Ripples = true;
        if (name == "Press flash only") options.PressFlash = true;
        if (name == "Combined stress") { options.Radius = 160; options.LeaveMs = 900; options.RippleMs = 650; options.Strength = 100; }
        return options;
    }

    public static void Run()
    {
        var session = new LocalSession();
        using var renderer = new LabRenderer(); using var bitmap = new SKBitmap(850, 282); using var canvas = new SKCanvas(bitmap);
        foreach (var name in Cases)
        {
            var options = Options(name); var effects = new Effects();
            renderer.Draw(canvas, new(0, 0, 850, 282), session, effects, options, 0);
            var initial = bitmap.Bytes;
            effects.Move(SelfTest.Center(session, "e"), session.Keyboard.Layout.Keys, options, 1);
            effects.Down(1, options); effects.Up();
            effects.Move(SelfTest.Center(session, "r"), session.Keyboard.Layout.Keys, options, 1.05);
            effects.Leave(options, 1.1); effects.Prune(3);
            renderer.Draw(canvas, new(0, 0, 850, 282), session, effects, options, 3);
            SelfTest.Require(!effects.Animating(3, options) && bitmap.Bytes.SequenceEqual(initial), name + ": exit returns exactly to baseline and stops animation");
        }
        var disabled = Options("Baseline"); var idle = new Effects();
        idle.Move(SelfTest.Center(session, "e"), session.Keyboard.Layout.Keys, disabled, 1); idle.Down(1, disabled); idle.Up(); idle.Leave(disabled, 1.1);
        SelfTest.Require(idle.Pulses.Count == 0 && !idle.Animating(1.1, disabled), "Disabled pointer effects allocate no pulse and schedule no exit animation");
        var flash = Options("Press flash only"); var shortPulse = new Effects();
        shortPulse.Move(SelfTest.Center(session, "e"), session.Keyboard.Layout.Keys, flash, 1); shortPulse.Down(1, flash); shortPulse.Up();
        SelfTest.Require(shortPulse.Animating(1.05, flash) && !shortPulse.Animating(1.2, flash), "Flash-only animation stops when the visible flash ends");
    }

    public static void Benchmark(string folder)
    {
        Directory.CreateDirectory(folder);
        var results = new List<Result>();
        foreach (var name in Cases)
        {
            var session = new LocalSession(); var options = Options(name); var effects = new Effects();
            using var renderer = new LabRenderer(); using var bitmap = new SKBitmap(1140, 810); using var canvas = new SKCanvas(bitmap);
            var view = new SKRect(200, 50, 920, 289); var baseline = new SKRect(200, 354, 920, 593);
            var samples = new List<double>(); long allocations = 0;
            var keyIds = new[] { "a", "e", "2" };
            void Paint(double now)
            {
                canvas.Clear(SKColors.Black);
                renderer.Draw(canvas, view, session, effects, options, now);
                renderer.Draw(canvas, baseline, session, effects, options, now, baselineOnly: true);
                for (var i = 0; i < keyIds.Length; i++)
                {
                    var b = session.Keyboard.Layout.Keys.First(k => k.Id == keyIds[i]).Bounds;
                    var x = 280 + i * 190; const float y = 672, scale = 104f / 44;
                    canvas.Save(); canvas.ClipRect(new(x, y, x + 104, y + 104));
                    renderer.Draw(canvas, new(x - b.X * scale, y - b.Y * scale, x + (850 - b.X) * scale, y + (282 - b.Y) * scale), session, effects, options, now);
                    canvas.Restore();
                }
                canvas.Flush();
            }
            Paint(0);
            // Identical deterministic path at 60 simulated samples/s, with key
            // crossings, gaps, held presses and overlapping click feedback.
            for (var i = 0; i < 180; i++)
            {
                var now = 1 + i / 60.0;
                var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                effects.Move(new(340 + 300 * MathF.Sin(i * .091f), 137 + 92 * MathF.Sin(i * .059f)), session.Keyboard.Layout.Keys, options, now);
                if (i % (name == "Combined stress" ? 2 : 12) == 0) effects.Down(now, options);
                if (i % 12 == 6) effects.Up();
                effects.Prune(now); Paint(now);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (i >= 60) { samples.Add(elapsed); allocations += GC.GetAllocatedBytesForCurrentThread() - allocated; }
            }
            samples.Sort();
            results.Add(new(name, samples[60], samples[113], samples[^1], allocations / 120, Convert.ToHexString(SHA256.HashData(bitmap.Bytes))));
            Console.WriteLine(FormattableString.Invariant($"{name}: median {samples[60]:F2} ms; p95 {samples[113]:F2} ms; max {samples[^1]:F2} ms; {allocations / 120:N0} managed bytes/frame"));
        }
        File.WriteAllText(Path.Combine(folder, "pointer-benchmark.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        var report = new StringBuilder("# Pointer effect benchmark\n\nCPU Skia, 1140 × 810 canvas; keyboard + baseline + three enlarged previews. Each case uses 60 warm-up frames and 120 measured frames on the same deterministic path. Timings include effect updates and drawing, exclude WinForms presentation. Allocation counts are managed allocations only.\n\n| Preset | Median ms | p95 ms | Max ms | Managed bytes/frame |\n|---|---:|---:|---:|---:|\n");
        foreach (var r in results) report.AppendLine(FormattableString.Invariant($"| {r.Preset} | {r.MedianMs:F2} | {r.P95Ms:F2} | {r.MaxMs:F2} | {r.BytesPerFrame} |"));
        report.Append("\nDefault pointer settings except Combined stress: radius 160, leave 900 ms, ripple 650 ms, strength 100%, clicks every two samples. Character transitions are disabled to isolate pointer cost. Pixel hashes allow before/after equivalence checks. Native allocations and GPU/SteamVR performance are not measured.\n");
        File.WriteAllText(Path.Combine(folder, "pointer-benchmark.md"), report.ToString());
    }

    public static int Desktop(string folder)
    {
        Directory.CreateDirectory(folder);
        using var form = new LabForm();
        using var timer = new System.Windows.Forms.Timer { Interval = 16 };
        var view = form.View;
        var cases = EffectOptions.Presets.Reverse().ToArray();
        var index = -1; var phase = 0; var began = 0.0;
        var frames = new List<(double Time, double Cost)>();
        var fadeFrames = new List<double>(); var idleFrames = 0;
        var lines = new List<string>(); var result = 0; var ticks = 0;
        view.FrameDrawn += (time, cost, _) =>
        {
            if (phase == 1) frames.Add((time, cost));
            if (phase == 2 && view.Effects.Animating(time, view.Options)) fadeFrames.Add(time);
            if (phase == 3) idleFrames++;
        };
        timer.Tick += (_, _) =>
        {
            try
            {
                var now = view.Now;
                if (phase == 0)
                {
                    if (now < .4) return;
                    if (++index == cases.Length)
                    {
                        timer.Stop(); File.WriteAllLines(Path.Combine(folder, "pointer-desktop.txt"), lines); form.Close(); return;
                    }
                    view.Options.Preset(cases[index]); view.Options.Transition = TransitionStyle.None;
                    view.Options.LeaveMs = view.Options.RippleMs = 700;
                    view.ResetMotion(); frames.Clear(); fadeFrames.Clear(); idleFrames = ticks = 0;
                    phase = 1; began = now;
                }
                if (phase == 1)
                {
                    var elapsed = now - began;
                    view.ExerciseMouse(new(340 + 280 * MathF.Sin((float)elapsed * 8), 137 + 80 * MathF.Sin((float)elapsed * 6)), ticks++ % 8 == 0);
                    if (elapsed < .8) return;
                    // Trigger a final click and leave via the real mouse route.
                    view.ExerciseMouse(SelfTest.Center(view.Session, "e"), true);
                    view.ExerciseMouse(new(-1000, -1000), false);
                    phase = 2; began = now;
                }
                else if (phase == 2 && now - began > .95)
                {
                    SelfTest.Require(!view.Effects.Animating(now, view.Options), cases[index] + ": effects settle after leave");
                    phase = 3; began = now;
                }
                else if (phase == 3 && now - began > .25)
                {
                    SelfTest.Require(frames.Count >= 10, cases[index] + ": desktop sweep presents intermediate frames");
                    if (cases[index] != "Baseline") SelfTest.Require(fadeFrames.Count >= 10 && fadeFrames[^1] - fadeFrames[0] > .45, cases[index] + ": exit/click animation continues across its duration");
                    else SelfTest.Require(fadeFrames.Count == 0, "Baseline: disabled effects schedule no fade frames");
                    SelfTest.Require(idleFrames <= 1, cases[index] + ": settled pointer stops repainting");
                    var costs = frames.Select(f => f.Cost).Order().ToArray();
                    var line = FormattableString.Invariant($"{cases[index]}: sweep {frames.Count} frames / 800 ms; median paint {costs[costs.Length / 2]:F2} ms; p95 {costs[(int)((costs.Length - 1) * .95)]:F2} ms; fade/click {fadeFrames.Count} frames; idle {idleFrames} frames.");
                    lines.Add(line); Console.WriteLine(line); phase = 0;
                }
            }
            catch (Exception ex)
            {
                timer.Stop(); result = 1; Console.Error.WriteLine(ex);
                File.WriteAllText(Path.Combine(folder, "pointer-desktop-error.txt"), ex.ToString()); form.Close();
            }
        };
        form.Shown += (_, _) => timer.Start(); Application.Run(form); return result;
    }
}
