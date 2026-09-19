namespace GoBoard.Core;

internal enum CharacterTransition { None, Crossfade, Lift }

// Shared defaults for new settings and Reset effects; saved values take precedence.
internal sealed record EffectSettings
{
    public bool Afterglow { get; init; } = true;
    public bool Spotlight { get; init; } = true;
    public bool Edges { get; init; }
    public bool Ripples { get; init; } = true;
    public bool PressFlash { get; init; }
    public int EnterMs { get; init; } = 80;
    public int LeaveMs { get; init; } = 160;
    public int Radius { get; init; } = 120;
    public int Strength { get; init; } = 30;
    public int RippleMs { get; init; } = 560;
    public CharacterTransition Transition { get; init; } = CharacterTransition.Lift;
    public int TransitionMs { get; init; } = 160;
    public int Travel { get; init; } = 10;
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
