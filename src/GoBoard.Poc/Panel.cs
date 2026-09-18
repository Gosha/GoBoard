using SkiaSharp;

namespace GoBoard.Poc;

internal static class Panel
{
    // Drawing and hit testing share logical units, independent of pixels/metres.
    public const int LayoutWidth = 844, LayoutHeight = 342;
    public const int RasterScale = 3;
    public const float WidthInMeters = 0.5445f * LayoutWidth / 512;
    public const float HeightInMeters = WidthInMeters * LayoutHeight / LayoutWidth;

    public static SKBitmap Render(KeyboardState keyboard = null, bool shift = false, string status = "Focus a text field on your desktop", bool altGr = false, bool caps = false)
    {
        var layout = keyboard?.Layout ?? new WindowsLayout(WindowsKeyboard.Foreground().Layout);
        var bitmap = new SKBitmap(new SKImageInfo(LayoutWidth * RasterScale, LayoutHeight * RasterScale, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(RasterScale);
        using var paint = new SKPaint { IsAntialias = true };
        using var face = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
        using var bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using var title = new SKFont(bold, 18);
        using var letter = new SKFont(face, 22);
        using var special = new SKFont(face, 14);
        using var small = new SKFont(face, 11);
        using var deadLabel = new SKFont(face, 7);
        var mint = new SKColor(89, 224, 184);
        canvas.Clear(new SKColor(18, 23, 33));
        paint.Color = mint;
        canvas.DrawRoundRect(new SKRect(16, 15, 20, 30), 2, 2, paint);
        paint.Color = new SKColor(241, 246, 252);
        canvas.DrawText("GoBoard", 28, 29, SKTextAlign.Left, title, paint);
        paint.Color = new SKColor(33, 46, 56);
        canvas.DrawRoundRect(new SKRect(LayoutWidth - 165, 10, LayoutWidth - 20, 34), 6, 6, paint);
        paint.Color = new SKColor(175, 195, 212);
        canvas.DrawText(layout.Name, LayoutWidth - 92.5f, 26, SKTextAlign.Center, small, paint);
        paint.Color = new SKColor(37, 45, 59);
        canvas.DrawLine(16, 40, LayoutWidth - 20, 40, paint);

        foreach (var key in layout.Keys)
        {
            var b = key.Bounds;
            var rect = new SKRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height);
            var pressed = keyboard?.Pressed(key) == true;
            var hover = keyboard?.Hovered(key) == true;
            var mode = key.IsModifier ? keyboard?.Mode(key.Scan) ?? ModifierMode.Idle : ModifierMode.Idle;
            var filled = mode == ModifierMode.Locked || (!key.IsModifier && pressed);
            paint.Style = SKPaintStyle.Fill;
            paint.Color = filled ? mint : mode == ModifierMode.OneShot ? new SKColor(30, 53, 48) : new SKColor(34, 43, 57);
            canvas.DrawRoundRect(rect, 6, 6, paint);
            if ((hover || mode == ModifierMode.OneShot) && !filled)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1;
                paint.Color = mint;
                canvas.DrawRoundRect(rect, 6, 6, paint);
                paint.Style = SKPaintStyle.Fill;
            }
            paint.Color = filled ? new SKColor(15, 39, 34) : mode == ModifierMode.OneShot ? mint : new SKColor(232, 240, 248);
            var font = key.Printable ? letter : special;
            var legend = layout.Legend(key, shift, altGr && layout.Swedish, caps);
            var label = legend.Text;
            var metrics = font.Metrics;
            canvas.DrawText(label, b.X + b.Width / 2, b.Y + b.Height / 2 - (metrics.Ascent + metrics.Descent) / 2,
                SKTextAlign.Center, font, paint);
            if (legend.Dead)
            {
                paint.Color = new SKColor(137, 158, 181);
                canvas.DrawText("dead", rect.MidX, rect.Bottom - 3, SKTextAlign.Center, deadLabel, paint);
            }
            if (mode == ModifierMode.OneShot)
            {
                canvas.DrawRoundRect(new SKRect(rect.MidX - 8, rect.Bottom - 6, rect.MidX + 8, rect.Bottom - 4), 1, 1, paint);
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.5f;
                canvas.DrawCircle(rect.Right - 9, rect.Top + 9, 3.3f, paint);
                paint.Style = SKPaintStyle.Fill;
            }
            else if (mode == ModifierMode.Locked)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.5f;
                canvas.DrawRoundRect(new SKRect(rect.Right - 12, rect.Top + 4, rect.Right - 6, rect.Top + 12), 3, 3, paint);
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawRoundRect(new SKRect(rect.Right - 13, rect.Top + 9, rect.Right - 5, rect.Top + 15), 1, 1, paint);
            }
        }
        paint.Color = new SKColor(137, 158, 181);
        canvas.Save();
        canvas.ClipRect(new SKRect(16, LayoutHeight - 23, LayoutWidth - 194, LayoutHeight - 2));
        canvas.DrawText(status, 16, LayoutHeight - 9, SKTextAlign.Left, small, paint);
        canvas.Restore();
        canvas.DrawText("Click once · twice to lock", LayoutWidth - 20, LayoutHeight - 9, SKTextAlign.Right, small, paint);
        canvas.Flush();
        return bitmap;
    }
}
