using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal sealed record CharacterLabel(string Text, float Size, float X, float Y, SKTextAlign Align, SKColor Color, bool Dot = false);
internal sealed record CharacterLayer(CharacterLabel Label, SKImage Image, SKRect Bounds);
internal sealed record CharacterKey(KeyboardKey Key, CharacterLayer[] Layers);

// Mirrors Panel's printable-legend placement. Actual settled
// rendering and stationary glyph pixels still come from Panel.Render.
internal sealed class CharacterLayers : IDisposable
{
    public readonly Dictionary<string, CharacterKey> Keys = new();
    public static CharacterLayers Capture(WindowsLayout layout, bool shift, bool altGr, bool caps, KeyboardTheme style)
    {
        var result = new CharacterLayers();
        using var face = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
        using var letter = new SKFont(face, 21);
        altGr &= layout.HasAltGr;
        foreach (var key in layout.Keys.Where(k => k.Printable))
        {
            var b = key.Bounds;
            var normal = layout.Legend(key, false, false, false);
            var shifted = layout.Legend(key, true, false, false);
            var alternate = layout.Legend(key, false, true, false);
            var active = layout.Legend(key, shift, altGr, caps);
            var isLetter = normal.Text.Length == 1 && char.IsLetter(normal.Text[0]) && string.Equals(normal.Text, shifted.Text, StringComparison.OrdinalIgnoreCase);
            var labels = new CharacterLabel[4]; // primary, Shift, AltGr, dead-key dot
            if (active.Text.Length > 0)
                labels[0] = isLetter
                    ? new(active.Text, 21, b.Width / 2, b.Height / 2 - (letter.Metrics.Ascent + letter.Metrics.Descent) / 2, SKTextAlign.Center, style.Text)
                    : new(active.Text, 18, 9, b.Height - 6, SKTextAlign.Left, style.Text);
            var upper = shift && !altGr ? normal.Text : shifted.Text;
            if (!isLetter && upper.Length > 0 && upper != active.Text && upper != "—")
                labels[1] = new(upper, 12, 9, 16, SKTextAlign.Left, style.Secondary);
            if (layout.HasAltGr && alternate.Text.Length > 0 && alternate.Text != "—" && alternate.Text != active.Text)
                labels[2] = new(alternate.Text, 12, b.Width - 6, b.Height - 6, SKTextAlign.Right, style.Accent);
            if (active.Dead) labels[3] = new("", 0, b.Width - 5, 5, SKTextAlign.Left, style.Accent, true);
            result.Keys[key.Id] = new(key, labels.Select(label => Raster(label, b, face)).ToArray());
        }
        return result;
    }

    private static CharacterLayer Raster(CharacterLabel label, KeyBounds key, SKTypeface face)
    {
        if (label == null) return new(null, null, SKRect.Empty);
        const int scale = OverlayGeometry.RasterScale;
        using var bitmap = new SKBitmap((int)(key.Width * scale), (int)(key.Height * scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent); canvas.Scale(scale);
        using var paint = new SKPaint { IsAntialias = true, Color = label.Color };
        SKRect bounds;
        if (label.Dot)
        {
            canvas.DrawCircle(label.X, label.Y, 1.3f, paint);
            bounds = new(label.X - 1.3f, label.Y - 1.3f, label.X + 1.3f, label.Y + 1.3f);
        }
        else
        {
            using var font = new SKFont(face, label.Size);
            var advance = font.MeasureText(label.Text, out bounds, paint);
            var offset = label.Align == SKTextAlign.Center ? advance / 2 : label.Align == SKTextAlign.Right ? advance : 0;
            bounds.Offset(label.X - offset, label.Y);
            canvas.DrawText(label.Text, label.X, label.Y, label.Align, font, paint);
        }
        // Include AA fringes, rounded outward on the source raster grid.
        bounds = new(MathF.Floor(bounds.Left * scale - 1) / scale, MathF.Floor(bounds.Top * scale - 1) / scale,
            MathF.Ceiling(bounds.Right * scale + 1) / scale, MathF.Ceiling(bounds.Bottom * scale + 1) / scale);
        return new(label, SKImage.FromBitmap(bitmap), bounds);
    }
    public void Dispose() { foreach (var key in Keys.Values) foreach (var layer in key.Layers) layer.Image?.Dispose(); Keys.Clear(); }
}
