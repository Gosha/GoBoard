namespace GoBoard.Core;

internal readonly record struct KeyBounds(float X, float Y, float Width, float Height)
{
    public bool Contains(float x, float y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

// ISO Enter excludes its lower-left corner. Drawing and picking share this shape.
internal sealed record KeyboardKey(string Id, string Label, ushort Scan, KeyBounds Bounds,
    bool Repeat = true, bool Printable = false, float CutoutWidth = 0, float CutoutTop = 0, KeyboardShortcut Shortcut = null)
{
    public bool IsModifier => Id is "Shift" or "RightShift" or "Ctrl" or "RightCtrl" or "Alt" or "AltGr" or "Win";
    public bool Contains(float x, float y) => Bounds.Contains(x, y) &&
        !(CutoutWidth > 0 && x < Bounds.X + CutoutWidth && y >= Bounds.Y + CutoutTop);
}

internal static class KeyboardLayout
{
    private const int KeySize = 44, KeyGap = 2, RowPitch = KeySize + KeyGap;
    public const ushort PrintScreenScan = 0xe037, ScrollLockScan = 0x46, PauseScan = 0xe145;
    // Internal key identity, not a physical scan code. Windows sends VK_KANJI.
    public const ushort ImeToggleKey = 0xff19;
    public static readonly IReadOnlyList<KeyboardKey> Keys = Create(false);
    public static readonly IReadOnlyList<KeyboardKey> IsoKeys = Create(true);
    public static readonly IReadOnlyList<KeyboardKey> JapaneseKeys = Create(false, japanese: true);
    public static IReadOnlyList<KeyboardKey> SwedishKeys => IsoKeys;
    private const int NumpadGroupGap = 14;
    public const int NumpadExtraWidth = 4 * RowPitch + NumpadGroupGap - KeyGap;
    public static IReadOnlyList<KeyboardKey> WithNumpad(IReadOnlyList<KeyboardKey> keys) => keys.Concat(
        ProgrammableKeys.NumpadKeys.Select(key => key with
        {
            Bounds = new(OverlayGeometry.PanelWidth - OverlayGeometry.PanelPadding + NumpadGroupGap + key.Bounds.X / 44 * RowPitch,
                OverlayGeometry.PanelPadding + RowPitch + key.Bounds.Y / 44 * RowPitch,
                (key.Bounds.Width + KeyGap) / 44 * RowPitch - KeyGap,
                (key.Bounds.Height + KeyGap) / 44 * RowPitch - KeyGap),
            Repeat = key.Scan is not (ProgrammableKeys.NumLock or ProgrammableKeys.NumEnter)
        })).ToArray();
    public static KeyboardKey Hit(float x, float y, IReadOnlyList<KeyboardKey> keys = null) => float.IsFinite(x) && float.IsFinite(y)
        ? (keys ?? Keys).FirstOrDefault(k => k.Contains(x, y)) : null;

    // OpenVR mouse coordinates have a bottom-left origin. Drawing uses top-left.
    public static KeyboardKey HitOpenVr(float x, float y, IReadOnlyList<KeyboardKey> keys = null) => Hit(x, OverlayGeometry.PanelHeight - y, keys);

    private static KeyboardKey[] Create(bool iso, bool japanese = false)
    {
        var keys = new List<KeyboardKey>();
        void Key(string id, string label, ushort scan, float x, float y, float width = KeySize, float height = KeySize, bool repeat = true)
            => keys.Add(new(id, label, scan, new(x, y, width, height), repeat));
        void Symbol(string id, string label, ushort scan, float x, float y, float width = KeySize)
            => keys.Add(new(id, label, scan, new(x, y, width, KeySize), Printable: true));
        void Row(string letters, ushort[] scans, float x, float y)
        {
            for (var i = 0; i < letters.Length; i++)
                Symbol(letters[i].ToString(), letters[i].ToString(), scans[i], x + i * RowPitch, y);
        }

        Key("Escape", "Esc", 0x01, 16, 12, 52, KeySize, false);
        for (var i = 1; i <= 12; i++)
            Key($"F{i}", $"F{i}", (ushort)(i <= 10 ? 0x3a + i : 0x57 + i - 11),
                116 + (i - 1) * RowPitch + (i - 1) / 4 * 20, 12, KeySize, KeySize, false);
        Key("PrintScreen", "PrtSc", PrintScreenScan, 722, 12, KeySize, KeySize, false);
        Key("ScrollLock", "ScrLk", ScrollLockScan, 768, 12, KeySize, KeySize, false);
        Key("Pause", "Pause", PauseScan, 814, 12, KeySize, KeySize, false);

        Symbol("Backquote", "`", 0x29, 16, 12 + RowPitch);
        Row("1234567890", [2, 3, 4, 5, 6, 7, 8, 9, 10, 11], 62, 12 + RowPitch);
        Symbol("Minus", "-", 0x0c, 522, 12 + RowPitch);
        Symbol("Equals", "=", 0x0d, 568, 12 + RowPitch);
        Key("Backspace", "Backspace", 0x0e, 614, 12 + RowPitch, 94);
        // Standard desktop 3-column by 2-row navigation cluster.
        Key("Insert", "Ins", 0xe052, 722, 12 + RowPitch, repeat: false);
        Key("Home", "Home", 0xe047, 768, 12 + RowPitch);
        Key("PageUp", "PgUp", 0xe049, 814, 12 + RowPitch);
        Key("Delete", "Delete", 0xe053, 722, 12 + RowPitch * 2);
        Key("End", "End", 0xe04f, 768, 12 + RowPitch * 2);
        Key("PageDown", "PgDn", 0xe051, 814, 12 + RowPitch * 2);

        Key("Tab", "Tab", 0x0f, 16, 12 + RowPitch * 2, 64);
        Row("qwertyuiop", [0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19], 82, 12 + RowPitch * 2);
        Symbol("BracketLeft", "[", 0x1a, 542, 12 + RowPitch * 2);
        Symbol("BracketRight", "]", 0x1b, 588, 12 + RowPitch * 2);
        if (iso)
            keys.Add(new("Enter", "Enter", 0x1c, new(634, 12 + RowPitch * 2, 74, KeySize * 2 + KeyGap), false, CutoutWidth: 16, CutoutTop: KeySize));
        else
        {
            Symbol("Backslash", "\\", 0x2b, 634, 12 + RowPitch * 2, 74);
            Key("Enter", "Enter", 0x1c, 604, 12 + RowPitch * 3, 104, repeat: false);
        }

        Key("Caps", "Caps", 0x3a, 16, 12 + RowPitch * 3, 80, repeat: false);
        Row("asdfghjkl", [0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26], 98, 12 + RowPitch * 3);
        Symbol("Semicolon", ";", 0x27, 512, 12 + RowPitch * 3);
        Symbol("Quote", "'", 0x28, 558, 12 + RowPitch * 3);
        if (iso) Symbol("Backslash", "\\", 0x2b, 604, 12 + RowPitch * 3);

        Key("Shift", "Shift", 0x2a, 16, 12 + RowPitch * 4, iso ? 70 : 116, repeat: false);
        if (iso) Symbol("Iso", "<", 0x56, 88, 12 + RowPitch * 4, KeySize);
        Row("zxcvbnm", [0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32], 134, 12 + RowPitch * 4);
        Symbol("Comma", ",", 0x33, 456, 12 + RowPitch * 4);
        Symbol("Period", ".", 0x34, 502, 12 + RowPitch * 4);
        Symbol("Slash", "/", 0x35, 548, 12 + RowPitch * 4);
        // Both buttons intentionally operate the same logical modifier.
        Key("RightShift", "Shift", 0x2a, 594, 12 + RowPitch * 4, 114, repeat: false);
        Key("Up", "▲", 0xe048, 768, 12 + RowPitch * 4);

        Key("Ctrl", "Ctrl", 0x1d, 16, 12 + RowPitch * 5, 56, repeat: false);
        Key("Win", "Win", 0xe05b, 74, 12 + RowPitch * 5, 52, repeat: false);
        Key("Alt", "Alt", 0x38, 128, 12 + RowPitch * 5, 56, repeat: false);
        Key("Space", "", 0x39, 186, 12 + RowPitch * 5, japanese ? 256 : 324);
        if (japanese) Key("ImeToggle", "あ/A", ImeToggleKey, 444, 12 + RowPitch * 5, 66, repeat: false);
        Key("AltGr", "AltGr", 0xe038, 512, 12 + RowPitch * 5, 60, repeat: false);
        Key("Menu", "Menu", 0xe05d, 574, 12 + RowPitch * 5, 68, repeat: false);
        Key("RightCtrl", "Ctrl", 0x1d, 644, 12 + RowPitch * 5, 64, repeat: false);
        Key("Left", "◀", 0xe04b, 722, 12 + RowPitch * 5);
        Key("Down", "▼", 0xe050, 768, 12 + RowPitch * 5);
        Key("Right", "▶", 0xe04d, 814, 12 + RowPitch * 5);
        // The design coordinates start at (16, 12). Trim only the outside
        // margin; drawing and picking use the same compact key geometry.
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
