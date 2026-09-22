using SkiaSharp;

namespace GoBoard.Presentation.Skia;

// Illustration colors identify materials/switch types independently of UI themes.
internal static class SoundIconColors
{
    public static readonly SKColor Background = SKColor.Parse("#102330");
    public static SKColor Ink => Background;
    public static readonly SKColor LowThud = SKColor.Parse("#C0B6ED");
    public static readonly SKColor SwitchBlue = SKColor.Parse("#69B9FF");
    public static readonly SKColor SwitchClear = SKColor.Parse("#E1EDF4");
    public static readonly SKColor SwitchYellow = SKColor.Parse("#F5CE62");
    public static readonly SKColor SwitchBody = SKColor.Parse("#344B5C");
    public static readonly SKColor SwitchOutline = SKColor.Parse("#8BA5B7");
    public static readonly SKColor Wood = SKColor.Parse("#D9A570");
    public static readonly SKColor WoodHighlight = SKColor.Parse("#F1D9B8");
}
