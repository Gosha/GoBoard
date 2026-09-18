using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal static class SettingsPanel
{
    public static SKBitmap Render(BoardSettings settings, SettingsPointerState pointers = null, string error = null, bool desktopMode = false)
    {
        var bitmap = new SKBitmap(SettingsControls.Width * 2, SettingsControls.Height * 2, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(2);
        canvas.Clear(new SKColor(0x0c, 0x15, 0x1e));
        using var paint = new SKPaint { IsAntialias = true };
        using var face = SKTypeface.FromFamilyName("Segoe UI");
        using var heading = new SKFont(face, 38);
        using var label = new SKFont(face, 25);
        using var small = new SKFont(face, 19);
        var text = new SKColor(0xf1, 0xf6, 0xfc);
        var accent = new SKColor(0x66, 0xc0, 0xf4);
        void Text(string value, float x, float y, SKFont font, SKColor color)
        {
            paint.Color = color;
            canvas.DrawText(value, x, y, SKTextAlign.Left, font, paint);
        }
        Text("GoBoard", 64, 72, heading, text);
        Text("Settings · Changes apply immediately", 64, 110, small, accent);
        Text($"Keyboard size   {settings.SizePercent}%", 64, 186, label, text);
        Text(desktopMode ? "Desktop scale · 50–150%" : $"{OverlayGeometry.PanelWidthInMeters * settings.Scale * 100:F0} cm wide · 50–150%", 64, 216, small, accent);
        Text("Key sounds", 64, 290, label, text);
        Text("Sound preset", 64, 347, small, accent);
        Text($"Volume   {settings.VolumePercent}%", 64, 511, label, text);
        Text(error == null ? "Saved on this PC · Shared by desktop and VR" : "Settings unavailable · Last working values kept",
            64, 558, small, error == null ? accent : new SKColor(0xff, 0xb0, 0xa0));
        foreach (var c in SettingsControls.All)
        {
            var selected = c.Action == SettingsAction.Wood && settings.Sound == KeySound.CushionedWood ||
                c.Action == SettingsAction.Thud && settings.Sound == KeySound.SoftLowThud ||
                c.Action == SettingsAction.ToggleSound && settings.SoundEnabled;
            var enabled = SettingsControls.Enabled(c.Action, settings);
            var b = c.Bounds;
            var rect = new SKRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height);
            paint.Color = selected ? accent : new SKColor(0x1b, 0x2c, 0x39);
            canvas.DrawRoundRect(rect, 10, 10, paint);
            if (enabled && pointers?.Hovered(c.Action) == true)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 3;
                paint.Color = text;
                canvas.DrawRoundRect(rect, 10, 10, paint);
                paint.Style = SKPaintStyle.Fill;
            }
            paint.Color = !enabled ? new SKColor(0x66, 0x78, 0x82) : selected ? new SKColor(0x09, 0x19, 0x23) : text;
            var title = c.Action == SettingsAction.ToggleSound ? settings.SoundEnabled ? "On" : "Off" : c.Label;
            var font = c.Action == SettingsAction.Defaults ? small : label;
            canvas.DrawText(title, rect.MidX, rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2, SKTextAlign.Center, font, paint);
        }
        return bitmap;
    }

    public static SKBitmap Icon()
    {
        var bitmap = new SKBitmap(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0x0c, 0x15, 0x1e));
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0x66, 0xc0, 0xf4) };
        for (var row = 0; row < 3; row++)
        for (var col = 0; col < 5; col++)
            canvas.DrawRoundRect(33 + col * 39, 61 + row * 37, 32, 29, 4, 4, paint);
        canvas.DrawRoundRect(72, 172, 110, 23, 4, 4, paint);
        return bitmap;
    }
}
