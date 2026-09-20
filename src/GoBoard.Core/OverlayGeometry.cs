using System.Numerics;

namespace GoBoard.Core;

internal static class OverlayGeometry
{
    // Shared logical units keep rendering and hit testing aligned.
    public const int PanelPadding = 4;
    public const int PanelWidth = 842 + PanelPadding * 2;
    public const int PanelHeight = 274 + PanelPadding * 2;
    public const int RasterScale = 3;
    public const float PanelWidthInMeters = 0.5445f * PanelWidth / 512;
    public const float PanelHeightInMeters = PanelWidthInMeters * PanelHeight / PanelWidth;
    public static int Width(bool numpad) => PanelWidth + (numpad ? KeyboardLayout.NumpadExtraWidth : 0);
    public static float WidthInMeters(bool numpad) => PanelWidthInMeters * Width(numpad) / PanelWidth;

    public const int GrabWidth = 180;
    public const int GrabHeight = 60;
    public const float GrabWidthInMeters = 0.18f;
    public const int ShortcutGrabWidth = GrabWidth / 2;
    public const float ShortcutGrabWidthInMeters = GrabWidthInMeters / 2;
    public const int ResizeSize = 70;
    public const float ResizeSizeInMeters = 0.07f;
    public const float ResizeCornerSpacingInMeters = 0.006f;
    // Offset slightly outward from the corner while keeping the grip close to both edges.
    public static Matrix4x4 ResizeFromScaledPanel(float scale, bool numpad = false) =>
        Matrix4x4.CreateTranslation(WidthInMeters(numpad) * scale / 2 + ResizeCornerSpacingInMeters,
            -(PanelHeightInMeters * scale / 2 + ResizeCornerSpacingInMeters), 0.002f);

    // OpenVR mouse coordinates start at the bottom left. Exclude the upper-left
    // quadrant over the keyboard so the transparent overlay cannot steal key clicks.
    public static readonly IReadOnlyList<KeyBounds> ResizeTargets =
        [new(ResizeSize / 2, 0, ResizeSize / 2, ResizeSize), new(0, 0, ResizeSize / 2, ResizeSize / 2)];
    // Native intersection masks use top-left texture coordinates, unlike mouse events.
    public static readonly IReadOnlyList<KeyBounds> ResizeMaskTargets = ResizeTargets
        .Select(r => r with { Y = ResizeSize - r.Y - r.Height }).ToArray();
    public static bool ResizeHit(float x, float y) => ResizeTargets.Any(r => r.Contains(x, y));

    public static Vector3 ResizePoint(float scale, float x, float y, bool numpad = false) =>
        Vector3.Transform(new Vector3((x / ResizeSize - .5f) * ResizeSizeInMeters,
            (y / ResizeSize - .5f) * ResizeSizeInMeters, 0), ResizeFromScaledPanel(scale, numpad));
    public static readonly Matrix4x4 GrabFromPanel =
        GrabFromScaledPanel(1);

    public static Matrix4x4 GrabFromScaledPanel(float scale) =>
        GrabFromPanelHeight(PanelHeightInMeters * scale);

    public static Matrix4x4 GrabFromPanelHeight(float heightInMeters) =>
        Matrix4x4.CreateTranslation(0, -(heightInMeters / 2 + 0.0375f), 0.002f);
}
