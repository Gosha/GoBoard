using SkiaSharp;

namespace GoBoard.Presentation.Skia;

// The only palette-to-purpose mapping. Current is fixed for now; future color
// options can resolve another ColorTheme without changing individual renderers.
internal sealed class UiColors(ColorTheme theme)
{
    public static UiColors Current { get; } = new(ColorThemes.SteamBlue);
    public ColorTheme Theme => theme;

    public SKColor WindowBackground => theme.Neutral050;
    public SKColor Text => theme.Neutral725;
    public SKColor TextMuted => theme.Neutral600;
    public SKColor TextDisabled => theme.Neutral550;
    public SKColor TextOnAccent => theme.Neutral125;
    public SKColor TextAccent => theme.AccentLight;
    public SKColor ButtonFill => theme.Neutral350;
    public SKColor ButtonSelectedFill => theme.AccentLight;
    public SKColor ButtonHoverOutline => theme.Neutral725;
    public SKColor Separator => theme.Neutral350;
    public SKColor DesktopHeader => theme.Neutral350;
    public SKColor Error => theme.Coral;
    public SKColor GrabHandleIdle => theme.Neutral575;
    public SKColor GrabHandleHover => theme.Neutral725;
    public SKColor GrabHandleActive => theme.AccentStrong;
    public SKColor ResizeHandleIdle => theme.Neutral575;
    public SKColor ResizeHandleHover => theme.Neutral750;
    public SKColor ResizeHandleActive => theme.AccentStrong;
    public SKColor ShortcutDragHandle => theme.AccentStrong;
    public SKColor PreviewBackground => theme.Neutral025;
    public SKColor Logo => theme.AccentStrong;
    public SKColor LogoBackground => theme.Neutral075;

    public SoundIconColors SoundIcons { get; } = new(theme);

    public KeyboardColors SoftKeyboard { get; } = new()
    {
        KeyLabelAccent = theme.AccentLight,
        KeyActiveFill = theme.AccentLight,
        KeyHoverOutline = theme.AccentLight,
        KeySelectedOutline = theme.AccentStrong,
        ModifierIndicator = theme.AccentLight,
        PointerEffect = theme.AccentLight,
        Notice = theme.Amber,
        Shadow = theme.Neutral000.WithAlpha(77),
        PanelBackground = theme.Neutral150,
        PanelSurface = theme.Neutral150,
        PanelBorder = theme.Neutral450,
        KeyLabel = theme.Neutral675,
        KeyLabelSecondary = theme.Neutral650,
        KeyLabelOnAccent = theme.Neutral175,
        FaceStops = [theme.Neutral325, theme.Neutral300, theme.Neutral275],
        EdgeStops = [theme.Neutral500, theme.Neutral450, theme.Neutral225],
        ArmedStops = [theme.Neutral475, theme.Neutral400],
    };

    public KeyboardColors FlatKeyboard { get; } = new()
    {
        KeyLabelAccent = theme.AccentLight,
        KeyActiveFill = theme.AccentLight,
        KeyHoverOutline = theme.AccentLight,
        KeySelectedOutline = theme.AccentStrong,
        ModifierIndicator = theme.AccentLight,
        PointerEffect = theme.AccentLight,
        Notice = theme.Amber,
        Shadow = theme.Neutral000.WithAlpha(77),
        PanelBackground = theme.Neutral050,
        PanelSurface = theme.Neutral100,
        PanelBorder = theme.Neutral425,
        KeyLabel = theme.Neutral725,
        KeyLabelSecondary = theme.Neutral725,
        KeyLabelOnAccent = theme.Neutral125,
        FaceStops = [theme.Neutral200, theme.Neutral200],
        EdgeStops = [theme.Neutral500, theme.Neutral450, theme.Neutral225],
        ArmedStops = [theme.Neutral375, theme.Neutral375],
    };
}

// Purpose colors for a keyboard style; no geometry, timing, or input state.
internal sealed record KeyboardColors
{
    public required SKColor KeyLabelAccent { get; init; }
    public required SKColor KeyActiveFill { get; init; }
    public required SKColor KeyHoverOutline { get; init; }
    public required SKColor KeySelectedOutline { get; init; }
    public required SKColor ModifierIndicator { get; init; }
    public required SKColor PointerEffect { get; init; }
    public required SKColor Notice { get; init; }
    public required SKColor Shadow { get; init; }
    public required SKColor PanelBackground { get; init; }
    public required SKColor PanelSurface { get; init; }
    public required SKColor PanelBorder { get; init; }
    public required SKColor KeyLabel { get; init; }
    public required SKColor KeyLabelSecondary { get; init; }
    public required SKColor KeyLabelOnAccent { get; init; }
    public required SKColor[] FaceStops { get; init; }
    public required SKColor[] EdgeStops { get; init; }
    public required SKColor[] ArmedStops { get; init; }
}

internal sealed class SoundIconColors(ColorTheme theme)
{
    public SKColor Background => theme.Neutral250;
    public SKColor Ink => theme.Neutral250;
    public SKColor LowThud => theme.Lavender;
    public SKColor SwitchBlue => theme.Blue;
    public SKColor SwitchClear => theme.Neutral700;
    public SKColor SwitchYellow => theme.Yellow;
    public SKColor SwitchBody => theme.Neutral525;
    public SKColor SwitchOutline => theme.Neutral625;
    public SKColor Wood => theme.Tan;
    public SKColor WoodHighlight => theme.Cream;
}
