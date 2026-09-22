using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

// Visual tokens only: themes never change key geometry or input behavior.
internal sealed record KeyboardTheme
{
    public SKColor Background { get; init; } = SKColor.Parse("#101820");
    public SKColor Surface { get; init; } = SKColor.Parse("#101820");
    public SKColor Border { get; init; } = SKColor.Parse("#233340");
    public SKColor Text { get; init; } = SKColor.Parse("#DAE2E8");
    public SKColor Secondary { get; init; } = SKColor.Parse("#BECBD5");
    public SKColor Accent { get; init; } = SKColor.Parse("#66C0F4");
    public SKColor HoverOutline => Accent;
    public SKColor Ink { get; init; } = SKColor.Parse("#0E202C");
    public SKColor Notice { get; init; } = SKColor.Parse("#EDC694");
    public SKColor SelectedOutline { get; init; } = InteractionColors.DashboardBlue;
    public SKColor[] FaceStops { get; init; } = [SKColor.Parse("#1C2A36"), SKColor.Parse("#192631"), SKColor.Parse("#17222C")];
    public float[] FacePositions { get; init; } = [0, .16f, 1];
    public SKColor[] EdgeStops { get; init; } = [SKColor.Parse("#354754"), SKColor.Parse("#233340"), SKColor.Parse("#15202A")];
    public float[] EdgePositions { get; init; } = [0, .35f, 1];
    public SKColor[] ArmedStops { get; init; } = [SKColor.Parse("#1D3545"), SKColor.Parse("#192E3C")];
    public bool PanelFrame { get; init; }
    public bool KeyEdges { get; init; } = true;
    public float ShadowBlur { get; init; } = .9f;
    public float ShadowOffset { get; init; } = 1.3f;
    public float SpecialSize { get; init; } = 11.5f;

    public static KeyboardTheme Soft { get; } = new();
    public static KeyboardTheme Flat { get; } = new()
    {
        Background = SKColor.Parse("#0C151E"), Surface = SKColor.Parse("#0D1720"),
        Border = SKColor.Parse("#1C303E"), Text = SKColor.Parse("#F1F6FC"),
        Secondary = SKColor.Parse("#F1F6FC"), Ink = SKColor.Parse("#091923"),
        FaceStops = [SKColor.Parse("#14202A"), SKColor.Parse("#14202A")], FacePositions = [0, 1],
        ArmedStops = [SKColor.Parse("#172D3C"), SKColor.Parse("#172D3C")],
        PanelFrame = true, KeyEdges = false,
        ShadowBlur = 0, ShadowOffset = 0, SpecialSize = 12
    };

    public static KeyboardTheme Resolve(string id) => BoardThemes.Normalize(id) == BoardThemes.SteamFlat ? Flat : Soft;
}
