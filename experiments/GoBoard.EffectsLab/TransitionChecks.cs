using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.EffectsLab;

internal static class TransitionChecks
{
    public static void Benchmark()
    {
        var session = new LocalSession(); var options = new EffectOptions { TransitionMs = 700 }; options.Preset("Baseline");
        using var renderer = new LabRenderer(); using var surface = new SKBitmap(1140, 810); using var canvas = new SKCanvas(surface);
        var effects = new Effects();
        void Paint(double now)
        {
            canvas.Clear(SKColors.Black);
            renderer.Draw(canvas, new(200, 50, 920, 289), session, effects, options, now);
            renderer.Draw(canvas, new(200, 354, 920, 593), session, effects, options, now, baselineOnly: true);
            var ids = new[] { "a", "e", "2" };
            for (var i = 0; i < 3; i++)
            {
                var key = session.Keyboard.Layout.Keys.First(k => k.Id == ids[i]); var b = key.Bounds;
                var x = 280 + i * 190; const float y = 672, scale = 104f / 44;
                canvas.Save(); canvas.ClipRect(new(x, y, x + 104, y + 104));
                renderer.Draw(canvas, new(x - b.X * scale, y - b.Y * scale, x + (850 - b.X) * scale, y + (282 - b.Y) * scale), session, effects, options, now);
                canvas.Restore();
            }
            canvas.Flush();
        }
        Paint(0); Click(session, "Shift", 1);
        var watch = System.Diagnostics.Stopwatch.StartNew(); Paint(1);
        Console.WriteLine($"State-change frame: {watch.Elapsed.TotalMilliseconds:F2} ms");
        var costs = new List<double>();
        for (var i = 1; i <= 12; i++) { watch.Restart(); Paint(1 + i * .04); costs.Add(watch.Elapsed.TotalMilliseconds); }
        costs.Sort();
        Console.WriteLine($"Animated frame (keyboard + baseline + 3 close-ups): median {costs[costs.Count / 2]:F2} ms; max {costs.Max():F2} ms");
    }
    private static SKBitmap Frame(LabRenderer renderer, LocalSession session, EffectOptions options, double now, bool baseline = false)
    {
        var bitmap = new SKBitmap(new SKImageInfo(2550, 846, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        renderer.Draw(canvas, new(0, 0, bitmap.Width, bitmap.Height), session, new Effects(), options, now, baselineOnly: baseline);
        return bitmap;
    }
    private static void Click(LocalSession session, string id, double time) => session.Click(SelfTest.Center(session, id), time);
    public static void Run()
    {
        foreach (var style in Enum.GetValues<TransitionStyle>().Where(s => s != TransitionStyle.None))
        {
            var session = new LocalSession(); var options = new EffectOptions { Transition = style, TransitionMs = 200 };
            options.Preset("Baseline");
            using var renderer = new LabRenderer();
            using var initial = Frame(renderer, session, options, 0);
            using var initialLabels = CharacterLayers.Capture(session);
            Click(session, "Shift", 1);
            using var start = Frame(renderer, session, options, 1);
            SelfTest.Require(renderer.Transitions.Contains("a") && renderer.Transitions.Contains("e") && !renderer.Transitions.Contains("Shift") && !renderer.Transitions.Contains("Enter"), $"{style}: characters animate; modifier buttons stay instant");
            SelfTest.Require(renderer.Transitions.Contains("2", 0) && renderer.Transitions.Contains("2", 1) && !renderer.Transitions.Contains("2", 2) && !renderer.Transitions.Contains("e", 2), $"{style}: Shift changes the two left labels but excludes stationary @ and €");
            var fixedLabels = true;
            foreach (var time in new[] { 1.0, 1.04, 1.08, 1.12, 1.16, 1.22, 1.3 })
            {
                using var sample = Frame(renderer, session, options, time);
                foreach (var id in new[] { "2", "e" })
                {
                    var key = initialLabels.Keys[id]; var area = key.Layers[2].Bounds; var b = key.Key.Bounds;
                    for (var y = (int)Math.Round((b.Y + area.Top) * 3); y < (b.Y + area.Bottom) * 3; y++)
                        for (var x = (int)Math.Round((b.X + area.Left) * 3); x < (b.X + area.Right) * 3; x++)
                            fixedLabels &= sample.GetPixel(x, y) == initial.GetPixel(x, y);
                }
            }
            SelfTest.Require(fixedLabels, $"{style}: unchanged @ and € remain pixel-identical throughout Shift animation");
            using var mid = Frame(renderer, session, options, 1.1);
            using var target = Frame(renderer, session, options, 1.1, true);
            SelfTest.Require(!mid.Bytes.SequenceEqual(initial.Bytes) && !mid.Bytes.SequenceEqual(target.Bytes), $"{style}: intermediate frame differs from both endpoints");
            using var end = Frame(renderer, session, options, 2);
            SelfTest.Require(end.Bytes.SequenceEqual(target.Bytes) && !renderer.Transitions.Animating(2), $"{style}: settles exactly to production rendering");
            Click(session, "e", 2.1);
            using var consumed = Frame(renderer, session, options, 2.1);
            SelfTest.Require(session.Keyboard.Mode(0x2a) == ModifierMode.Idle && renderer.Transitions.Animating(2.1), $"{style}: one-shot consumption animates without delaying input");
        }
        {
            var session = new LocalSession(); var options = new EffectOptions(); options.Preset("Baseline");
            using var renderer = new LabRenderer(); using var initial = Frame(renderer, session, options, 0);
            Click(session, "Shift", 1); using var start = Frame(renderer, session, options, 1);
            using var before = Frame(renderer, session, options, 1.08);
            Click(session, "AltGr", 1.08); using var retarget = Frame(renderer, session, options, 1.08);
            var b = session.Keyboard.Layout.Keys.First(k => k.Id == "e").Bounds;
            var continuous = true;
            for (var y = (int)(b.Y + 3) * 3; y < (b.Y + b.Height - 3) * 3; y++)
                for (var x = (int)(b.X + 3) * 3; x < (b.X + b.Width - 3) * 3; x++)
                    continuous &= before.GetPixel(x, y) == retarget.GetPixel(x, y);
            SelfTest.Require(continuous, "Rapid Shift→AltGr retarget preserves the current legend pixels");
            Click(session, "Ctrl", 1.2); Click(session, "Ctrl", 1.3); using var locked = Frame(renderer, session, options, 1.3);
            SelfTest.Require(session.Keyboard.Mode(0x1d) == ModifierMode.Locked && !renderer.Transitions.Contains("Ctrl") && !renderer.Transitions.Contains("RightCtrl"), "Both Ctrl buttons change instantly without character animation");
            session.SetLayout(false, 2); using var switched = Frame(renderer, session, options, 2);
            SelfTest.Require(!renderer.Transitions.Animating(2), "Layout switch discards old transition geometry");
            options.TransitionMs = 0; Click(session, "Shift", 3); using var instant = Frame(renderer, session, options, 3);
            SelfTest.Require(!renderer.Transitions.Animating(3), "Zero duration immediately applies state");
            options.TransitionMs = 200; options.Transition = TransitionStyle.None; Click(session, "Caps", 4); using var disabled = Frame(renderer, session, options, 4);
            SelfTest.Require(!renderer.Transitions.Animating(4), "None disables modifier transitions");
        }
    }

    public static void Gallery(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var style in Enum.GetValues<TransitionStyle>().Where(s => s != TransitionStyle.None))
        {
            using var sheet = new SKBitmap(1700, 1060);
            using var canvas = new SKCanvas(sheet); canvas.Clear(new SKColor(9, 17, 25));
            using var font = new SKFont(SKTypeface.Default, 18);
            using var paint = new SKPaint { IsAntialias = true, Color = new(0xd8, 0xe6, 0xf0) };
            var options = new EffectOptions { Transition = style, TransitionMs = 400, StaggerMs = 200 }; options.Preset("Baseline");
            var session = new LocalSession(); using var renderer = new LabRenderer();
            using var initial = Frame(renderer, session, options, 0);
            var labels = new[] { "Shift armed", "Shift locked", "AltGr armed" };
            for (var row = 0; row < 3; row++)
            {
                var time = row * 2 + 1;
                Click(session, row < 2 ? "Shift" : "AltGr", time);
                using var begin = Frame(renderer, session, options, time);
                for (var col = 0; col < 2; col++)
                {
                    var at = time + (col == 0 ? .12 : .65);
                    canvas.DrawText($"{style} · {labels[row]} · {(col == 0 ? "120 ms" : "settled")}", col * 850 + 14, row * 350 + 28, SKTextAlign.Left, font, paint);
                    renderer.Draw(canvas, new(col * 850 + 10, row * 350 + 46, col * 850 + 840, row * 350 + 46 + 830 * 282f / 850), session, new(), options, at);
                }
            }
            using var image = SKImage.FromBitmap(sheet); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(Path.Combine(folder, "transition-" + style.ToString().ToLowerInvariant() + ".png")); data.SaveTo(stream);
        }
    }
}
