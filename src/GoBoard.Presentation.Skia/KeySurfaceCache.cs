using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

// Cache backgrounds by shape, not key identity or position. Legends and state
// indicators are still rendered live. Immutable raster images can be shared by
// desktop/VR renderers without rerunning a blur on every interaction.
internal static class KeySurfaceCache
{
    private readonly record struct SurfaceKey(KeyboardTheme Theme, float Width, float Height,
        float CutoutWidth, float CutoutTop, bool Hover, bool Filled, bool Selected);
    private const int Padding = 6; // Covers the 0.9-unit blur and 1.3-unit offset.
    private const int Capacity = 256;
    private static readonly object Gate = new();
    private static readonly Dictionary<SurfaceKey, SKImage> Images = new();

    public static void Draw(SKCanvas canvas, KeyboardKey key, KeyboardTheme theme, bool hover, bool filled, bool selected)
    {
        var b = key.Bounds;
        // Selected outlines remain visible on filled locked modifiers. Hover
        // cannot replace their outline, so it does not need a separate cache entry.
        var id = new SurfaceKey(theme, b.Width, b.Height, key.CutoutWidth, key.CutoutTop,
            !filled && !selected && hover, filled, selected);
        lock (Gate)
        {
            if (!Images.TryGetValue(id, out var image))
            {
                // Bound native memory if future layouts add arbitrary shapes.
                // Drawing holds the same lock so eviction cannot dispose an image in use.
                if (Images.Count == Capacity)
                {
                    foreach (var old in Images.Values) old.Dispose();
                    Images.Clear();
                }
                using var bitmap = new SKBitmap((int)Math.Ceiling((b.Width + Padding * 2) * Panel.RasterScale),
                    (int)Math.Ceiling((b.Height + Padding * 2) * Panel.RasterScale), SKColorType.Rgba8888, SKAlphaType.Premul);
                using var target = new SKCanvas(bitmap);
                target.Clear(SKColors.Transparent);
                target.Scale(Panel.RasterScale);
                var local = key with { Bounds = b with { X = Padding, Y = Padding } };
                Panel.DrawKeySurface(target, local, theme, id.Hover, id.Filled, id.Selected);
                target.Flush();
                bitmap.SetImmutable();
                image = SKImage.FromBitmap(bitmap);
                Images.Add(id, image);
            }
            // Both canvases use the fixed texture raster scale. This is a 1:1
            // pixel copy; no resampling blur is introduced by the cache.
            canvas.DrawImage(image, new SKRect(b.X - Padding, b.Y - Padding,
                b.X - Padding + image.Width / (float)Panel.RasterScale,
                b.Y - Padding + image.Height / (float)Panel.RasterScale),
                new SKSamplingOptions(SKFilterMode.Nearest));
        }
    }
}
