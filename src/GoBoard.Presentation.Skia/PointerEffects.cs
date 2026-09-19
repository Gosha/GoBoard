using GoBoard.Core;
using SkiaSharp;
namespace GoBoard.Presentation.Skia;

// Time is supplied by the host, so re-entry, zero duration and expiry can be
// tested without timers, sleeping or a window. Smooth fades never delay picking.
internal sealed class Envelope
{
    private float from, to;
    private double start, duration;
    public float Value(double now)
    {
        var t = duration <= 0 ? 1 : Math.Clamp((now - start) / duration, 0, 1);
        t = 1 - Math.Pow(1 - t, 3);
        return from + (to - from) * (float)t;
    }
    public void Target(float value, double now, int milliseconds)
    {
        from = Value(now); to = value; start = now; duration = milliseconds / 1000.0;
    }
    public bool Moving(double now) => from != to && now < start + duration;
}

internal sealed record Pulse(KeyboardKey Key, SKPoint Origin, double Start, double Duration);

internal sealed class PointerEffects
{
    public readonly Dictionary<string, Envelope> Trails = new();
    public readonly List<Pulse> Pulses = new();
    public readonly Envelope Presence = new();
    public KeyboardKey Hover { get; private set; }
    public KeyboardKey Pressed { get; private set; }
    public SKPoint Pointer { get; private set; }
    private bool inside;

    public void Move(SKPoint point, IReadOnlyList<KeyboardKey> keys, EffectSettings options, double now)
    {
        Pointer = point;
        if (!inside) { Presence.Target(1, now, options.EnterMs); inside = true; }
        SetHover(KeyboardLayout.Hit(point.X, point.Y, keys), options, now);
    }
    public void Leave(EffectSettings options, double now)
    {
        if (inside) Presence.Target(0, now, options.LeaveMs);
        inside = false; Pressed = null;
        SetHover(null, options, now);
    }
    private void SetHover(KeyboardKey key, EffectSettings options, double now)
    {
        if (key == Hover) return;
        if (Hover != null) Trails[Hover.Id].Target(0, now, options.Afterglow ? options.LeaveMs : 0);
        Hover = key;
        if (key == null) return;
        if (!Trails.TryGetValue(key.Id, out var fade)) Trails[key.Id] = fade = new();
        fade.Target(1, now, options.EnterMs);
    }
    public void Down(double now, EffectSettings options)
    {
        if (Hover == null) return;
        Pressed = Hover;
        if (!options.Ripples && !options.PressFlash) return;
        Pulses.Add(new(Hover, Pointer, now, options.RippleMs / 1000.0));
        if (Pulses.Count > 32) Pulses.RemoveAt(0);
    }
    public void Up() => Pressed = null;
    public void SetPressed(KeyboardKey key) => Pressed = key;
    public void Prune(double now) => Pulses.RemoveAll(p => now >= p.Start + p.Duration);
    public bool Animating(double now, EffectSettings options = null) =>
        ((options == null || options.Spotlight || options.Edges) && Presence.Moving(now)) ||
        Trails.Values.Any(f => f.Moving(now)) ||
        Pulses.Any(p => now < p.Start + p.Duration && (options == null || options.Ripples || (options.PressFlash && now < p.Start + p.Duration * .4)));
    public void Reset()
    {
        Trails.Clear(); Pulses.Clear(); Hover = Pressed = null; inside = false;
        Presence.Target(0, 0, 0);
    }
}
