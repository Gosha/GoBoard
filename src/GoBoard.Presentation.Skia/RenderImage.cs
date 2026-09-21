using SkiaSharp;

namespace GoBoard.Presentation.Skia;

// Cached images own their storage after the temporary drawing surface is disposed.
internal static class RenderImage
{
    internal static SKImage Create(SKImageInfo info, GRContext context, Action<SKCanvas> draw)
    {
        if (context != null)
        {
            using var surface = SKSurface.Create(context, true, info.WithAlphaType(SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Could not allocate a Skia GPU cache surface.");
            draw(surface.Canvas);
            return surface.Snapshot() ?? throw new InvalidOperationException("Could not snapshot a Skia GPU cache surface.");
        }
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap)) draw(canvas);
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }
}
