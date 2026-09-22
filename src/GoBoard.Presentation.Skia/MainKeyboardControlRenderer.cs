using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal static class MainKeyboardControlRenderer
{
    public static SKBitmap Render(KeyboardAction action, bool enabled, bool hovered, bool pressed, string theme)
    {
        const int size = MainKeyboardControls.Size;
        var bitmap = new SKBitmap(size * Panel.RasterScale, size * Panel.RasterScale, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(Panel.RasterScale);
        var style = KeyboardStyle.Resolve(theme);
        using var paint = new SKPaint { IsAntialias = true };
        if (action == KeyboardAction.ToggleNumpad)
        {
            var inset = enabled ? 0 : 2;
            Panel.DrawKeySurface(canvas, new("Numpad", "", 0, new(inset, inset, size - 2 * inset, size - 2 * inset)), style, hovered, pressed, enabled, armed: enabled);
            paint.Color = pressed ? style.Colors.KeyLabelOnAccent : enabled ? style.Colors.KeyLabelAccent : style.Colors.KeyLabel;
            for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
                canvas.DrawRoundRect(new SKRect(12 + col * 8, 9 + row * 8, 16 + col * 8, 13 + row * 8), 1, 1, paint);
            canvas.DrawRoundRect(new SKRect(12, 33, 24, 37), 1, 1, paint);
            canvas.DrawRoundRect(new SKRect(28, 33, 32, 37), 1, 1, paint);
        }
        else
        {
            // Only the arrow has alpha; never paint a tile, border or hover plate.
            paint.Color = pressed ? style.Colors.KeyLabelAccent : hovered ? style.Colors.KeyLabel : style.Colors.KeyLabelSecondary;
            paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = hovered || pressed ? 3 : 2.5f;
            paint.StrokeCap = SKStrokeCap.Round; paint.StrokeJoin = SKStrokeJoin.Round;
            canvas.DrawLine(22, 10, 22, 33, paint);
            using var builder = new SKPathBuilder();
            builder.MoveTo(12, 23); builder.LineTo(22, 33); builder.LineTo(32, 23);
            using var arrow = builder.Detach();
            canvas.DrawPath(arrow, paint);
        }
        return bitmap;
    }

    // Compose production surfaces at their actual relative VR positions for PNG review.
    public static SKBitmap Compose(SKBitmap keyboard, bool numpad, string theme)
    {
        var units = ProgrammableKeys.MetersPerUnit;
        var height = OverlayGeometry.PanelHeight;
        KeyBounds Bounds(KeyboardAction action)
        {
            var offset = MainKeyboardControls.Offset(action, 1);
            var size = MainKeyboardControls.WidthInMeters(action, 1) / units;
            return new(OverlayGeometry.PanelWidth / 2f + offset.M41 / units - size / 2, height / 2f - offset.M42 / units - size / 2, size, size);
        }
        var toggle = Bounds(KeyboardAction.ToggleNumpad);
        var reset = Bounds(KeyboardAction.ResetPosition);
        var gripWidth = OverlayGeometry.GrabWidthInMeters / units;
        var gripHeight = .06f / units;
        var grip = new KeyBounds((OverlayGeometry.PanelWidth - gripWidth) / 2, height + .0075f / units, gripWidth, gripHeight);
        var result = new SKBitmap((int)Math.Ceiling(Math.Max(keyboard.Width / (float)Panel.RasterScale, toggle.X + toggle.Width) + 4) * Panel.RasterScale,
            (int)Math.Ceiling(Math.Max(grip.Y + grip.Height, reset.Y + reset.Height) + 4) * Panel.RasterScale,
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(keyboard, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        canvas.Scale(Panel.RasterScale);
        void Draw(SKBitmap image, KeyBounds b) => canvas.DrawBitmap(image, new SKRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height),
            new SKSamplingOptions(SKFilterMode.Linear));
        using var numpadButton = Render(KeyboardAction.ToggleNumpad, numpad, false, false, theme);
        using var resetButton = Render(KeyboardAction.ResetPosition, false, false, false, theme);
        using var handle = GrabHandleRenderer.Render(0);
        Draw(numpadButton, toggle); Draw(resetButton, reset); Draw(handle, grip);
        return result;
    }
}
