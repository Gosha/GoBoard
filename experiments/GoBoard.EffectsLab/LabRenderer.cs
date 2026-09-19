using GoBoard.Core;
using SkiaSharp;
using ProductionPanel = GoBoard.Presentation.Skia.Panel;

namespace GoBoard.EffectsLab;

internal sealed class LabRenderer : IDisposable
{
    private SKBitmap baseline;
    // DrawBitmap wraps a mutable bitmap as an image on every call. Cache the
    // snapshot: transitions and close-ups reuse it many times per frame.
    private SKImage baselineImage;
    private string visualState;
    private WindowsLayout layout;
    public readonly KeyTransitions Transitions = new();
    private IReadOnlyList<KeyboardKey> keys;
    private readonly Dictionary<string, SKPath> paths = new();
    private readonly SKColor accent = new(0x66, 0xc0, 0xf4);

    private void Prepare(LocalSession session, EffectOptions options, double now)
    {
        var keyboard = session.Keyboard;
        var state = $"{keyboard.Shift}/{keyboard.AltGr}/{session.Caps}/{session.Scroll}/" +
            string.Join(',', keyboard.Layout.Keys.Where(k => k.IsModifier).Select(k => (int)keyboard.Mode(k.Scan)));
        var layoutChanged = !ReferenceEquals(layout, keyboard.Layout);
        if (baseline == null || visualState != state || layoutChanged)
        {
            Transitions.Update(session, options, now, layoutChanged);
            baselineImage?.Dispose();
            baseline?.Dispose();
            // Preserve the experiment's flat faces: character crops currently
            // clear to that solid face color when sliding/flipping legends.
            baseline = ProductionPanel.Render(keyboard, keyboard.Shift, altGr: keyboard.AltGr, caps: session.Caps, scrollLock: session.Scroll, theme: BoardThemes.SteamFlat);
            baselineImage = SKImage.FromBitmap(baseline);
            visualState = state; layout = keyboard.Layout;
        }
        if (ReferenceEquals(keys, keyboard.Layout.Keys)) return;
        foreach (var path in paths.Values) path.Dispose();
        paths.Clear(); keys = keyboard.Layout.Keys;
        foreach (var key in keys) paths[key.Id] = KeyPath(key);
    }

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

    public void Draw(SKCanvas canvas, SKRect destination, LocalSession session, Effects effects, EffectOptions options, double now, bool baselineOnly = false, bool demoPointer = false)
    {
        Prepare(session, options, now);
        canvas.Save();
        canvas.ClipRect(destination);
        canvas.Translate(destination.Left, destination.Top);
        canvas.Scale(destination.Width / ProductionPanel.LayoutWidth, destination.Height / ProductionPanel.LayoutHeight);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(baselineImage, new SKRect(0, 0, ProductionPanel.LayoutWidth, ProductionPanel.LayoutHeight), new SKSamplingOptions(SKFilterMode.Linear));
        if (!baselineOnly && options.Transition != TransitionStyle.None) Transitions.Draw(canvas, baselineImage, now);
        var strength = options.Strength / 100f;
        var presence = effects.Presence.Value(now);
        // The light is shared by every key. Build each shader once per view,
        // instead of allocating identical shaders/arrays for every nearby key.
        using var spotlight = !baselineOnly && options.Spotlight && presence > .001
            ? SKShader.CreateRadialGradient(effects.Pointer, options.Radius,
                [accent.WithAlpha((byte)(115 * strength * presence)), accent.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp) : null;
        using var edgeLight = !baselineOnly && options.Edges && presence > .001
            ? SKShader.CreateRadialGradient(effects.Pointer, options.Radius * 1.3f,
                [accent.WithAlpha((byte)(255 * strength * presence)), accent.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp) : null;
        foreach (var key in keys)
        {
            var path = paths[key.Id];
            var hover = baselineOnly
                ? (effects.Hover == key ? 1 : 0)
                : effects.Trails.GetValueOrDefault(key.Id)?.Value(now) ?? 0;
            var b = key.Bounds;
            if (canvas.QuickReject(new SKRect(b.X - 1, b.Y - 1, b.X + b.Width + 1, b.Y + b.Height + 1))) continue;
            var dx = Math.Max(Math.Max(b.X - effects.Pointer.X, 0), effects.Pointer.X - b.X - b.Width);
            var dy = Math.Max(Math.Max(b.Y - effects.Pointer.Y, 0), effects.Pointer.Y - b.Y - b.Height);
            var nearLight = dx * dx + dy * dy < options.Radius * options.Radius * 1.69f;
            // Persistent modifier markers remain unambiguous: blue locked fill
            // and one-shot underline/outline come from the production renderer.
            var locked = key.IsModifier && session.Keyboard.Mode(key.Scan) == ModifierMode.Locked;
            if (!baselineOnly && !locked)
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
        if (demoPointer && presence > .001)
        {
            paint.Color = SKColors.White.WithAlpha(220); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = .8f;
            canvas.DrawCircle(effects.Pointer, 3, paint);
        }
        canvas.Restore();
    }

    public void Save(string path, LocalSession session, Effects effects, EffectOptions options, double now)
    {
        using var bitmap = new SKBitmap(1700, 564);
        using var canvas = new SKCanvas(bitmap);
        Draw(canvas, new(0, 0, bitmap.Width, bitmap.Height), session, effects, options, now);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path); data.SaveTo(stream);
    }
    public void Dispose()
    {
        baselineImage?.Dispose(); baseline?.Dispose(); Transitions.Dispose(); foreach (var path in paths.Values) path.Dispose(); paths.Clear();
    }
}
