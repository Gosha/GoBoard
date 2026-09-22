using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

// One renderer per host. Input remains synchronous; only the resulting visual
// state is animated. Cached immutable images are reused throughout a transition.
internal sealed partial class AnimatedKeyboardRenderer : IDisposable
{
    private readonly KeyTransitions transitions = new();
    private readonly Dictionary<uint, (PointerEffects Effects, long Press)> pointers = new();
    private readonly Dictionary<string, SKPath> paths = new();
    private IReadOnlyList<KeyboardKey> keys;
    private KeyboardTheme style;
    private SKImage baseline, surfaces, outputBaseline;
    private (SKImageInfo Info, SKRect Bounds)? cachedOutput;
    private KeyboardState bound;
    private WindowsLayout layout;
    private EffectSettings options;
    private string theme;
    private (int Revision, bool Shift, bool AltGr, bool Caps, bool Scroll, string Status)? drawn;
    private (int Revision, bool Shift, bool AltGr, bool Caps, bool Scroll, string Status)? cached;
    private (SKImageInfo Info, SKRect Bounds)? drawnOutput;
    internal int BaselineBuildCount { get; private set; }
    private (bool Shift, bool AltGr, bool Caps)? legends;
    private int visualRevision = -1, cancellationRevision = -1;
    private bool wasAnimating;
    private GRContext cacheContext;

    // Null means the host can keep its existing texture/frame, with no upload.
    public SKBitmap Render(KeyboardState keyboard, bool shift, string status, bool altGr, bool caps,
        bool scroll, string theme, EffectSettings options, double now, SKImageInfo? outputInfo = null, SKRect? contentBounds = null)
    {
        var output = outputInfo ?? new SKImageInfo(keyboard.Width * Panel.RasterScale, keyboard.Height * Panel.RasterScale,
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        SKBitmap result = null;
        SKCanvas canvas = null;
        try
        {
            DrawFrame(() => canvas = new SKCanvas(result = new SKBitmap(output)), null,
                keyboard, shift, status, altGr, caps, scroll, theme, options, now, output, contentBounds);
            return result;
        }
        catch { canvas?.Dispose(); canvas = null; result?.Dispose(); throw; }
        finally { canvas?.Dispose(); }
    }

    // A false return leaves the existing submitted texture untouched. The host
    // retains canvas/context ownership, including flush and presentation timing.
    public bool Draw(SKCanvas canvas, GRContext context, KeyboardState keyboard, bool shift, string status,
        bool altGr, bool caps, bool scroll, string theme, EffectSettings options, double now,
        SKImageInfo output, SKRect? contentBounds = null) =>
        DrawFrame(() => canvas, context, keyboard, shift, status, altGr, caps, scroll, theme, options, now, output, contentBounds);

    private bool DrawFrame(Func<SKCanvas> acquireCanvas, GRContext context, KeyboardState keyboard,
        bool shift, string status, bool altGr, bool caps, bool scroll, string theme, EffectSettings options,
        double now, SKImageInfo output, SKRect? contentBounds)
    {
        var bounds = contentBounds ?? new SKRect(0, 0, output.Width, output.Height);
        var presentation = (output, bounds);
        var reset = bound != keyboard || layout != keyboard.Layout || this.theme != theme || this.options != options ||
            cancellationRevision != keyboard.CancellationRevision || cacheContext != context;
        if (reset)
        {
            Reset();
            cacheContext = context;
            bound = keyboard; layout = keyboard.Layout; this.theme = theme; this.options = options;
            cancellationRevision = keyboard.CancellationRevision;
            style = KeyboardTheme.Resolve(theme);
            keys = keyboard.Keys;
            foreach (var key in keys) paths[key.Id] = KeyPath(key);
            // Never replay clicks from before a layout/settings change or cancel.
            foreach (var p in keyboard.VisualPointers) pointers[p.Cursor] = (new(), p.PressSequence);
        }
        var signature = (keyboard.Revision, shift, altGr, caps, scroll, status);
        var dirty = drawn != signature;
        var labels = (shift, altGr, caps);
        if (legends != labels && options.Transition != CharacterTransition.None && options.TransitionMs > 0)
            transitions.Update(layout, shift, altGr, caps, style, options, now, legends == null, keys, context);
        legends = labels;

        var motion = visualRevision != keyboard.VisualRevision;
        if (options.PointerEnabled && (dirty || motion))
            foreach (var p in keyboard.VisualPointers)
            {
                if (!pointers.TryGetValue(p.Cursor, out var entry)) entry = (new(), 0);
                var fx = entry.Effects;
                if (p.PressSequence != entry.Press && p.LastPressed != null)
                {
                    fx.Move(new(p.PressX, p.PressY), keys, options, now);
                    fx.Down(now, options);
                }
                if (p.Focused && float.IsFinite(p.X) && float.IsFinite(p.Y) &&
                    p.X >= 0 && p.Y >= 0 && p.X < keyboard.Width && p.Y < keyboard.Height)
                    fx.Move(new(p.X, p.Y), keys, options, now);
                else fx.Leave(options, now);
                fx.SetPressed(p.Held == fx.Hover ? p.Held : null);
                pointers[p.Cursor] = (fx, p.PressSequence);
            }
        foreach (var p in pointers.Values) p.Effects.Prune(now);
        var animating = transitions.Animating(now) || options.PointerEnabled && pointers.Values.Any(p => p.Effects.Animating(now, options));
        var repaint = dirty || reset || drawnOutput != presentation || animating || wasAnimating || motion && options.PointerEnabled && (options.Spotlight || options.Edges);
        visualRevision = keyboard.VisualRevision;
        wasAnimating = animating;
        if (!repaint) return false;
        var content = (options.PointerEnabled ? keyboard.ContentRevision : keyboard.Revision, shift, altGr, caps, scroll, status);
        if (cached != content || baseline == null)
        {
            BaselineBuildCount++;
            cachedOutput = null;
            var info = new SKImageInfo(keyboard.Width * Panel.RasterScale, keyboard.Height * Panel.RasterScale,
                SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var next = RenderImage.Create(info, context, canvas => Panel.Draw(canvas, keyboard, shift, status, altGr, caps, scroll, theme,
                suppressHover: options.PointerEnabled, suppressPressed: options.PressFlash));
            baseline?.Dispose(); baseline = next;
            surfaces?.Dispose(); surfaces = null;
            if (transitions.Animating(now))
            {
                surfaces = RenderImage.Create(info, context, canvas => Panel.Draw(canvas, keyboard, shift, status, altGr, caps, scroll, theme,
                    omitPrintableLegends: true, suppressHover: options.PointerEnabled, suppressPressed: options.PressFlash));
            }
            cached = content;
        }
        if (cachedOutput != presentation)
        {
            outputBaseline?.Dispose(); outputBaseline = null;
            // Resize/swizzle the settled keyboard once, not once per animated
            // frame. Desktop usually needs far fewer pixels than the VR texture.
            if (output.Width != baseline.Width || output.Height != baseline.Height || output.ColorType != baseline.ColorType || output.AlphaType != baseline.AlphaType ||
                bounds != new SKRect(0, 0, output.Width, output.Height))
            {
                outputBaseline = RenderImage.Create(output, context, target =>
                {
                    target.Clear(output.AlphaType == SKAlphaType.Opaque ? style.Background : SKColors.Transparent);
                    target.DrawImage(baseline, bounds, new SKSamplingOptions(SKFilterMode.Linear));
                });
            }
            cachedOutput = presentation;
        }
        var canvas = acquireCanvas();
        using var restore = new SKAutoCanvasRestore(canvas, true);
        canvas.Clear(output.AlphaType == SKAlphaType.Opaque ? style.Background : SKColors.Transparent);
        canvas.DrawImage(outputBaseline ?? baseline, new SKRect(0, 0, output.Width, output.Height), new SKSamplingOptions(SKFilterMode.Nearest));
        canvas.ClipRect(bounds);
        canvas.Translate(bounds.Left, bounds.Top);
        canvas.Scale(bounds.Width / keyboard.Width, bounds.Height / keyboard.Height);
        if (surfaces != null) transitions.Draw(canvas, baseline, surfaces, keyboard, style, now, filledPress: !options.PressFlash);
        if (options.PointerEnabled)
            foreach (var p in pointers.Values) DrawPointer(canvas, keyboard, p.Effects, options, now, caps, scroll);
        drawn = signature;
        drawnOutput = presentation;
        return true;
    }

    public void Reset()
    {
        transitions.Dispose(); pointers.Clear();
        foreach (var path in paths.Values) path.Dispose(); paths.Clear();
        baseline?.Dispose(); baseline = null; surfaces?.Dispose(); surfaces = null;
        outputBaseline?.Dispose(); outputBaseline = null; cachedOutput = null;
        cacheContext = null;
        bound = null; layout = null; drawn = null; cached = null; drawnOutput = null; legends = null; visualRevision = -1; wasAnimating = false;
    }
    public void Dispose() => Reset();
}
