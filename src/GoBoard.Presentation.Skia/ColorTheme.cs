using SkiaSharp;

namespace GoBoard.Presentation.Skia;

// Only base colors are chosen per theme. UiColors derives shades and maps roles.
internal sealed record ColorTheme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required SKColor Background { get; init; }
    public required SKColor Surface { get; init; }
    public required SKColor Text { get; init; }
    public required SKColor TextOnAccent { get; init; }
    public required SKColor AccentLight { get; init; }
    public required SKColor AccentStrong { get; init; }
    public required SKColor Warning { get; init; }
    public required SKColor Error { get; init; }
}

internal static class ColorThemes
{
    public static ColorTheme SteamBlue { get; } = new()
    {
        Id = "steam-blue",
        Name = "Steam Blue",
        Background = SKColor.Parse("#0C151E"),
        Surface = SKColor.Parse("#1B2C39"),
        Text = SKColor.Parse("#F1F6FC"),
        TextOnAccent = SKColor.Parse("#091923"),
        AccentLight = SKColor.Parse("#66C0F4"),
        AccentStrong = SKColor.Parse("#1A9FFF"),
        Warning = SKColor.Parse("#EDC694"),
        Error = SKColor.Parse("#FFB0A0"),
    };
}
