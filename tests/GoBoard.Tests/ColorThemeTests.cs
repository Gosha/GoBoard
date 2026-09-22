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

    [Theory]
    [InlineData(BoardThemes.SteamSoft, false)]
    [InlineData(BoardThemes.SteamSoft, true)]
    [InlineData(BoardThemes.SteamFlat, false)]
    [InlineData(BoardThemes.SteamFlat, true)]
    public void OneShotAndPersistentSelectionUseDistinctSoftSurfaces(string styleId, bool hovered)
    {
        var style = KeyboardStyle.Resolve(styleId);
        using var selected = Render(style, true, cached: true, hover: hovered);
        using var armed = Render(style, true, cached: true, armed: true, hover: hovered);
        using var direct = Render(style, true, cached: false, armed: true, hover: hovered);
        using var selectedAgain = Render(style, true, cached: true, hover: hovered);
        Assert.Equal(selected.Bytes, selectedAgain.Bytes);
        Assert.Equal(styleId == BoardThemes.SteamFlat, selected.Bytes.SequenceEqual(armed.Bytes));
        var expected = direct.Bytes;
        var actual = armed.Bytes;
        for (var i = 0; i < actual.Length; i++)
            Assert.InRange(Math.Abs(actual[i] - expected[i]), 0, 2);
    }

    private static SKBitmap Render(KeyboardStyle style, bool selected, bool cached, bool armed = false, bool hover = true)
    {
        var bitmap = new SKBitmap(252, 192, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(Panel.RasterScale);
        var key = new KeyboardKey("Test", "", 0, new(12, 12, 60, 40));
        if (cached) KeySurfaceCache.Draw(canvas, key, style, hover: hover, filled: false, selected: selected, armed: armed);
        else Panel.DrawKeySurface(canvas, key, style, hover: hover, filled: false, selected: selected, armed: armed);
        canvas.Flush();
        return bitmap;
    }
}
