using GoBoard.Core;
using GoBoard.Platform.Windows;
using SkiaSharp;

namespace GoBoard.EffectsLab;

internal sealed class EffectOptions
{
    public bool Afterglow { get; set; } = true;
    public bool Spotlight { get; set; } = true;
    public bool Edges { get; set; } = true;
    public bool Ripples { get; set; } = true;
    public bool PressFlash { get; set; } = true;
    public int EnterMs { get; set; } = 0;
    public int LeaveMs { get; set; } = 220;
    public int Radius { get; set; } = 80;
    public int Strength { get; set; } = 45;
    public int RippleMs { get; set; } = 380;
    public TransitionStyle Transition { get; set; } = TransitionStyle.Lift;
    public int TransitionMs { get; set; } = 300;
    public int StaggerMs { get; set; } = 130;
    public int Travel { get; set; } = 8;
    public static readonly string[] Presets = ["Combined", "Soft afterglow", "Pointer spotlight", "Proximity edges", "Click feedback", "Baseline"];
    public void Preset(string name)
    {
        Afterglow = name is "Combined" or "Soft afterglow";
        Spotlight = name is "Combined" or "Pointer spotlight";
        Edges = name is "Combined" or "Proximity edges";
        Ripples = PressFlash = name is "Combined" or "Click feedback";
    }
}

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

internal sealed class Effects
{
    public readonly Dictionary<string, Envelope> Trails = new();
    public readonly List<Pulse> Pulses = new();
    public readonly Envelope Presence = new();
    public KeyboardKey Hover { get; private set; }
    public KeyboardKey Pressed { get; private set; }
    public SKPoint Pointer { get; private set; }
    private bool inside;

    public void Move(SKPoint point, IReadOnlyList<KeyboardKey> keys, EffectOptions options, double now)
    {
        Pointer = point;
        if (!inside) { Presence.Target(1, now, options.EnterMs); inside = true; }
        SetHover(KeyboardLayout.Hit(point.X, point.Y, keys), options, now);
    }
    public void Leave(EffectOptions options, double now)
    {
        if (inside) Presence.Target(0, now, options.LeaveMs);
        inside = false; Pressed = null;
        SetHover(null, options, now);
    }
    private void SetHover(KeyboardKey key, EffectOptions options, double now)
    {
        if (key == Hover) return;
        if (Hover != null) Trails[Hover.Id].Target(0, now, options.Afterglow ? options.LeaveMs : 0);
        Hover = key;
        if (key == null) return;
        if (!Trails.TryGetValue(key.Id, out var fade)) Trails[key.Id] = fade = new();
        fade.Target(1, now, options.EnterMs);
    }
    public void Down(double now, EffectOptions options)
    {
        if (Hover == null) return;
        Pressed = Hover;
        if (!options.Ripples && !options.PressFlash) return;
        Pulses.Add(new(Hover, Pointer, now, options.RippleMs / 1000.0));
        if (Pulses.Count > 32) Pulses.RemoveAt(0);
    }
    public void Up() => Pressed = null;
    public void Prune(double now) => Pulses.RemoveAll(p => now >= p.Start + p.Duration);
    public bool Animating(double now, EffectOptions options = null) =>
        ((options == null || options.Spotlight || options.Edges) && Presence.Moving(now)) ||
        Trails.Values.Any(f => f.Moving(now)) ||
        Pulses.Any(p => now < p.Start + p.Duration && (options == null || options.Ripples || (options.PressFlash && now < p.Start + p.Duration * .4)));
    public void Reset()
    {
        Trails.Clear(); Pulses.Clear(); Hover = Pressed = null; inside = false;
        Presence.Target(0, 0, 0);
    }
}

internal sealed class LocalSession : IKeySink
{
    public KeyboardState Keyboard { get; }
    public bool Caps { get; private set; }
    public bool Scroll { get; private set; }
    public string LastChord { get; private set; } = "Click a key to preview its chord here";
    public List<(ushort Scan, ushort[] Chord)> Strokes { get; } = new();
    public SKPoint LastPosition { get; private set; }
    private double lastClick = -1;
    public LocalSession() { Keyboard = new(this); SetLayout(true, 0); }
    public void SetLayout(bool swedish, double now)
    {
        // Use the application's table reader instead of the legacy fixture's
        // em-dash placeholders. Reading a KLID does not activate the layout.
        var handle = (nint)(swedish ? WindowsLayout.SwedishHandle : WindowsLayout.UsHandle);
        Keyboard.SetLayout(WindowsLayoutProvider.FromKlid(handle, swedish ? "0000041d" : "00000409"), now);
        Caps = Scroll = false;
    }
    public void Click(SKPoint position, double now)
    {
        // Production input rejects stale timestamps. Preserve ordering for rapid
        // synthetic demo clicks as well as normal desktop events.
        now = Math.Max(now, lastClick + .01); lastClick = now;
        var key = KeyboardLayout.Hit(position.X, position.Y, Keyboard.Layout.Keys);
        Keyboard.Enter(0, 7, now);
        var accepted = Keyboard.Press(0, 7, position.X, OverlayGeometry.PanelHeight - position.Y, now, now);
        Keyboard.Up(0, 7, now); Keyboard.Leave(0, 7, now);
        if (!accepted || key == null) return;
        LastPosition = position;
        if (key.Id == "Caps") Caps = !Caps;
        if (key.Id == "ScrollLock") Scroll = !Scroll;
        if (key.IsModifier) LastChord = $"{key.Label}: {Keyboard.Mode(key.Scan)}";
    }
    public void Reset(double now) { Keyboard.Cancel(now); Caps = Scroll = false; LastChord = "Modifiers cleared"; }
    public void Down(ushort scan) { }
    public void Up(ushort scan) { }
    public void Stroke(ushort scan, ushort[] chord)
    {
        Strokes.Add((scan, chord.ToArray()));
        if (Strokes.Count > 128) Strokes.RemoveAt(0);
        string Name(ushort s) => Keyboard.Layout.Keys.FirstOrDefault(k => k.Scan == s)?.Id ?? $"0x{s:x}";
        LastChord = string.Join(" + ", chord.Append(scan).Select(Name));
    }
}
