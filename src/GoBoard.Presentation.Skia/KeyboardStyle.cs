using GoBoard.Core;

namespace GoBoard.Presentation.Skia;

// Shape/shading choices are independent of the color theme. Keep the existing
// persisted steam-soft/steam-flat IDs and user-facing names for compatibility.
internal sealed record KeyboardStyle
{
    public required KeyboardColors Colors { get; init; }
    public float[] FacePositions { get; init; } = [0, .16f, 1];
    public float[] EdgePositions { get; init; } = [0, .35f, 1];
    public bool PanelFrame { get; init; }
    public bool KeyEdges { get; init; } = true;
    public bool SubtleArmedOutline { get; init; } = true;
    public float ShadowBlur { get; init; } = .9f;
    public float ShadowOffset { get; init; } = 1.3f;
    public float SpecialSize { get; init; } = 11.5f;

    public static KeyboardStyle Soft { get; } = new() { Colors = UiColors.Current.SoftKeyboard };
    public static KeyboardStyle Flat { get; } = new()
    {
        Colors = UiColors.Current.FlatKeyboard, FacePositions = [0, 1],
        PanelFrame = true, KeyEdges = false, SubtleArmedOutline = false,
        ShadowBlur = 0, ShadowOffset = 0, SpecialSize = 12
    };

    public static KeyboardStyle Resolve(string id, UiColors colors = null)
    {
        var flat = BoardThemes.Normalize(id) == BoardThemes.SteamFlat;
        var style = flat ? Flat : Soft;
        return colors == null || ReferenceEquals(colors, UiColors.Current)
            ? style : style with { Colors = flat ? colors.FlatKeyboard : colors.SoftKeyboard };
    }
}
