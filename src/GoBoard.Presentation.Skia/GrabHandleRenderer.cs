using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal static class GrabHandleRenderer
{
    public const int PixelWidth = OverlayGeometry.GrabWidth * OverlayGeometry.RasterScale;
    public const int PixelHeight = OverlayGeometry.GrabHeight * OverlayGeometry.RasterScale;

    public static SKBitmap Render(int state, int layoutWidth = OverlayGeometry.GrabWidth)
    {
        var bitmap = new SKBitmap(new SKImageInfo(layoutWidth * OverlayGeometry.RasterScale, PixelHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(OverlayGeometry.RasterScale);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        paint.Color = state switch
        {
            2 => new SKColor(0x66, 0xc0, 0xf4),
            1 => new SKColor(241, 246, 252),
            _ => new SKColor(112, 122, 134)
        };
        var lineWidth = state == 0 ? layoutWidth * 5f / 6 : layoutWidth;
        var lineHeight = state == 0 ? 4 : 6;
        // Halve the visible idle gap (35.5 mm -> 17.75 mm) while keeping the
        // generous transparent target below the keyboard, clear of its keys.
        const float lineCenterY = 12.25f;
        canvas.DrawRoundRect(new SKRect(
            (layoutWidth - lineWidth) / 2f,
            lineCenterY - lineHeight / 2f,
            (layoutWidth + lineWidth) / 2f,
            lineCenterY + lineHeight / 2f), lineHeight / 2f, lineHeight / 2f, paint);
        canvas.Flush();
        return bitmap;
    }
}
