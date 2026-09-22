using GoBoard.Core;
using GoBoard.Presentation.Skia;
using SkiaSharp;
using Xunit;

namespace GoBoard.Tests;

public sealed class DesktopCaptionTests
{
    private sealed class Sink : IKeySink
    {
        public void Down(ushort scan) { }
        public void Up(ushort scan) { }
    }

    [Theory]
    [InlineData(BoardThemes.SteamSoft, 2)] // 150% desktop scale
    [InlineData(BoardThemes.SteamSoft, 3)] // 100%
    [InlineData(BoardThemes.SteamSoft, 6)] // 50%
    [InlineData(BoardThemes.SteamFlat, 2)]
    [InlineData(BoardThemes.SteamFlat, 3)]
    [InlineData(BoardThemes.SteamFlat, 6)]
    public void ReducingShortcutCaptionsPreservesTheirLetterStrokes(string theme, int divisor)
    {
        var keyboard = new KeyboardState(new Sink(), shortcutsOnly: true, shortcutFooter: false);
        using var source = Panel.Render(keyboard, theme: theme);
        var output = new SKImageInfo(source.Width / divisor, source.Height / divisor,
            SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var renderer = new AnimatedKeyboardRenderer();
        using var actual = renderer.Render(keyboard, false, null, false, false, false,
            theme, new EffectSettings(), 0, output);
        double squaredError = 0;
        var samples = 0;
        foreach (var key in keyboard.Keys.Where(key => key.Shortcut is { } shortcut &&
            shortcut.ChordFor(keyboard.Layout) != key.Label))
        {
            var b = key.Bounds;
            // Caption band only: exclude the title, slot number and tile border.
            for (var y = (int)((b.Y + b.Height / 2 + 5) * Panel.RasterScale / divisor);
                y < (b.Y + b.Height / 2 + 19) * Panel.RasterScale / divisor; y++)
            for (var x = (int)((b.X + 5) * Panel.RasterScale / divisor);
                x < (b.X + b.Width - 5) * Panel.RasterScale / divisor; x++)
            {
                // Independent box average: every source pixel must contribute,
                // so thin strokes cannot disappear between bilinear samples.
                double expected = 0;
                for (var dy = 0; dy < divisor; dy++)
                for (var dx = 0; dx < divisor; dx++)
                    expected += source.GetPixel(x * divisor + dx, y * divisor + dy).Red;
                expected /= divisor * divisor;
                if (expected < 55) continue; // Measure visible lettering, not empty background.
                squaredError += Math.Pow(actual.GetPixel(x, y).Red - expected, 2);
                samples++;
            }
        }
        Assert.True(samples > 100);
        // Allows filter differences while rejecting the lost strokes (old error: 47–68).
        Assert.InRange(Math.Sqrt(squaredError / samples), 0, 25);
    }
}
