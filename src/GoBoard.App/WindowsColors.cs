using SkiaSharp;

namespace GoBoard.App;

// Convert shared presentation colors at the Windows Forms boundary.
internal static class WindowsColors
{
    public static Color ToDrawingColor(this SKColor color) => Color.FromArgb(unchecked((int)(uint)color));
}
