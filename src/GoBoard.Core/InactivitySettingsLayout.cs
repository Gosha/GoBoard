namespace GoBoard.Core;

// Logical panel coordinates shared by drawing and hit testing. The four mode
// choices fill the content width; each parameter has a label and a -/value/+ row.
internal static class InactivitySettingsLayout
{
    public const float ContentLeft = 64, ContentRight = 804;
    private const int ModeCount = 4;
    private const float ModeTop = 164, ModeHeight = 64, ModeGap = 16;
    private const float ModeWidth = (ContentRight - ContentLeft - ModeGap * (ModeCount - 1)) / ModeCount;
    private const float ParameterTop = 344, ParameterRowSpacing = 80;
    private const float StepperWidth = 64, StepperHeight = 52, ValueWidth = 160;
    private const float IncreaseLeft = ContentRight - StepperWidth;
    private const float DecreaseLeft = IncreaseLeft - ValueWidth - StepperWidth;
    private const float ParameterBaselineOffset = 33;
    private const float ResetTop = 812, ResetWidth = 216, ResetHeight = 48;
    public const float ModeHeadingBaseline = 149, DescriptionBaseline = 267, RevealHintBaseline = 300;
    public const float ActivityHintBaseline = 764, DashboardHintBaseline = 793, ErrorBaseline = 890;
    public const float ValueCenter = IncreaseLeft - ValueWidth / 2;

    public static KeyBounds ModeBounds(int column) => new(
        ContentLeft + column * (ModeWidth + ModeGap), ModeTop, ModeWidth, ModeHeight);
    public static KeyBounds DecreaseBounds(int row) => new(
        DecreaseLeft, ParameterTop + row * ParameterRowSpacing, StepperWidth, StepperHeight);
    public static KeyBounds IncreaseBounds(int row) => new(
        IncreaseLeft, ParameterTop + row * ParameterRowSpacing, StepperWidth, StepperHeight);
    public static float ParameterBaseline(int row) => ParameterTop + row * ParameterRowSpacing + ParameterBaselineOffset;
    public static KeyBounds ResetBounds => new(ContentRight - ResetWidth, ResetTop, ResetWidth, ResetHeight);
}
