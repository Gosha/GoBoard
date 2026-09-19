using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal sealed partial class AnimatedKeyboardRenderer
{
    private static SKPath KeyPath(KeyboardKey key)
    {
        var b = key.Bounds;
        using var builder = new SKPathBuilder();
        if (key.CutoutWidth == 0)
        {
            builder.AddRoundRect(new SKRoundRect(new SKRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height), 4, 4));
            return builder.Detach();
        }
        builder.MoveTo(b.X, b.Y); builder.LineTo(b.X + b.Width, b.Y);
        builder.LineTo(b.X + b.Width, b.Y + b.Height);
        builder.LineTo(b.X + key.CutoutWidth, b.Y + b.Height);
        builder.LineTo(b.X + key.CutoutWidth, b.Y + key.CutoutTop);
        builder.LineTo(b.X, b.Y + key.CutoutTop); builder.Close();
        using var raw = builder.Detach();
        using var corner = SKPathEffect.CreateCorner(4);
        using var paint = new SKPaint { PathEffect = corner };
        return paint.GetFillPath(raw);
    }


    private void DrawPointer(SKCanvas canvas, KeyboardState keyboard, PointerEffects effects, EffectSettings options, double now)
    {
        using var paint = new SKPaint { IsAntialias = true };
        var accent = style.Accent;
        var strength = options.Strength / 100f;
        var presence = effects.Presence.Value(now);
        // The light is shared by every key. Build each shader once per view,
        // instead of allocating identical shaders/arrays for every nearby key.
        using var spotlight = options.Spotlight && presence > .001
            ? SKShader.CreateRadialGradient(effects.Pointer, options.Radius,
                [accent.WithAlpha((byte)(115 * strength * presence)), accent.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp) : null;
        using var edgeLight = options.Edges && presence > .001
            ? SKShader.CreateRadialGradient(effects.Pointer, options.Radius * 1.3f,
                [accent.WithAlpha((byte)(255 * strength * presence)), accent.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp) : null;
        foreach (var key in keys)
        {
            var path = paths[key.Id];
            var hover = effects.Trails.GetValueOrDefault(key.Id)?.Value(now) ?? 0;
            var b = key.Bounds;
            if (canvas.QuickReject(new SKRect(b.X - 1, b.Y - 1, b.X + b.Width + 1, b.Y + b.Height + 1))) continue;
            var dx = Math.Max(Math.Max(b.X - effects.Pointer.X, 0), effects.Pointer.X - b.X - b.Width);
            var dy = Math.Max(Math.Max(b.Y - effects.Pointer.Y, 0), effects.Pointer.Y - b.Y - b.Height);
            var nearLight = dx * dx + dy * dy < options.Radius * options.Radius * 1.69f;
            // Persistent modifier markers remain unambiguous: blue locked fill
            // and one-shot underline/outline come from the production renderer.
            var locked = key.IsModifier && keyboard.Mode(key.Scan) == ModifierMode.Locked;
            if (!locked)
            {
                var hasPulse = false;
                if (options.Ripples || options.PressFlash)
                    foreach (var pulse in effects.Pulses)
                        if (pulse.Key == key) { hasPulse = true; break; }
                var lightFace = spotlight != null && nearLight;
                var glowFace = options.Afterglow && hover > .001;
                var heldFace = options.PressFlash && effects.Pressed == key;
                if (lightFace || glowFace || heldFace || hasPulse)
                {
                    canvas.Save(); canvas.ClipPath(path, antialias: true);
                    if (lightFace)
                    {
                        paint.Color = SKColors.White;
                        paint.Shader = spotlight; canvas.DrawPath(path, paint); paint.Shader = null;
                    }
                    if (glowFace)
                    {
                        paint.Color = accent.WithAlpha((byte)(35 * strength * hover)); canvas.DrawPath(path, paint);
                    }
                    if (hasPulse)
                        foreach (var pulse in effects.Pulses)
                        {
                            if (pulse.Key != key) continue;
                            var t = (float)Math.Clamp((now - pulse.Start) / pulse.Duration, 0, 1);
                            if (options.PressFlash)
                            {
                                var flash = Math.Max(0, 1 - t * 2.5f);
                                paint.Color = accent.WithAlpha((byte)(95 * strength * flash)); canvas.DrawPath(path, paint);
                            }
                            if (options.Ripples && t < 1)
                            {
                                paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1.6f;
                                paint.Color = accent.WithAlpha((byte)(230 * strength * (1 - t)));
                                canvas.DrawCircle(pulse.Origin, 3 + MathF.Sqrt(t) * Math.Min(120, Math.Max(key.Bounds.Width, key.Bounds.Height)), paint);
                                paint.Style = SKPaintStyle.Fill;
                            }
                        }
                    if (heldFace)
                    {
                        paint.Color = accent.WithAlpha((byte)(55 * strength)); canvas.DrawPath(path, paint);
                    }
                    canvas.Restore();
                }
                if (edgeLight != null && nearLight)
                {
                    paint.Color = SKColors.White;
                    paint.Shader = edgeLight; paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1;
                    canvas.DrawPath(path, paint); paint.Shader = null; paint.Style = SKPaintStyle.Fill;
                }
            }
            if (hover > .001 && !locked)
            {
                paint.Color = accent.WithAlpha((byte)(230 * hover)); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1.2f;
                canvas.DrawPath(path, paint); paint.Style = SKPaintStyle.Fill;
            }
        }
    }
}
