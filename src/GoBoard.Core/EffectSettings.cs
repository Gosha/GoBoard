namespace GoBoard.Core;

internal enum CharacterTransition { None, Crossfade, Lift }

// Opt in to animation; existing installations retain their immediate feedback.
internal sealed record EffectSettings
{
    public bool Afterglow { get; init; }
    public bool Spotlight { get; init; }
    public bool Edges { get; init; }
    public bool Ripples { get; init; }
    public bool PressFlash { get; init; }
    public int EnterMs { get; init; } = 0;
    public int LeaveMs { get; init; } = 220;
    public int Radius { get; init; } = 80;
    public int Strength { get; init; } = 45;
    public int RippleMs { get; init; } = 380;
    public CharacterTransition Transition { get; init; }
    public int TransitionMs { get; init; } = 300;
    public int Travel { get; init; } = 8;
    public bool PointerEnabled => Afterglow || Spotlight || Edges || Ripples || PressFlash;
    public EffectSettings Normalize() => this with
    {
        EnterMs = Math.Clamp(EnterMs, 0, 500), LeaveMs = Math.Clamp(LeaveMs, 0, 1000),
        Radius = Math.Clamp(Radius, 20, 200), Strength = Math.Clamp(Strength, 0, 100),
        RippleMs = Math.Clamp(RippleMs, 0, 1000), TransitionMs = Math.Clamp(TransitionMs, 0, 1000),
        Travel = Math.Clamp(Travel, 0, 20),
        Transition = Enum.IsDefined(Transition) ? Transition : CharacterTransition.None
    };
}
