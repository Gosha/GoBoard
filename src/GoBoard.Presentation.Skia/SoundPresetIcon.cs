using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

// Small vector pictograms stay crisp in the desktop and VR settings viewports.
internal static class SoundPresetIcon
{
    public const float Size = 40;

    public static void Draw(SKCanvas canvas, KeySound sound, float x, float y)
    {
        var colors = UiColors.Current.SoundIcons;
        sound = KeySounds.Canonical(sound);
        canvas.Save();
        canvas.Translate(x, y);
        using var paint = new SKPaint { IsAntialias = true, StrokeWidth = 1.8f,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        var ink = colors.Ink;
        paint.Color = colors.Background;
        canvas.DrawRoundRect(new SKRect(0, 0, Size, Size), 9, 9, paint);

        void Line(float x1, float y1, float x2, float y2, SKColor color)
        {
            paint.Color = color;
            canvas.DrawLine(x1, y1, x2, y2, paint);
        }
        void Box(SKRect rect, float radius, SKColor color, bool outline = false)
        {
            paint.Color = color;
            paint.Style = outline ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
            canvas.DrawRoundRect(rect, radius, radius, paint);
            paint.Style = SKPaintStyle.Fill;
        }
        switch (sound)
        {
            case KeySound.SoftLowThud:
                var lavender = colors.LowThud;
                paint.Color = lavender;
                canvas.DrawCircle(20, 15, 5, paint);
                paint.Style = SKPaintStyle.Stroke;
                // Broad, shallow ripples suggest a low, cushioned impact.
                using (var ripple = new SKPathBuilder())
                {
                    ripple.MoveTo(12, 23);
                    ripple.QuadTo(20, 29, 28, 23);
                    ripple.MoveTo(7, 28);
                    ripple.QuadTo(20, 37, 33, 28);
                    using var path = ripple.Detach();
                    canvas.DrawPath(path, paint);
                }
                break;
            case KeySound.CherryMxBlue:
            case KeySound.CherryMxClear:
            case KeySound.GateronYellowModified:
            case KeySound.GateronYellowPairs:
                var stem = sound == KeySound.CherryMxBlue ? colors.SwitchBlue :
                    sound == KeySound.CherryMxClear ? colors.SwitchClear : colors.SwitchYellow;
                Box(new SKRect(8, 16, 32, 32), 3, colors.SwitchBody);
                Box(new SKRect(8, 16, 32, 32), 3, colors.SwitchOutline, true);
                Box(new SKRect(13, 10, 27, 23), 2, stem);
                Line(17, 16.5f, 23, 16.5f, ink);
                Line(20, 13.5f, 20, 19.5f, ink);
                if (sound == KeySound.CherryMxBlue)
                {
                    // Two click rays distinguish the clicky switch by shape too.
                    Line(8, 8, 10, 11, stem);
                    Line(30, 11, 32, 8, stem);
                    Line(16, 28, 24, 28, stem);
                }
                else if (sound == KeySound.CherryMxClear)
                {
                    // A bump for the tactile switch.
                    Line(15, 29, 20, 26, stem);
                    Line(20, 26, 25, 29, stem);
                }
                else if (sound == KeySound.GateronYellowModified)
                {
                    // A small adjustment slider for the modified switch.
                    Line(14, 28, 26, 28, stem);
                    Line(23, 26, 23, 30, stem);
                }
                else
                {
                    // Separate down/up marks identify the paired press and release.
                    Line(14, 26, 17, 29, stem);
                    Line(17, 29, 20, 26, stem);
                    Line(22, 29, 25, 26, stem);
                    Line(25, 26, 28, 29, stem);
                }
                break;
            default:
                var wood = colors.Wood;
                Box(new SKRect(7, 18, 33, 32), 3, wood);
                Line(11, 24, 20, 24, ink);
                Line(17, 28, 29, 28, ink);
                Box(new SKRect(7, 9, 33, 18), 4, colors.WoodHighlight);
                break;
        }
        canvas.Restore();
    }
}
