using SkiaSharp;

namespace GoBoard.Presentation.Skia;

// Palette values only. UI roles are assigned in UiColors; keyboard geometry and
// shading behavior belong to KeyboardStyle. Neutral stops run from dark to light.
internal sealed record ColorTheme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required SKColor Neutral000 { get; init; }
    public required SKColor Neutral025 { get; init; }
    public required SKColor Neutral050 { get; init; }
    public required SKColor Neutral075 { get; init; }
    public required SKColor Neutral100 { get; init; }
    public required SKColor Neutral125 { get; init; }
    public required SKColor Neutral150 { get; init; }
    public required SKColor Neutral175 { get; init; }
    public required SKColor Neutral200 { get; init; }
    public required SKColor Neutral225 { get; init; }
    public required SKColor Neutral250 { get; init; }
    public required SKColor Neutral275 { get; init; }
    public required SKColor Neutral300 { get; init; }
    public required SKColor Neutral325 { get; init; }
    public required SKColor Neutral350 { get; init; }
    public required SKColor Neutral375 { get; init; }
    public required SKColor Neutral400 { get; init; }
    public required SKColor Neutral425 { get; init; }
    public required SKColor Neutral450 { get; init; }
    public required SKColor Neutral475 { get; init; }
    public required SKColor Neutral500 { get; init; }
    public required SKColor Neutral525 { get; init; }
    public required SKColor Neutral537 { get; init; }
    public required SKColor Neutral550 { get; init; }
    public required SKColor Neutral575 { get; init; }
    public required SKColor Neutral600 { get; init; }
    public required SKColor Neutral625 { get; init; }
    public required SKColor Neutral650 { get; init; }
    public required SKColor Neutral675 { get; init; }
    public required SKColor Neutral700 { get; init; }
    public required SKColor Neutral725 { get; init; }
    public required SKColor Neutral750 { get; init; }
    public required SKColor AccentLight { get; init; }
    public required SKColor AccentStrong { get; init; }
    public required SKColor Amber { get; init; }
    public required SKColor Coral { get; init; }
    public required SKColor Lavender { get; init; }
    public required SKColor Blue { get; init; }
    public required SKColor Yellow { get; init; }
    public required SKColor Tan { get; init; }
    public required SKColor Cream { get; init; }
}

internal static class ColorThemes
{
    public static ColorTheme SteamBlue { get; } = new()
    {
        Id = "steam-blue",
        Name = "Steam Blue",
        Neutral000 = SKColor.Parse("#000000"),
        Neutral025 = SKColor.Parse("#071018"),
        Neutral050 = SKColor.Parse("#0C151E"),
        Neutral075 = SKColor.Parse("#0C171F"),
        Neutral100 = SKColor.Parse("#0D1720"),
        Neutral125 = SKColor.Parse("#091923"),
        Neutral150 = SKColor.Parse("#101820"),
        Neutral175 = SKColor.Parse("#0E202C"),
        Neutral200 = SKColor.Parse("#14202A"),
        Neutral225 = SKColor.Parse("#15202A"),
        Neutral250 = SKColor.Parse("#102330"),
        Neutral275 = SKColor.Parse("#17222C"),
        Neutral300 = SKColor.Parse("#192631"),
        Neutral325 = SKColor.Parse("#1C2A36"),
        Neutral350 = SKColor.Parse("#1B2C39"),
        Neutral375 = SKColor.Parse("#172D3C"),
        Neutral400 = SKColor.Parse("#192E3C"),
        Neutral425 = SKColor.Parse("#1C303E"),
        Neutral450 = SKColor.Parse("#233340"),
        Neutral475 = SKColor.Parse("#1D3545"),
        Neutral500 = SKColor.Parse("#354754"),
        Neutral525 = SKColor.Parse("#344B5C"),
        Neutral537 = SKColor.Parse("#315B73"),
        Neutral550 = SKColor.Parse("#667882"),
        Neutral575 = SKColor.Parse("#707A86"),
        Neutral600 = SKColor.Parse("#81909E"),
        Neutral625 = SKColor.Parse("#8BA5B7"),
        Neutral650 = SKColor.Parse("#BECBD5"),
        Neutral675 = SKColor.Parse("#DAE2E8"),
        Neutral700 = SKColor.Parse("#E1EDF4"),
        Neutral725 = SKColor.Parse("#F1F6FC"),
        Neutral750 = SKColor.Parse("#FFFFFF"),
        AccentLight = SKColor.Parse("#66C0F4"),
        AccentStrong = SKColor.Parse("#1A9FFF"),
        Amber = SKColor.Parse("#EDC694"),
        Coral = SKColor.Parse("#FFB0A0"),
        Lavender = SKColor.Parse("#C0B6ED"),
        Blue = SKColor.Parse("#69B9FF"),
        Yellow = SKColor.Parse("#F5CE62"),
        Tan = SKColor.Parse("#D9A570"),
        Cream = SKColor.Parse("#F1D9B8"),
    };
}
