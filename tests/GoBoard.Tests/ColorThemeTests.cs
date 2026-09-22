using GoBoard.Core;
using GoBoard.Presentation.Skia;
using SkiaSharp;
using Xunit;

namespace GoBoard.Tests;

public sealed class ColorThemeTests
{
    [Theory]
    [InlineData(BoardThemes.SteamSoft, false)]
    [InlineData(BoardThemes.SteamSoft, true)]
    [InlineData(BoardThemes.SteamFlat, false)]
    [InlineData(BoardThemes.SteamFlat, true)]
    public void AlternatePaletteRecolorsBothStylesWithoutSharingCachedSurfaces(string styleId, bool selected)
    {
        var original = KeyboardStyle.Resolve(styleId);
        var colors = new UiColors(ColorThemes.SteamBlue with
        {
            Id = "test-palette", Name = "Test palette",
            AccentLight = SKColors.Magenta, AccentStrong = SKColors.Lime
        });
        var alternate = KeyboardStyle.Resolve(styleId, colors);
        // The same geometry/state is deliberately drawn with two palettes,
        // then drawn again with the original to catch cache contamination.
        using var before = Render(original, selected, cached: true);
        using var changed = Render(alternate, selected, cached: true);
        using var uncached = Render(alternate, selected, cached: false);
        using var after = Render(original, selected, cached: true);
        Assert.Contains(selected ? SKColors.Lime : SKColors.Magenta, changed.Pixels);
        Assert.DoesNotContain(selected ? SKColors.Lime : SKColors.Magenta, before.Pixels);
        Assert.Equal(before.Bytes, after.Bytes);
        Assert.Equal(original.ShadowBlur, alternate.ShadowBlur);
        Assert.Equal(original.KeyEdges, alternate.KeyEdges);
        var actual = changed.Bytes;
        var expected = uncached.Bytes;
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < actual.Length; i++)
            Assert.InRange(Math.Abs(actual[i] - expected[i]), 0, 2);
    }

    private static SKBitmap Render(KeyboardStyle style, bool selected, bool cached)
    {
        var bitmap = new SKBitmap(252, 192, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(Panel.RasterScale);
        var key = new KeyboardKey("Test", "", 0, new(12, 12, 60, 40));
        if (cached) KeySurfaceCache.Draw(canvas, key, style, hover: true, filled: false, selected: selected);
        else Panel.DrawKeySurface(canvas, key, style, hover: true, filled: false, selected: selected);
        canvas.Flush();
        return bitmap;
    }
}
