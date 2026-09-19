namespace GoBoard.Core;

// The desktop has its own chrome, but shares the exact keyboard hit geometry.
internal readonly record struct DesktopGeometry(float Width, float Height, float HeaderHeight, int KeyboardWidth = OverlayGeometry.PanelWidth)
{
    public static DesktopGeometry Create(float scale, float dpiScale, float availableWidth, float availableHeight, int keyboardWidth = OverlayGeometry.PanelWidth)
    {
        var header = 42 * dpiScale;
        var factor = Math.Min(scale * dpiScale, Math.Min(availableWidth / keyboardWidth,
            (availableHeight - header) / OverlayGeometry.PanelHeight));
        factor = Math.Max(.1f, factor);
        return new(keyboardWidth * factor, OverlayGeometry.PanelHeight * factor + header, header, keyboardWidth);
    }

    public (float X, float Y) ToKeyboard(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || x >= Width || y < HeaderHeight || y >= Height)
            return (float.NaN, float.NaN);
        return (x * KeyboardWidth / Width,
            OverlayGeometry.PanelHeight - (y - HeaderHeight) * OverlayGeometry.PanelHeight / (Height - HeaderHeight));
    }
}
