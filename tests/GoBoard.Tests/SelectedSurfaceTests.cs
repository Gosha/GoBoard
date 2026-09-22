using GoBoard.Core;
using GoBoard.Presentation.Skia;
using SkiaSharp;
using Xunit;

namespace GoBoard.Tests;

public sealed class SelectedSurfaceTests
{
    private sealed class Sink : IKeySink
    {
        public void Down(ushort scan) { }
        public void Up(ushort scan) { }
    }

    [Theory]
    [InlineData("NumLock", false)]
    [InlineData("NumLock", true)]
    [InlineData("Caps", false)]
    [InlineData("Caps", true)]
    [InlineData("ScrollLock", false)]
    [InlineData("ScrollLock", true)]
    public void EnabledLockKeysUseTheSoftArmedSurface(string id, bool hover)
    {
        var keyboard = new KeyboardState(new Sink());
        keyboard.SetNumpad(true, 0);
        keyboard.SetNumLock(true);
        var key = keyboard.Keys.Single(k => k.Id == id);
        if (hover)
        {
            keyboard.Enter(0, 7, 1);
            keyboard.Move(0, 7, key.Bounds.X + key.Bounds.Width / 2, keyboard.Height - key.Bounds.Y - key.Bounds.Height / 2);
        }
        using var actual = Panel.Render(keyboard, caps: true, scrollLock: true, theme: BoardThemes.SteamSoft);
        AssertArmedBorder(actual, key.Bounds, hover);
        Assert.True(Panel.IsArmedSelection(key, keyboard, caps: true, scrollLock: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothFloatingButtonsUseTheSoftArmedSurface(bool hover)
    {
        using var numpad = MainKeyboardControlRenderer.Render(KeyboardAction.ToggleNumpad,
            enabled: true, hovered: hover, pressed: false, BoardThemes.SteamSoft);
        using var shortcuts = ShortcutLauncherRenderer.Render(true, hover, BoardThemes.SteamSoft);
        AssertArmedBorder(numpad, new(2, 2, MainKeyboardControls.Size - 4, MainKeyboardControls.Size - 4), hover);
        AssertArmedBorder(shortcuts, new(2, 2, ProgrammableKeys.ToggleSize - 4, ProgrammableKeys.ToggleSize - 4), hover);
    }

    [Theory]
    [InlineData(BoardThemes.SteamSoft)]
    [InlineData(BoardThemes.SteamFlat)]
    public void NumpadButtonKeepsItsSizeAndBorderMarginAcrossStates(string theme)
    {
        using var idle = MainKeyboardControlRenderer.Render(KeyboardAction.ToggleNumpad, false, false, false, theme);
        var idleEdges = Edges(idle);
        foreach (var enabled in new[] { false, true })
        foreach (var hovered in new[] { false, true })
        foreach (var pressed in new[] { false, true })
        {
            using var button = MainKeyboardControlRenderer.Render(KeyboardAction.ToggleNumpad, enabled, hovered, pressed, theme);
            var edges = Edges(button);
            // Border widths vary by state, but the tile must not grow or clip.
            Assert.InRange(Math.Abs(edges.Left - idleEdges.Left), 0, 2);
            Assert.InRange(Math.Abs(edges.Right - idleEdges.Right), 0, 2);
            Assert.True(edges.Left >= 3 && edges.Right < button.Width - 3);
        }

        static (int Left, int Right) Edges(SKBitmap bitmap)
        {
            var row = Enumerable.Range(0, bitmap.Width)
                .Where(x => bitmap.GetPixel(x, bitmap.Height / 2).Alpha >= 200).ToArray();
            return (row.First(), row.Last());
        }
    }

    private static void AssertArmedBorder(SKBitmap actual, KeyBounds bounds, bool hover)
    {
        var style = KeyboardStyle.Soft;
        var local = new KeyBounds(8, 8, bounds.Width, bounds.Height);
        using var expected = new SKBitmap((int)(bounds.Width + 16) * Panel.RasterScale,
            (int)(bounds.Height + 16) * Panel.RasterScale, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(expected);
        canvas.Clear(style.Colors.PanelBackground);
        canvas.Scale(Panel.RasterScale);
        Panel.DrawKeySurface(canvas, new("Reference", "", 0, local), style, hover, false, true, armed: true);
        canvas.Flush();
        // Compare inside the straight border, away from labels and markers.
        for (var dx = 1; dx <= 4; dx++)
        {
            var a = actual.GetPixel((int)(bounds.X * Panel.RasterScale) + dx,
                (int)((bounds.Y + bounds.Height / 2) * Panel.RasterScale));
            var e = expected.GetPixel(8 * Panel.RasterScale + dx,
                (int)((8 + bounds.Height / 2) * Panel.RasterScale));
            Assert.InRange(Math.Abs(a.Red - e.Red), 0, 2);
            Assert.InRange(Math.Abs(a.Green - e.Green), 0, 2);
            Assert.InRange(Math.Abs(a.Blue - e.Blue), 0, 2);
        }
    }
}
