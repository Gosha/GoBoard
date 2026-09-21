using System.Numerics;

namespace GoBoard.Core;

internal enum KeyboardAction { ToggleNumpad, ResetPosition }

internal static class MainKeyboardControls
{
    public const int Size = 44, Gap = 12;
    public static readonly KeyboardAction[] Actions = [KeyboardAction.ToggleNumpad, KeyboardAction.ResetPosition];
    public static string Name(KeyboardAction action) => action == KeyboardAction.ToggleNumpad ? "Toggle numpad" : "Reset position";
    public static bool Visible(KeyboardAction action, BoardSettings settings) =>
        action != KeyboardAction.ToggleNumpad || settings.NumpadButtonEnabled;
    public static float WidthInMeters(KeyboardAction action, float scale) => action == KeyboardAction.ToggleNumpad
        ? Size * ProgrammableKeys.MetersPerUnit * scale : .036f;

    public static Matrix4x4 Offset(KeyboardAction action, bool numpad, float scale)
    {
        var size = WidthInMeters(action, scale);
        if (action == KeyboardAction.ToggleNumpad)
            return Matrix4x4.CreateTranslation(OverlayGeometry.RightEdgeInMeters(numpad) * scale + size / 2 + .012f * scale,
                (OverlayGeometry.PanelHeightInMeters * scale - size) / 2, .002f * scale);
        // The handle retains its physical size while the keyboard scales. Align
        // the arrow with its visible line, outside its transparent grab target.
        var handle = OverlayGeometry.GrabFromScaledPanel(scale);
        return Matrix4x4.CreateTranslation(OverlayGeometry.GrabWidthInMeters / 2 + .008f + size / 2,
            handle.M42 + .01775f, handle.M43);
    }

    public static KeyBounds DesktopBounds(KeyboardAction action, float width, float header, float scale) => action switch
    {
        KeyboardAction.ToggleNumpad => new(width + Gap * scale, header, Size * scale, Size * scale),
        _ => new(-(Size + Gap) * scale, (header - Size * scale) / 2, Size * scale, Size * scale)
    };

    public static BoardSettings Apply(KeyboardAction action, BoardSettings settings) =>
        SettingsControls.Apply(action == KeyboardAction.ToggleNumpad ? SettingsAction.ToggleNumpad : SettingsAction.ResetPosition, settings);
}
