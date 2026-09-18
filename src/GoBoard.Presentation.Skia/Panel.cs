using SkiaSharp;
using GoBoard.Core;

namespace GoBoard.Presentation.Skia;

internal static class Panel
{
    public const int LayoutWidth = OverlayGeometry.PanelWidth, LayoutHeight = OverlayGeometry.PanelHeight;
    public const int RasterScale = OverlayGeometry.RasterScale;
    public const float WidthInMeters = OverlayGeometry.PanelWidthInMeters;
    public const float HeightInMeters = OverlayGeometry.PanelHeightInMeters;

    public static SKBitmap Render(KeyboardState keyboard = null, bool shift = false, string status = null, bool altGr = false, bool caps = false, bool scrollLock = false, string theme = BoardThemes.Default, bool cacheSurfaces = true)
    {
        var style = KeyboardTheme.Resolve(theme);
        var Accent = style.Accent;
        var Ink = style.Ink;
        var layout = keyboard?.Layout ?? new WindowsLayout((nint)WindowsLayout.UsHandle);
        var bitmap = new SKBitmap(new SKImageInfo(LayoutWidth * RasterScale, LayoutHeight * RasterScale, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(RasterScale);
        using var paint = new SKPaint { IsAntialias = true };
        using var face = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
        using var letter = new SKFont(face, 21);
        using var number = new SKFont(face, 18);
        using var special = new SKFont(face, style.SpecialSize);
        using var secondary = new SKFont(face, 12);
        using var notice = new SKFont(face, 9);
        canvas.Clear(style.Background);
        if (style.PanelFrame)
        {
            var panelRect = new SKRect(.5f, .5f, LayoutWidth - .5f, LayoutHeight - .5f);
            paint.Color = style.Surface;
            canvas.DrawRoundRect(panelRect, 6, 6, paint);
            paint.Color = style.Border;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = .8f;
            canvas.DrawRoundRect(panelRect, 6, 6, paint);
            paint.Style = SKPaintStyle.Fill;
        }

        foreach (var key in layout.Keys)
        {
            var b = key.Bounds;
            var rect = new SKRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height);
            var hover = keyboard?.Hovered(key) == true;
            var mode = key.IsModifier ? keyboard?.Mode(key.Scan) ?? ModifierMode.Idle : ModifierMode.Idle;
            var filled = mode == ModifierMode.Locked || (!key.IsModifier && keyboard?.Pressed(key) == true);
            var armed = mode == ModifierMode.OneShot;
            var toggleOn = (key.Id == "Caps" && caps) || (key.Id == "ScrollLock" && scrollLock);
            if (cacheSurfaces && style.ShadowBlur > 0)
                KeySurfaceCache.Draw(canvas, key, style, hover, filled, armed || toggleOn);
            else
                DrawKeySurface(canvas, key, style, hover, filled, armed || toggleOn);
            var foreground = filled ? Ink : armed || toggleOn ? Accent : style.Text;
            paint.Color = foreground;
            if (key.Printable)
            {
                var normal = layout.Legend(key, false, false, false);
                var shifted = layout.Legend(key, true, false, false);
                var alternate = layout.Legend(key, false, true, false);
                var active = layout.Legend(key, shift, altGr && layout.Swedish, caps);
                var isLetter = normal.Text.Length == 1 && char.IsLetter(normal.Text[0]);
                if (isLetter)
                    Center(canvas, active.Text, rect.MidX, rect.MidY, letter, paint);
                else
                {
                    canvas.DrawText(active.Text, rect.Left + 9, rect.Bottom - 6, SKTextAlign.Left, number, paint);
                    var upper = shift && !(altGr && layout.Swedish) ? normal.Text : shifted.Text;
                    paint.Color = filled ? Ink : style.Secondary;
                    if (upper != active.Text && upper != "—")
                        canvas.DrawText(upper, rect.Left + 9, rect.Top + 16, SKTextAlign.Left, secondary, paint);
                }
                // The current output stays primary; suppress duplicate auxiliary legends.
                if (layout.Swedish && alternate.Text != "—" && alternate.Text != active.Text)
                {
                    paint.Color = filled ? Ink : Accent;
                    canvas.DrawText(alternate.Text, rect.Right - 6, rect.Bottom - 6, SKTextAlign.Right, secondary, paint);
                }
                if (active.Dead)
                {
                    paint.Color = filled ? Ink : Accent;
                    canvas.DrawCircle(rect.Right - 5, rect.Top + 5, 1.3f, paint);
                }
            }
            else if (key.Id == "Win") DrawWindows(canvas, rect.MidX, rect.MidY, paint);
            else if (key.Id is "Up" or "Down" or "Left" or "Right") DrawArrow(canvas, key.Id, rect.MidX, rect.MidY, paint);
            else if (key.Id == "Backspace")
            {
                DrawReturn(canvas, rect.Left + 14, rect.MidY, false, paint);
                Center(canvas, "Backspace", rect.MidX + 9, rect.MidY, special, paint);
            }
            else if (key.Id == "Enter")
            {
                var cx = rect.MidX + key.CutoutWidth / 2;
                Center(canvas, "Enter", cx, rect.MidY - (layout.Swedish ? 5 : 0), special, paint);
                if (layout.Swedish) DrawReturn(canvas, cx, rect.MidY + 18, true, paint);
            }
            else
            {
                if (key.Id == "AltGr" && layout.Swedish && !filled) paint.Color = Accent;
                Center(canvas, layout.Legend(key, shift, altGr, caps).Text, rect.MidX, rect.MidY, special, paint);
            }
            paint.Color = filled ? Ink : Accent;
            if (armed)
            {
                canvas.DrawRoundRect(new SKRect(rect.MidX - 8, rect.Bottom - 5, rect.MidX + 8, rect.Bottom - 3), 1, 1, paint);
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.2f;
                canvas.DrawCircle(rect.Right - 7, rect.Top + 7, 2.5f, paint);
                paint.Style = SKPaintStyle.Fill;
            }
            else if (mode == ModifierMode.Locked)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.2f;
                canvas.DrawRoundRect(new SKRect(rect.Right - 11, rect.Top + 4, rect.Right - 5, rect.Top + 11), 3, 3, paint);
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawRoundRect(new SKRect(rect.Right - 12, rect.Top + 8, rect.Right - 4, rect.Top + 14), 1, 1, paint);
            }
            else if (toggleOn) canvas.DrawCircle(rect.Right - 7, rect.Top + 7, 2, paint);
        }
        var message = !string.IsNullOrWhiteSpace(status) ? status : !layout.Supported ? layout.Status : null;
        if (!string.IsNullOrWhiteSpace(message))
        {
            // Use the existing gap between navigation and arrows instead of
            // reserving an otherwise empty footer around the whole keyboard.
            var delete = layout.Keys.Single(k => k.Id == "Delete").Bounds;
            var pageDown = layout.Keys.Single(k => k.Id == "PageDown").Bounds;
            var up = layout.Keys.Single(k => k.Id == "Up").Bounds;
            var area = new SKRect(delete.X, delete.Y + delete.Height + 6,
                pageDown.X + pageDown.Width, up.Y - 4);
            paint.Color = style.Notice;
            DrawNotice(canvas, message, area, notice, paint);
        }
        canvas.Flush();
        return bitmap;
    }

    private static void Center(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint)
        => canvas.DrawText(text, x, y - (font.Metrics.Ascent + font.Metrics.Descent) / 2, SKTextAlign.Center, font, paint);

    // Also used by the uncached rendering path to verify cache fidelity.
    internal static void DrawKeySurface(SKCanvas canvas, KeyboardKey key, KeyboardTheme style, bool hover, bool filled, bool armed)
    {
        var b = key.Bounds;
        using var paint = new SKPaint { IsAntialias = true };
        using var shadow = style.ShadowBlur > 0 ? SKImageFilter.CreateDropShadowOnly(0, style.ShadowOffset,
            style.ShadowBlur, style.ShadowBlur, new SKColor(0, 0, 0, 77)) : null;
        if (shadow != null)
        {
            paint.Color = SKColors.Black;
            paint.ImageFilter = shadow;
            DrawKey(canvas, key, paint);
            paint.ImageFilter = null;
        }
        using var fill = SKShader.CreateLinearGradient(new SKPoint(0, b.Y), new SKPoint(0, b.Y + b.Height),
            armed ? style.ArmedStops : style.FaceStops, armed ? null : style.FacePositions, SKShaderTileMode.Clamp);
        paint.Color = filled ? style.Accent : SKColors.White;
        paint.Shader = filled ? null : fill;
        DrawKey(canvas, key, paint);
        paint.Shader = null;
        if (style.KeyEdges || (hover || armed) && !filled)
        {
            using var edge = SKShader.CreateLinearGradient(new SKPoint(0, b.Y), new SKPoint(0, b.Y + b.Height),
                style.EdgeStops, style.EdgePositions, SKShaderTileMode.Clamp);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = (hover && !filled) || !style.KeyEdges ? 1.2f : .6f;
            paint.Color = hover && !filled ? style.Accent : armed ? style.ArmedBorder : SKColors.White;
            paint.Shader = (!hover && !armed) || filled ? edge : null;
            DrawKey(canvas, key, paint);
        }
    }

    private static void DrawNotice(SKCanvas canvas, string message, SKRect area, SKFont font, SKPaint paint)
    {
        var remaining = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        const float lineHeight = 11;
        var lines = (int)(area.Height / lineHeight);
        canvas.Save();
        canvas.ClipRect(area);
        for (var line = 0; line < lines && remaining.Length > 0; line++)
        {
            var count = remaining.Length;
            var suffix = "";
            if (font.MeasureText(remaining, paint) > area.Width)
            {
                if (line == lines - 1) suffix = "…";
                while (count > 0 && font.MeasureText(remaining[..count] + suffix, paint) > area.Width) count--;
                if (suffix.Length == 0 && count > 0)
                {
                    var space = remaining.LastIndexOf(' ', count - 1, count);
                    if (space > 0) count = space;
                }
            }
            canvas.DrawText(remaining[..count].TrimEnd() + suffix, area.Left,
                area.Top - font.Metrics.Ascent + line * lineHeight, SKTextAlign.Left, font, paint);
            remaining = remaining[count..].TrimStart();
        }
        canvas.Restore();
    }

    private static void DrawKey(SKCanvas canvas, KeyboardKey key, SKPaint paint)
    {
        var b = key.Bounds;
        if (key.CutoutWidth == 0)
        {
            canvas.DrawRoundRect(new SKRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height), 4, 4, paint);
            return;
        }
        using var builder = new SKPathBuilder();
        builder.MoveTo(b.X, b.Y);
        builder.LineTo(b.X + b.Width, b.Y);
        builder.LineTo(b.X + b.Width, b.Y + b.Height);
        builder.LineTo(b.X + key.CutoutWidth, b.Y + b.Height);
        builder.LineTo(b.X + key.CutoutWidth, b.Y + key.CutoutTop);
        builder.LineTo(b.X, b.Y + key.CutoutTop);
        builder.Close();
        using var corners = SKPathEffect.CreateCorner(4);
        paint.PathEffect = corners;
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
        paint.PathEffect = null;
    }

    private static void DrawWindows(SKCanvas canvas, float x, float y, SKPaint paint)
    {
        for (var row = 0; row < 2; row++)
            for (var col = 0; col < 2; col++)
                canvas.DrawRect(x - 8 + col * 9, y - 8 + row * 9, 7.5f, 7.5f, paint);
    }

    private static void DrawArrow(SKCanvas canvas, string direction, float x, float y, SKPaint paint)
    {
        canvas.Save();
        canvas.Translate(x, y);
        canvas.RotateDegrees(direction switch { "Right" => 90, "Down" => 180, "Left" => 270, _ => 0 });
        using var builder = new SKPathBuilder();
        builder.MoveTo(0, -6); builder.LineTo(6, 5); builder.LineTo(-6, 5); builder.Close();
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    private static void DrawReturn(SKCanvas canvas, float x, float y, bool bent, SKPaint paint)
    {
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1.6f;
        using var builder = new SKPathBuilder();
        builder.MoveTo(x + 6, y - (bent ? 7 : 0));
        if (bent) builder.LineTo(x + 6, y + 1);
        builder.LineTo(x - 6, y + (bent ? 1 : 0));
        builder.MoveTo(x - 1, y - 5); builder.LineTo(x - 6, y + (bent ? 1 : 0)); builder.LineTo(x - 1, y + 6);
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
        paint.Style = SKPaintStyle.Fill;
    }
}
