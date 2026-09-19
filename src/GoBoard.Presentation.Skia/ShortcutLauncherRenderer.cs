using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal static class ShortcutLauncherRenderer
{
    public static SKBitmap Render(bool expanded, bool hovered, string theme = BoardThemes.Default)
    {
        const int size = ProgrammableKeys.ToggleSize;
        var bitmap = new SKBitmap(size * Panel.RasterScale, size * Panel.RasterScale, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent); canvas.Scale(Panel.RasterScale);
        var style = KeyboardTheme.Resolve(theme);
        Panel.DrawKeySurface(canvas, new("Shortcuts", "", 0, new(2, 2, size - 4, size - 4)), style, hovered, false, expanded);
        using var paint = new SKPaint { IsAntialias = true, Color = expanded ? style.Accent : style.Text };
        for (var row = 0; row < 2; row++)
        for (var col = 0; col < 2; col++) canvas.DrawRoundRect(new SKRect(11 + col * 12, 10 + row * 12, 20 + col * 12, 19 + row * 12), 1.5f, 1.5f, paint);
        if (expanded) canvas.DrawRoundRect(new SKRect(17, 36, 27, 38), 1, 1, paint);
        return bitmap;
    }
}
