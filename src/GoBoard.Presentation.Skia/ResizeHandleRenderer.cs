using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal static class ResizeHandleRenderer
{
    public static SKBitmap Render(int state)
    {
        var size = OverlayGeometry.ResizeSize;
        var bitmap = new SKBitmap(size * OverlayGeometry.RasterScale, size * OverlayGeometry.RasterScale,
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(OverlayGeometry.RasterScale);
        // Grow only the transparent margin. Keep the 28 mm grip centered and the
        // same physical size/spacing as the original 60-unit texture.
        canvas.Translate((size - 60) / 2f, (size - 60) / 2f);
        using var paint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round,
            StrokeWidth = state == 0 ? 4 : 4.5f,
            Color = state switch
            {
                2 => InteractionColors.DashboardBlue,
                1 => SKColors.White,
                _ => new SKColor(112, 122, 134)
            }
        };
        // A single rounded bottom-right bracket, matching the dashboard grip.
        // The bracket wraps the overlay center; geometry adds the spacing from the panel corner.
        using var builder = new SKPathBuilder();
        builder.MoveTo(10, 38);
        builder.LineTo(26, 38);
        builder.CubicTo(32.6274f, 38, 38, 32.6274f, 38, 26);
        builder.LineTo(38, 10);
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
        canvas.Flush();
        return bitmap;
    }
}
