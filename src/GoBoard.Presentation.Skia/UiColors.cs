using SkiaSharp;

namespace GoBoard.Presentation.Skia;

// Palette-to-purpose mapping. Blend weights describe contrast and elevation,
// shared by every color theme, rather than reproducing individual legacy shades.
internal sealed class UiColors(ColorTheme theme)
{
    public static UiColors Current { get; } = new(ColorThemes.SteamBlue);
    public ColorTheme Theme => theme;

    public SKColor WindowBackground => theme.Background;
    public SKColor Text => theme.Text;
    public SKColor TextMuted => Mix(theme.Surface, theme.Text, .48f);
    public SKColor TextDisabled => Mix(theme.Surface, theme.Text, .35f);
    public SKColor TextOnAccent => theme.TextOnAccent;
    public SKColor TextAccent => theme.AccentLight;
    public SKColor ButtonFill => theme.Surface;
    public SKColor ButtonSelectedFill => theme.AccentLight;
    public SKColor ButtonHoverOutline => theme.Text;
    public SKColor Separator => theme.Surface;
    public SKColor DesktopHeader => theme.Surface;
    public SKColor Error => theme.Error;
    public SKColor GrabHandleIdle => Mix(theme.Surface, theme.Text, .4f);
    public SKColor GrabHandleHover => theme.Text;
    public SKColor GrabHandleActive => theme.AccentStrong;
    public SKColor ResizeHandleIdle => GrabHandleIdle;
    public SKColor ResizeHandleHover => theme.Text;
    public SKColor ResizeHandleActive => theme.AccentStrong;
    public SKColor ShortcutDragHandle => theme.AccentStrong;
    public SKColor PreviewBackground => theme.Background;
    public SKColor Logo => theme.AccentStrong;
    public SKColor LogoBackground => theme.Background;

    public KeyboardColors SoftKeyboard { get; } = Keyboard(theme, soft: true);
    public KeyboardColors FlatKeyboard { get; } = Keyboard(theme, soft: false);

    private static KeyboardColors Keyboard(ColorTheme theme, bool soft)
    {
        var faceTop = Mix(theme.Surface, theme.Background, .1f);
        var faceMiddle = Mix(theme.Surface, theme.Background, .2f);
        var faceBottom = Mix(theme.Surface, theme.Background, .35f);
        var flatFace = Mix(theme.Background, theme.Surface, .5f);
        var edge = Mix(theme.Surface, theme.Text, .05f);
        var armedTop = Mix(theme.Surface, theme.AccentLight, .07f);
        var armedBottom = Mix(faceMiddle, theme.AccentLight, .05f);
        return new()
        {
            KeyLabelAccent = theme.AccentLight,
            KeyActiveFill = theme.AccentLight,
            KeyHoverOutline = theme.AccentLight,
            KeySelectedOutline = theme.AccentStrong,
            KeyArmedOutline = soft ? Mix(theme.Surface, theme.AccentLight, .3f) : theme.AccentStrong,
            ModifierIndicator = theme.AccentLight,
            PointerEffect = theme.AccentLight,
            Notice = theme.Warning,
            Shadow = SKColors.Black.WithAlpha(77),
            PanelBackground = soft ? Mix(theme.Background, theme.Surface, .15f) : theme.Background,
            PanelSurface = soft ? Mix(theme.Background, theme.Surface, .15f) : theme.Background,
            PanelBorder = edge,
            KeyLabel = soft ? Mix(theme.Text, theme.Background, .1f) : theme.Text,
            KeyLabelSecondary = soft ? Mix(theme.Text, theme.Background, .22f) : theme.Text,
            KeyLabelOnAccent = theme.TextOnAccent,
            FaceStops = soft ? [faceTop, faceMiddle, faceBottom] : [flatFace, flatFace],
            EdgeStops = [Mix(theme.Surface, theme.Text, .12f), edge, faceBottom],
            ArmedStops = soft ? [armedTop, armedBottom] : [armedBottom, armedBottom],
        };
    }

    // Interpolate display RGB consistently; all current theme bases are opaque.
    private static SKColor Mix(SKColor from, SKColor to, float amount)
    {
        byte Channel(byte a, byte b) => (byte)MathF.Round(a + (b - a) * amount);
        return new(Channel(from.Red, to.Red), Channel(from.Green, to.Green),
            Channel(from.Blue, to.Blue), Channel(from.Alpha, to.Alpha));
    }
}

// Purpose colors for a keyboard style; no geometry, timing, or input state.
internal sealed record KeyboardColors
{
    public required SKColor KeyLabelAccent { get; init; }
    public required SKColor KeyActiveFill { get; init; }
    public required SKColor KeyHoverOutline { get; init; }
    public required SKColor KeySelectedOutline { get; init; }
    public required SKColor KeyArmedOutline { get; init; }
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
