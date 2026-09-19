using System.Diagnostics;
using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal static class EffectsBenchmark
{
    private sealed class Sink : IKeySink { public void Down(ushort scan) { } public void Up(ushort scan) { } }
    public static void Run(SKImageInfo? output = null, Action<SKBitmap> present = null)
    {
        Console.WriteLine($"CPU rendering, {output?.Width ?? 2550} x {output?.Height ?? 846}; 30 warmup + 120 measured frames. " +
            (present == null ? "Excludes presentation/upload." : "Includes desktop bitmap wrapping and GDI paint; excludes compositor."));
        Console.WriteLine("Theme,Scenario,MedianMs,P95Ms,Frames");
        foreach (var theme in new[] { BoardThemes.SteamSoft, BoardThemes.SteamFlat })
        foreach (var (name, options) in new (string, EffectSettings)[] {
            ("Off", new()), ("Afterglow", new() { Afterglow = true }),
            ("Spotlight", new() { Spotlight = true }), ("Edges", new() { Edges = true }),
            ("Ripple", new() { Ripples = true }), ("Flash", new() { PressFlash = true }),
            ("Crossfade", new() { Transition = CharacterTransition.Crossfade }),
            ("Lift", new() { Transition = CharacterTransition.Lift }),
            ("Combined", new() { Afterglow = true, Spotlight = true, Edges = true, Ripples = true, PressFlash = true, Transition = CharacterTransition.Lift }) })
        {
            var keyboard = new KeyboardState(new Sink());
            using var renderer = new AnimatedKeyboardRenderer();
            var costs = new List<double>(); var watch = new Stopwatch();
            for (var i = 0; i < 150; i++)
            {
                var now = 1 + i / 90.0;
                keyboard.Enter(0, 7, now);
                var x = 80 + i % 50 * 11f; var y = 120f;
                keyboard.Move(0, 7, x, OverlayGeometry.PanelHeight - y);
                if (i % 12 == 0) { keyboard.Press(0, 7, x, OverlayGeometry.PanelHeight - y, now, now); keyboard.Up(0, 7, now); }
                watch.Restart();
                using var frame = renderer.Render(keyboard, i / 24 % 2 == 1, null, false, false, false, theme, options, now, output);
                if (frame != null) present?.Invoke(frame);
                watch.Stop();
                if (i >= 30 && frame != null) costs.Add(watch.Elapsed.TotalMilliseconds);
            }
            costs.Sort();
            Console.WriteLine(FormattableString.Invariant($"{theme},{name},{costs[costs.Count / 2]:F3},{costs[(int)(costs.Count * .95)]:F3},{costs.Count}"));
        }
    }
}
