using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal static class BrandColors
{
    // SteamVR Dashboard blue, shared by keyboard themes and surrounding UI.
    public const uint AccentArgb = 0xFF1A9FFF;
    public static readonly SKColor Accent = new(AccentArgb);
}
