using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal static class InteractionColors
{
    // Reserve Dashboard blue for grab handles and keyboard hover outlines.
    public const uint DashboardBlueArgb = 0xFF1A9FFF;
    public static readonly SKColor DashboardBlue = new(DashboardBlueArgb);
}
