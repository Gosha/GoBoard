namespace GoBoard.Core;

internal enum SettingsPage { General, Effects }
internal enum SettingsAction
{
    Smaller, Larger, ToggleSound, Wood, Thud, Quieter, Louder, Defaults, Geometry, SteamSoft, SteamFlat,
    GeneralTab, EffectsTab, Afterglow, Spotlight, Edges, Ripples, PressFlash,
    TransitionNone, Crossfade, Lift, TransitionFaster, TransitionSlower, TravelLess, TravelMore,
    EnterFaster, EnterSlower, LeaveFaster, LeaveSlower, RadiusLess, RadiusMore,
    StrengthLess, StrengthMore, RippleFaster, RippleSlower, ResetEffects,
    CherryBlue, GateronYellow, ResetPosition
}
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
    public const int Width = 900, Height = 850;
    public static readonly SettingsControl[] All =
    [
        new(SettingsAction.Smaller, "−", new(588, 158, 100, 64)),
        new(SettingsAction.Larger, "+", new(704, 158, 100, 64)),
        new(SettingsAction.ToggleSound, "", new(588, 260, 216, 64)),
        new(SettingsAction.Wood, KeySounds.Name(KeySound.CushionedWood), new(64, 358, 173, 88)),
        new(SettingsAction.Thud, KeySounds.Name(KeySound.SoftLowThud), new(253, 358, 173, 88)),
        new(SettingsAction.CherryBlue, KeySounds.Name(KeySound.CherryMxBlue), new(442, 358, 173, 88)),
        new(SettingsAction.GateronYellow, KeySounds.Name(KeySound.GateronYellowPairs), new(631, 358, 173, 88)),
        new(SettingsAction.Quieter, "−", new(588, 474, 100, 64)),
        new(SettingsAction.Louder, "+", new(704, 474, 100, 64)),
        new(SettingsAction.Geometry, "", new(588, 550, 216, 64)),
        new(SettingsAction.SteamSoft, BoardThemes.Name(BoardThemes.SteamSoft), new(64, 668, 340, 64)),
        new(SettingsAction.SteamFlat, BoardThemes.Name(BoardThemes.SteamFlat), new(420, 668, 384, 64)),
        new(SettingsAction.ResetPosition, "Reset position", new(356, 770, 216, 48)),
        new(SettingsAction.Defaults, "Reset to defaults", new(588, 770, 216, 48))
    ];

    public static readonly SettingsControl[] Tabs =
    [new(SettingsAction.GeneralTab, "General", new(548, 32, 120, 54)),
     new(SettingsAction.EffectsTab, "Effects", new(684, 32, 120, 54))];
    public static readonly (string Label, SettingsAction Less, SettingsAction More)[] Parameters =
    [
        ("Character duration", SettingsAction.TransitionFaster, SettingsAction.TransitionSlower),
        ("Lift distance", SettingsAction.TravelLess, SettingsAction.TravelMore),
        ("Hover fade in", SettingsAction.EnterFaster, SettingsAction.EnterSlower),
        ("Hover fade out", SettingsAction.LeaveFaster, SettingsAction.LeaveSlower),
        ("Light radius", SettingsAction.RadiusLess, SettingsAction.RadiusMore),
        ("Effect strength", SettingsAction.StrengthLess, SettingsAction.StrengthMore),
        ("Click duration", SettingsAction.RippleFaster, SettingsAction.RippleSlower)
    ];
    public static readonly SettingsControl[] Effects = BuildEffects();
    private static SettingsControl[] BuildEffects()
    {
        var controls = new List<SettingsControl>
        {
            new(SettingsAction.Afterglow, "Afterglow", new(64, 156, 236, 52)),
            new(SettingsAction.Spotlight, "Spotlight", new(316, 156, 236, 52)),
            new(SettingsAction.Edges, "Proximity edges", new(568, 156, 236, 52)),
            new(SettingsAction.Ripples, "Click ripple", new(64, 220, 236, 52)),
            new(SettingsAction.PressFlash, "Press flash / tint", new(316, 220, 236, 52)),
            new(SettingsAction.TransitionNone, "None", new(64, 322, 236, 52)),
            new(SettingsAction.Crossfade, "Crossfade", new(316, 322, 236, 52)),
            new(SettingsAction.Lift, "Lift", new(568, 322, 236, 52)),
            new(SettingsAction.ResetEffects, "Reset effects", new(588, 770, 216, 48))
        };
        for (var i = 0; i < Parameters.Length; i++)
        {
            var x = 64 + i % 2 * 392; var y = 414 + i / 2 * 84;
            controls.Add(new(Parameters[i].Less, "−", new(x, y, 54, 44)));
            controls.Add(new(Parameters[i].More, "+", new(x + 294, y, 54, 44)));
        }
        return controls.ToArray();
    }
    public static IEnumerable<SettingsControl> ForPage(SettingsPage page) => Tabs.Concat(page == SettingsPage.Effects ? Effects : All);
    public static SettingsAction? Hit(float x, float y, SettingsPage page = SettingsPage.General) => ForPage(page).FirstOrDefault(c => c.Bounds.Contains(x, y))?.Action;
    public static string ParameterValue(int index, EffectSettings e) => index switch
    {
        0 => $"{e.TransitionMs} ms", 1 => $"{e.Travel} px", 2 => e.EnterMs == 0 ? "Instant" : $"{e.EnterMs} ms",
        3 => $"{e.LeaveMs} ms", 4 => $"{e.Radius} px", 5 => $"{e.Strength}%", _ => $"{e.RippleMs} ms"
    };
    public static bool Enabled(SettingsAction action, BoardSettings s) => action switch
    {
        SettingsAction.Smaller => s.SizePercent > 50,
        SettingsAction.Larger => s.SizePercent < 150,
        SettingsAction.Quieter => s.SoundEnabled && s.VolumePercent > 0,
        SettingsAction.Louder => s.SoundEnabled && s.VolumePercent < 100,
        >= SettingsAction.TransitionFaster and <= SettingsAction.RippleSlower => Apply(action, s) != s,
        _ => true
    };
    public static KeySound? SoundFor(SettingsAction action) => action switch
    {
        SettingsAction.Wood => KeySound.CushionedWood,
        SettingsAction.Thud => KeySound.SoftLowThud,
        SettingsAction.CherryBlue => KeySound.CherryMxBlue,
        SettingsAction.GateronYellow => KeySound.GateronYellowPairs,
        _ => null
    };
    public static bool AuditionsSound(SettingsAction action) => SoundFor(action).HasValue || action is
        SettingsAction.ToggleSound or SettingsAction.Quieter or SettingsAction.Louder;
    public static BoardSettings Apply(SettingsAction action, BoardSettings s) => (action switch
    {
        SettingsAction.Smaller => s with { SizePercent = s.SizePercent - 5 },
        SettingsAction.Larger => s with { SizePercent = s.SizePercent + 5 },
        SettingsAction.ToggleSound => s with { SoundEnabled = !s.SoundEnabled },
        _ when SoundFor(action) is { } sound => s with { Sound = sound },
        SettingsAction.Quieter => s with { VolumePercent = s.VolumePercent - 10 },
        SettingsAction.Louder => s with { VolumePercent = s.VolumePercent + 10 },
        SettingsAction.SteamSoft => s with { Theme = BoardThemes.SteamSoft },
        SettingsAction.SteamFlat => s with { Theme = BoardThemes.SteamFlat },
        SettingsAction.ResetPosition => s with { PositionResetId = Guid.NewGuid() },
        SettingsAction.Defaults => new BoardSettings { PositionResetId = s.PositionResetId },
        SettingsAction.Geometry => s with { Geometry = (KeyboardGeometry)(((int)s.Geometry + 1) % 3) },
        SettingsAction.ResetEffects => s with { Effects = new() },
        SettingsAction.Afterglow => s with { Effects = s.Effects with { Afterglow = !s.Effects.Afterglow } },
        SettingsAction.Spotlight => s with { Effects = s.Effects with { Spotlight = !s.Effects.Spotlight } },
        SettingsAction.Edges => s with { Effects = s.Effects with { Edges = !s.Effects.Edges } },
        SettingsAction.Ripples => s with { Effects = s.Effects with { Ripples = !s.Effects.Ripples } },
        SettingsAction.PressFlash => s with { Effects = s.Effects with { PressFlash = !s.Effects.PressFlash } },
        SettingsAction.TransitionNone => s with { Effects = s.Effects with { Transition = CharacterTransition.None } },
        SettingsAction.Crossfade => s with { Effects = s.Effects with { Transition = CharacterTransition.Crossfade } },
        SettingsAction.Lift => s with { Effects = s.Effects with { Transition = CharacterTransition.Lift } },
        SettingsAction.TransitionFaster => s with { Effects = s.Effects with { TransitionMs = s.Effects.TransitionMs - 20 } },
        SettingsAction.TransitionSlower => s with { Effects = s.Effects with { TransitionMs = s.Effects.TransitionMs + 20 } },
        SettingsAction.TravelLess => s with { Effects = s.Effects with { Travel = s.Effects.Travel - 1 } },
        SettingsAction.TravelMore => s with { Effects = s.Effects with { Travel = s.Effects.Travel + 1 } },
        SettingsAction.EnterFaster => s with { Effects = s.Effects with { EnterMs = s.Effects.EnterMs - 20 } },
        SettingsAction.EnterSlower => s with { Effects = s.Effects with { EnterMs = s.Effects.EnterMs + 20 } },
        SettingsAction.LeaveFaster => s with { Effects = s.Effects with { LeaveMs = s.Effects.LeaveMs - 20 } },
        SettingsAction.LeaveSlower => s with { Effects = s.Effects with { LeaveMs = s.Effects.LeaveMs + 20 } },
        SettingsAction.RadiusLess => s with { Effects = s.Effects with { Radius = s.Effects.Radius - 10 } },
        SettingsAction.RadiusMore => s with { Effects = s.Effects with { Radius = s.Effects.Radius + 10 } },
        SettingsAction.StrengthLess => s with { Effects = s.Effects with { Strength = s.Effects.Strength - 5 } },
        SettingsAction.StrengthMore => s with { Effects = s.Effects with { Strength = s.Effects.Strength + 5 } },
        SettingsAction.RippleFaster => s with { Effects = s.Effects with { RippleMs = s.Effects.RippleMs - 20 } },
        SettingsAction.RippleSlower => s with { Effects = s.Effects with { RippleMs = s.Effects.RippleMs + 20 } },
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
    public SettingsPage Page { get; private set; }
    public int Revision { get; private set; }
    public bool Hovered(SettingsAction action) => pointers.Values.Any(p => p.Hover == action);
    public void Reset() { pointers.Clear(); Revision++; }
    public SettingsAction? Process(uint device, float x, float y, double time, double now, bool down = false, bool up = false, bool leave = false)
    {
        if (!double.IsFinite(time) || time > now || now - time > .5) return null;
        if (!pointers.TryGetValue(device, out var p)) pointers[device] = p = new();
        if (time < p.Last) return null;
        p.Last = time;
        var hit = leave ? null : SettingsControls.Hit(x, y, Page);
        if (p.Hover != hit) Revision++;
        p.Hover = hit;
        if (leave || (p.Capture.HasValue && p.Capture != hit)) p.Capture = null;
        if (down) p.Capture = now - time <= .2 ? hit : null;
        if (!up) return null;
        var clicked = p.Capture == hit ? hit : null;
        p.Capture = null;
        if (clicked is SettingsAction.GeneralTab or SettingsAction.EffectsTab)
        {
            Page = clicked == SettingsAction.GeneralTab ? SettingsPage.General : SettingsPage.Effects;
            Reset(); // Both hands lose captures from the old page.
            return null;
        }
        return clicked;
    }
}
