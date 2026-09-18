namespace GoBoard.Core;

internal enum SettingsAction { Smaller, Larger, ToggleSound, Wood, Thud, Quieter, Louder, Defaults }
internal sealed record SettingsControl(SettingsAction Action, string Label, KeyBounds Bounds);

// Preserve the dashboard aspect ratio in a resizable desktop window. Painting
// and hit testing use this same viewport, including any letterboxed margins.
internal readonly record struct SettingsViewport(float X, float Y, float Scale)
{
    public float Width => SettingsControls.Width * Scale;
    public float Height => SettingsControls.Height * Scale;
    public static SettingsViewport Fit(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return default;
        var scale = Math.Min(width / SettingsControls.Width, height / SettingsControls.Height);
        return new((width - SettingsControls.Width * scale) / 2, (height - SettingsControls.Height * scale) / 2, scale);
    }
    public (float X, float Y) ToPanel(float x, float y) => Scale > 0 &&
        x >= X && x < X + Width && y >= Y && y < Y + Height
        ? ((x - X) / Scale, (y - Y) / Scale) : (float.NaN, float.NaN);
}

internal static class SettingsControls
{
    public const int Width = 900, Height = 650;
    public static readonly SettingsControl[] All =
    [
        new(SettingsAction.Smaller, "−", new(588, 158, 100, 64)),
        new(SettingsAction.Larger, "+", new(704, 158, 100, 64)),
        new(SettingsAction.ToggleSound, "", new(588, 260, 216, 64)),
        new(SettingsAction.Wood, "Cushioned wood", new(64, 368, 340, 68)),
        new(SettingsAction.Thud, "Soft low thud", new(420, 368, 384, 68)),
        new(SettingsAction.Quieter, "−", new(588, 474, 100, 64)),
        new(SettingsAction.Louder, "+", new(704, 474, 100, 64)),
        new(SettingsAction.Defaults, "Reset to defaults", new(588, 570, 216, 48))
    ];

    public static SettingsAction? Hit(float x, float y) => All.FirstOrDefault(c => c.Bounds.Contains(x, y))?.Action;
    public static bool Enabled(SettingsAction action, BoardSettings s) => action switch
    {
        SettingsAction.Smaller => s.SizePercent > 50,
        SettingsAction.Larger => s.SizePercent < 150,
        SettingsAction.Quieter => s.SoundEnabled && s.VolumePercent > 0,
        SettingsAction.Louder => s.SoundEnabled && s.VolumePercent < 100,
        _ => true
    };
    public static BoardSettings Apply(SettingsAction action, BoardSettings s) => (action switch
    {
        SettingsAction.Smaller => s with { SizePercent = s.SizePercent - 5 },
        SettingsAction.Larger => s with { SizePercent = s.SizePercent + 5 },
        SettingsAction.ToggleSound => s with { SoundEnabled = !s.SoundEnabled },
        SettingsAction.Wood => s with { Sound = KeySound.CushionedWood },
        SettingsAction.Thud => s with { Sound = KeySound.SoftLowThud },
        SettingsAction.Quieter => s with { VolumePercent = s.VolumePercent - 10 },
        SettingsAction.Louder => s with { VolumePercent = s.VolumePercent + 10 },
        SettingsAction.Defaults => new BoardSettings(),
        _ => s
    }).Normalize();
}

// Release-to-activate; leaving a button cancels its capture. Per-controller
// timestamps reject stale edges without letting one hand release the other.
internal sealed class SettingsPointerState
{
    private sealed class Pointer
    {
        public double Last = double.NegativeInfinity;
        public SettingsAction? Hover, Capture;
    }
    private readonly Dictionary<uint, Pointer> pointers = new();
    public bool Hovered(SettingsAction action) => pointers.Values.Any(p => p.Hover == action);
    public void Reset() => pointers.Clear();
    public SettingsAction? Process(uint device, float x, float y, double time, double now, bool down = false, bool up = false, bool leave = false)
    {
        if (!double.IsFinite(time) || time > now || now - time > .5) return null;
        if (!pointers.TryGetValue(device, out var p)) pointers[device] = p = new();
        if (time < p.Last) return null;
        p.Last = time;
        var hit = leave ? null : SettingsControls.Hit(x, y);
        p.Hover = hit;
        if (leave || (p.Capture.HasValue && p.Capture != hit)) p.Capture = null;
        if (down) p.Capture = now - time <= .2 ? hit : null;
        if (!up) return null;
        var clicked = p.Capture == hit ? hit : null;
        p.Capture = null;
        return clicked;
    }
}
