using GoBoard.Core;
using GoBoard.Presentation.Skia;
using SkiaSharp;

namespace GoBoard.EffectsLab;

internal enum TransitionStyle { None, Crossfade, Lift, Flip, Cascade }

internal sealed class KeyTransitions : IDisposable
{
    private sealed record Track(SKImage From, double Start, double Duration, TransitionStyle Style, int Travel);
    private readonly Dictionary<(string Key, int Slot), Track> tracks = new();
    private CharacterLayers current;
    public int ChangedCount => tracks.Keys.Select(k => k.Key).Distinct().Count();
    public bool Animating(double now) => tracks.Values.Any(t => now < t.Start + t.Duration);
    public bool Contains(string id) => tracks.Keys.Any(k => k.Key == id);
    internal bool Contains(string id, int slot) => tracks.ContainsKey((id, slot));

    public void Update(LocalSession session, EffectOptions options, double now, bool layoutChanged)
    {
        var next = CharacterLayers.Capture(session);
        if (!layoutChanged && current != null && current.Keys.All(p =>
                p.Value.Layers.Select(l => l.Label).SequenceEqual(next.Keys[p.Key].Layers.Select(l => l.Label))))
        {
            next.Dispose(); return; // Ctrl/locking Shift doesn't restart letters.
        }
        var nextTracks = new Dictionary<(string Key, int Slot), Track>();
        if (!layoutChanged && current != null && options.Transition != TransitionStyle.None && options.TransitionMs > 0)
            foreach (var key in next.Keys.Values)
            {
                var b = key.Key.Bounds;
                var distance = Math.Sqrt(Math.Pow(b.X + b.Width / 2 - session.LastPosition.X, 2) + Math.Pow(b.Y + b.Height / 2 - session.LastPosition.Y, 2));
                var delay = options.Transition == TransitionStyle.Cascade ? distance / OverlayGeometry.PanelWidth * options.StaggerMs / 1000.0 : 0;
                for (var slot = 0; slot < key.Layers.Length; slot++)
                {
                    var id = (key.Key.Id, slot);
                    var old = current.Keys[key.Key.Id].Layers[slot];
                    tracks.TryGetValue(id, out var oldTrack);
                    var moving = oldTrack != null && now < oldTrack.Start + oldTrack.Duration;
                    if (!moving && old.Label == key.Layers[slot].Label) continue;
                    // Capture just this label's visible frame on interruption.
                    using var bitmap = new SKBitmap((int)(b.Width * OverlayGeometry.RasterScale), (int)(b.Height * OverlayGeometry.RasterScale), SKColorType.Rgba8888, SKAlphaType.Premul);
                    using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent); canvas.Scale(OverlayGeometry.RasterScale);
                    var rect = new SKRect(0, 0, b.Width, b.Height);
                    if (moving) DrawTrack(canvas, oldTrack, old.Image, rect, now);
                    else Stamp(canvas, old.Image, rect);
                    nextTracks[id] = new(SKImage.FromBitmap(bitmap), now + delay, options.TransitionMs / 1000.0, options.Transition, options.Travel);
                }
            }
        Reset(); current?.Dispose(); current = next;
        foreach (var pair in nextTracks) tracks.Add(pair.Key, pair.Value);
    }

    private static void Stamp(SKCanvas canvas, SKImage image, SKRect rect, float alpha = 1)
    {
        if (image == null || alpha <= 0) return;
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)Math.Round(255 * alpha), 0, 255)) };
        canvas.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Linear), paint);
    }
    private static void DrawTrack(SKCanvas canvas, Track track, SKImage target, SKRect rect, double now)
    {
        // Composite the character on transparency before applying it to the
        // face, matching the snapshot used when a transition is interrupted.
        canvas.SaveLayer(rect, null);
        DrawTrackContent(canvas, track, target, rect, now);
        canvas.Restore();
    }
    private static void DrawTrackContent(SKCanvas canvas, Track track, SKImage target, SKRect rect, double now)
    {
        var linear = (float)Math.Clamp((now - track.Start) / track.Duration, 0, 1);
        var t = linear * linear * (3 - 2 * linear);
        if (t == 0) { Stamp(canvas, track.From, rect); return; }
        if (track.Style == TransitionStyle.Flip)
        {
            var squash = Math.Abs(1 - 2 * t);
            if (squash > .001)
                Stamp(canvas, t < .5 ? track.From : target, new(rect.Left, rect.MidY - rect.Height * squash / 2, rect.Right, rect.MidY + rect.Height * squash / 2));
        }
        else
        {
            var oldRect = rect; var newRect = rect;
            if (track.Style == TransitionStyle.Lift) { oldRect.Offset(0, -track.Travel * t); newRect.Offset(0, track.Travel * (1 - t)); }
            Stamp(canvas, track.From, oldRect, 1 - t); Stamp(canvas, target, newRect, t);
        }
    }

    public void Draw(SKCanvas canvas, SKImage target, double now)
    {
        if (!Animating(now)) return;
        using var face = new SKPaint { Color = KeyboardTheme.Flat.FaceStops[0] };
        foreach (var key in current.Keys.Values)
        {
            if (!Enumerable.Range(0, key.Layers.Length).Any(s => tracks.TryGetValue((key.Key.Id, s), out var t) && now < t.Start + t.Duration)) continue;
            var b = key.Key.Bounds; var global = new SKRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height);
            if (canvas.QuickReject(global)) continue;
            canvas.Save(); canvas.Translate(b.X, b.Y);
            var rect = new SKRect(0, 0, b.Width, b.Height);
            var inner = new SKRect(2, 2, b.Width - 2, b.Height - 2);
            canvas.ClipRect(inner); canvas.DrawRect(inner, face);
            for (var slot = 0; slot < key.Layers.Length; slot++)
                if (tracks.TryGetValue((key.Key.Id, slot), out var track) && now < track.Start + track.Duration)
                    DrawTrack(canvas, track, key.Layers[slot].Image, rect, now);
            // Restore unchanged labels last from the production bitmap: no
            // movement or transparent-raster rounding flicker on those glyphs.
            for (var slot = 0; slot < key.Layers.Length; slot++)
            {
                var layer = key.Layers[slot];
                if (layer.Label == null || (tracks.TryGetValue((key.Key.Id, slot), out var track) && now < track.Start + track.Duration)) continue;
                var area = layer.Bounds; const int scale = OverlayGeometry.RasterScale;
                var crop = new SKRect((b.X + area.Left) * scale, (b.Y + area.Top) * scale, (b.X + area.Right) * scale, (b.Y + area.Bottom) * scale);
                canvas.DrawImage(target, crop, area, new SKSamplingOptions(SKFilterMode.Linear));
            }
            canvas.Restore();
        }
    }
    public void Reset() { foreach (var track in tracks.Values) track.From?.Dispose(); tracks.Clear(); }
    public void Dispose() { Reset(); current?.Dispose(); current = null; }
}
