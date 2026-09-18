namespace GoBoard.Core;

internal readonly record struct KeyBounds(float X, float Y, float Width, float Height)
{
    public bool Contains(float x, float y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

// ISO Enter excludes its lower-left corner. Drawing and picking share this shape.
internal sealed record KeyboardKey(string Id, string Label, ushort Scan, KeyBounds Bounds,
    bool Repeat = true, bool Printable = false, float CutoutWidth = 0, float CutoutTop = 0)
{
    public bool IsModifier => Id is "Shift" or "RightShift" or "Ctrl" or "RightCtrl" or "Alt" or "AltGr" or "Win";
    public bool Contains(float x, float y) => Bounds.Contains(x, y) &&
        !(CutoutWidth > 0 && x < Bounds.X + CutoutWidth && y >= Bounds.Y + CutoutTop);
}

internal static class KeyboardLayout
{
    public const ushort PrintScreenScan = 0xe037, ScrollLockScan = 0x46, PauseScan = 0xe145;
    public static readonly IReadOnlyList<KeyboardKey> Keys = Create(false);
    public static readonly IReadOnlyList<KeyboardKey> SwedishKeys = Create(true);
    public static KeyboardKey Hit(float x, float y, IReadOnlyList<KeyboardKey> keys = null) => float.IsFinite(x) && float.IsFinite(y)
        ? (keys ?? Keys).FirstOrDefault(k => k.Contains(x, y)) : null;

    // OpenVR mouse coordinates have a bottom-left origin. Drawing uses top-left.
    public static KeyboardKey HitOpenVr(float x, float y, IReadOnlyList<KeyboardKey> keys = null) => Hit(x, OverlayGeometry.PanelHeight - y, keys);

    private static KeyboardKey[] Create(bool swedish)
    {
        var keys = new List<KeyboardKey>();
        void Key(string id, string label, ushort scan, float x, float y, float width = 44, float height = 44, bool repeat = true)
            => keys.Add(new(id, label, scan, new(x, y, width, height), repeat));
        void Symbol(string id, string label, ushort scan, float x, float y, float width = 44)
            => keys.Add(new(id, label, scan, new(x, y, width, 44), Printable: true));
        void Row(string letters, ushort[] scans, float x, float y)
        {
            for (var i = 0; i < letters.Length; i++)
                Symbol(letters[i].ToString(), letters[i].ToString(), scans[i], x + i * 46, y);
        }

        Key("Escape", "Esc", 0x01, 16, 12, 52, 40, false);
        for (var i = 1; i <= 12; i++)
            Key($"F{i}", $"F{i}", (ushort)(i <= 10 ? 0x3a + i : 0x57 + i - 11),
                116 + (i - 1) * 46 + (i - 1) / 4 * 20, 12, 42, 40, false);
        Key("PrintScreen", "PrtSc", PrintScreenScan, 722, 12, 44, 40, false);
        Key("ScrollLock", "ScrLk", ScrollLockScan, 772, 12, 44, 40, false);
        Key("Pause", "Pause", PauseScan, 822, 12, 44, 40, false);

        Symbol("Backquote", "`", 0x29, 16, 62);
        Row("1234567890", [2, 3, 4, 5, 6, 7, 8, 9, 10, 11], 62, 62);
        Symbol("Minus", "-", 0x0c, 522, 62);
        Symbol("Equals", "=", 0x0d, 568, 62);
        Key("Backspace", "Backspace", 0x0e, 614, 62, 94);
        // Standard desktop 3-column by 2-row navigation cluster.
        Key("Insert", "Ins", 0xe052, 722, 62, repeat: false);
        Key("Home", "Home", 0xe047, 772, 62);
        Key("PageUp", "PgUp", 0xe049, 822, 62);
        Key("Delete", "Delete", 0xe053, 722, 112);
        Key("End", "End", 0xe04f, 772, 112);
        Key("PageDown", "PgDn", 0xe051, 822, 112);

        Key("Tab", "Tab", 0x0f, 16, 112, 62);
        Row("qwertyuiop", [0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19], 82, 112);
        Symbol("BracketLeft", "[", 0x1a, 542, 112);
        Symbol("BracketRight", "]", 0x1b, 588, 112);
        if (swedish)
            keys.Add(new("Enter", "Enter", 0x1c, new(634, 112, 74, 94), false, CutoutWidth: 16, CutoutTop: 48));
        else
        {
            Symbol("Backslash", "\\", 0x2b, 634, 112, 74);
            Key("Enter", "Enter", 0x1c, 604, 162, 104, repeat: false);
        }

        Key("Caps", "Caps", 0x3a, 16, 162, 78, repeat: false);
        Row("asdfghjkl", [0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26], 98, 162);
        Symbol("Semicolon", ";", 0x27, 512, 162);
        Symbol("Quote", "'", 0x28, 558, 162);
        if (swedish) Symbol("Backslash", "\\", 0x2b, 604, 162);

        Key("Shift", "Shift", 0x2a, 16, 212, swedish ? 68 : 114, repeat: false);
        if (swedish) Symbol("Iso", "<", 0x56, 88, 212, 42);
        Row("zxcvbnm", [0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32], 134, 212);
        Symbol("Comma", ",", 0x33, 456, 212);
        Symbol("Period", ".", 0x34, 502, 212);
        Symbol("Slash", "/", 0x35, 548, 212);
        // Both buttons intentionally operate the same logical modifier.
        Key("RightShift", "Shift", 0x2a, 596, 212, 112, repeat: false);
        Key("Up", "▲", 0xe048, 772, 212);

        Key("Ctrl", "Ctrl", 0x1d, 16, 262, 54, repeat: false);
        Key("Win", "Win", 0xe05b, 74, 262, 50, repeat: false);
        Key("Alt", "Alt", 0x38, 128, 262, 54, repeat: false);
        Key("Space", "", 0x39, 186, 262, 322);
        Key("AltGr", "AltGr", 0xe038, 512, 262, 58, repeat: false);
        Key("Menu", "Menu", 0xe05d, 574, 262, 66, repeat: false);
        Key("RightCtrl", "Ctrl", 0x1d, 644, 262, 64, repeat: false);
        Key("Left", "◀", 0xe04b, 722, 262);
        Key("Down", "▼", 0xe050, 772, 262);
        Key("Right", "▶", 0xe04d, 822, 262);
        // The design coordinates start at (16, 12). Trim only the outside
        // margin; key sizes, row gaps and the shared picking geometry stay intact.
        return keys.Select(key => key with
        {
            Bounds = key.Bounds with
            {
                X = key.Bounds.X - 16 + OverlayGeometry.PanelPadding,
                Y = key.Bounds.Y - 12 + OverlayGeometry.PanelPadding
            }
        }).ToArray();
    }
}
