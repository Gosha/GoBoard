namespace GoBoard.Core;

// Stable IDs are persisted, while display names can evolve independently.
internal static class BoardThemes
{
    public const string SteamSoft = "steam-soft";
    public const string SteamFlat = "steam-flat";
    public const string Default = SteamSoft;
    public static string Normalize(string id) => id is SteamSoft or SteamFlat ? id : Default;
    public static string Name(string id) => Normalize(id) == SteamFlat ? "Steam Flat" : "Steam Soft";
}
