using System.Numerics;

namespace GoBoard.Core;

internal enum KeyboardAction { ToggleNumpad, ResetPosition }

internal static class MainKeyboardControls
{
    public const int Size = 44, Gap = 12;
    // Same target in both states: floating beside the main keys, then embedded
    // in the otherwise empty function row directly above Num Lock.
    public static readonly KeyBounds NumpadBounds = new(KeyboardLayout.NumpadLeft, OverlayGeometry.PanelPadding, Size, Size);
    public static IReadOnlyList<KeyBounds> KeyboardInputBounds(bool numpad) => numpad
        ? [new(0, 0, OverlayGeometry.PanelWidth, NumpadBounds.Y + NumpadBounds.Height),
           new(0, NumpadBounds.Y + NumpadBounds.Height, OverlayGeometry.Width(true),
               OverlayGeometry.PanelHeight - NumpadBounds.Y - NumpadBounds.Height)]
        : [new(0, 0, OverlayGeometry.PanelWidth, OverlayGeometry.PanelHeight)];
    public static readonly KeyboardAction[] Actions = [KeyboardAction.ToggleNumpad, KeyboardAction.ResetPosition];
    public static string Name(KeyboardAction action) => action == KeyboardAction.ToggleNumpad ? "Toggle numpad" : "Reset position";
    public static bool Visible(KeyboardAction action, BoardSettings settings) =>
        action != KeyboardAction.ToggleNumpad || settings.NumpadButtonEnabled;
    public static float WidthInMeters(KeyboardAction action, float scale) => action == KeyboardAction.ToggleNumpad
        ? Size * ProgrammableKeys.MetersPerUnit * scale : .036f;

    public static Matrix4x4 Offset(KeyboardAction action, float scale)
    {
        if (action == KeyboardAction.ToggleNumpad)
            return Matrix4x4.CreateTranslation(
                (NumpadBounds.X + NumpadBounds.Width / 2 - OverlayGeometry.PanelWidth / 2f) * ProgrammableKeys.MetersPerUnit * scale,
                (OverlayGeometry.PanelHeight / 2f - NumpadBounds.Y - NumpadBounds.Height / 2) * ProgrammableKeys.MetersPerUnit * scale,
                .002f * scale);
        // The handle retains its physical size while the keyboard scales. Align
        // the arrow with its visible line, outside its transparent grab target.
        var size = WidthInMeters(action, scale);
        var handle = OverlayGeometry.GrabFromScaledPanel(scale);
        return Matrix4x4.CreateTranslation(OverlayGeometry.GrabWidthInMeters / 2 + .008f + size / 2,
            handle.M42 + .01775f, handle.M43);
    }

    public static KeyBounds DesktopBounds(KeyboardAction action, float header, float scale) => action switch
    {
        KeyboardAction.ToggleNumpad => new(NumpadBounds.X * scale, header + NumpadBounds.Y * scale, Size * scale, Size * scale),
        _ => new(-(Size + Gap) * scale, (header - Size * scale) / 2, Size * scale, Size * scale)
    };

    public static BoardSettings Apply(KeyboardAction action, BoardSettings settings) =>
        SettingsControls.Apply(action == KeyboardAction.ToggleNumpad ? SettingsAction.ToggleNumpad : SettingsAction.ResetPosition, settings);
}
