using System.Numerics;

namespace GoBoard.Core;

internal static class OverlayGeometry
{
    // Shared logical units keep rendering and hit testing aligned.
    public const int PanelPadding = 4;
    public const int PanelWidth = 850 + PanelPadding * 2;
    public const int PanelHeight = 294 + PanelPadding * 2;
    public const int RasterScale = 3;
    public const float PanelWidthInMeters = 0.5445f * PanelWidth / 512;
    public const float PanelHeightInMeters = PanelWidthInMeters * PanelHeight / PanelWidth;

    public const int GrabWidth = 180;
    public const int GrabHeight = 60;
    public const float GrabWidthInMeters = 0.18f;
    public static readonly Matrix4x4 GrabFromPanel =
        Matrix4x4.CreateTranslation(0, -(PanelHeightInMeters / 2 + 0.0375f), 0.002f);
}
