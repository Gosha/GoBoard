using SkiaSharp;

namespace SteamVR.StereoDashboard;

internal static class StereoPanel
{
    public const int EyeWidth = 1024;
    public const int Height = 640;

    public static SKBitmap Render()
    {
        // Full-resolution side-by-side: left eye, then right eye. SetOverlayRaw
        // consumes tightly packed RGBA. Everything is opaque to avoid alpha ambiguity.
        var bitmap = new SKBitmap(EyeWidth * 2, Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        DrawEye(canvas, true);
        canvas.Translate(EyeWidth, 0);
        DrawEye(canvas, false);
        canvas.Flush();
        return bitmap;
    }

    private static void DrawEye(SKCanvas canvas, bool left)
    {
        using var paint = new SKPaint { IsAntialias = true };
        using var face = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
        using var title = new SKFont(face, 46);
        using var body = new SKFont(face, 23);
        using var small = new SKFont(face, 18);
        // Clear ignores the canvas transform, so draw the background as a rectangle.
        paint.Color = new SKColor(16, 22, 34);
        canvas.DrawRect(0, 0, EyeWidth, Height, paint);
        paint.Color = new SKColor(93, 225, 193);
        canvas.DrawText("STEREO / EXPERIMENT 01", 48, 53, SKTextAlign.Left, small, paint);
        paint.Color = new SKColor(243, 247, 253);
        canvas.DrawText("Depth inside the dashboard", 48, 114, SKTextAlign.Left, title, paint);
        paint.Color = new SKColor(156, 173, 195);
        canvas.DrawText("Three targets. One stereoscopic texture.", 48, 157, SKTextAlign.Left, body, paint);

        var names = new[] { "NEAR", "ON THE PANEL", "FAR" };
        var captions = new[] { "In front of the surface", "At the surface", "Behind the surface" };
        var colors = new[] { new SKColor(93, 225, 193), new SKColor(227, 234, 245), new SKColor(149, 165, 255) };
        // Crossed disparity gives a near target: left-eye image lies to the right
        // of the right-eye image. Equal and opposite offsets give a far target.
        var disparities = new[] { 12f, 0f, -12f };
        for (var i = 0; i < 3; i++)
        {
            float x = 196 + i * 316;
            paint.Color = new SKColor(25, 35, 51);
            canvas.DrawRoundRect(new SKRect(x - 148, 199, x + 148, 476), 18, 18, paint);
            paint.Color = new SKColor(54, 70, 91);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1;
            for (var step = -2; step <= 2; step++)
            {
                canvas.DrawLine(x - 116, 324 + step * 32, x + 116, 324 + step * 32, paint);
                canvas.DrawLine(x + step * 32, 230, x + step * 32, 409, paint);
            }
            paint.Style = SKPaintStyle.Fill;
            paint.Color = colors[i];
            float eyeX = x + (left ? 0.5f : -0.5f) * disparities[i];
            canvas.DrawCircle(eyeX, 321, 58, paint);
            paint.Color = new SKColor(16, 22, 34);
            canvas.DrawText((i + 1).ToString(), eyeX, 337, SKTextAlign.Center, title, paint);
            paint.Color = colors[i];
            canvas.DrawText(names[i], x, 447, SKTextAlign.Center, small, paint);
            paint.Color = new SKColor(156, 173, 195);
            canvas.DrawText(captions[i], x, 512, SKTextAlign.Center, small, paint);
        }

        // Markers occupy different locations to avoid conflicting glyphs when fused.
        paint.Color = new SKColor(93, 225, 193);
        canvas.DrawText(left ? "L" : "R", left ? 48 : 958, 597, SKTextAlign.Left, body, paint);
        paint.Color = new SKColor(156, 173, 195);
        canvas.DrawText("Close either eye to check L / R routing. Circles should stay round.", 512, 595, SKTextAlign.Center, small, paint);
    }

    public static SKBitmap RenderThumbnail()
    {
        var bitmap = new SKBitmap(128, 128, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(16, 22, 34));
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(93, 225, 193), Style = SKPaintStyle.Stroke, StrokeWidth = 7 };
        canvas.DrawRoundRect(new SKRect(15, 37, 113, 91), 16, 16, paint);
        canvas.DrawCircle(43, 64, 14, paint);
        canvas.DrawCircle(85, 64, 14, paint);
        canvas.Flush();
        return bitmap;
    }
}
