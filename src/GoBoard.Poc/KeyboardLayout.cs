namespace GoBoard.Poc;

internal readonly record struct KeyBounds(float X, float Y, float Width, float Height)
{
    public bool Contains(float x, float y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

internal sealed record KeyboardKey(string Id, string Label, ushort Scan, KeyBounds Bounds, bool Repeat = true, bool Printable = false)
{
    public bool IsModifier => Id is "Shift" or "Ctrl" or "Alt" or "AltGr" or "Win";
}

internal static class KeyboardLayout
{
    public static readonly IReadOnlyList<KeyboardKey> Keys = Create(false);
    public static readonly IReadOnlyList<KeyboardKey> SwedishKeys = Create(true);
    public static KeyboardKey Hit(float x, float y, IReadOnlyList<KeyboardKey> keys = null) => float.IsFinite(x) && float.IsFinite(y)
        ? (keys ?? Keys).FirstOrDefault(k => k.Bounds.Contains(x, y)) : null;

    // OpenVR mouse coordinates have a bottom-left origin. Drawing uses top-left.
    public static KeyboardKey HitOpenVr(float x, float y, IReadOnlyList<KeyboardKey> keys = null) => Hit(x, Panel.LayoutHeight - y, keys);

    private static KeyboardKey[] Create(bool swedish)
    {
        var keys = new List<KeyboardKey>();
        for (var i = 1; i <= 12; i++)
            keys.Add(new($"F{i}", $"F{i}", (ushort)(i <= 10 ? 0x3a + i : 0x57 + i - 11), new(16 + (i - 1) * 52, 50, 48, 32), false));
        void Row(string letters, ushort[] scans, float x, float y)
        {
            for (var i = 0; i < letters.Length; i++)
                keys.Add(new(letters[i].ToString(), letters[i].ToString(), scans[i], new(x + i * 48, y, 44, 40), Printable: true));
        }
        void Symbol(string id, string label, ushort scan, float x, float y)
            => keys.Add(new(id, label, scan, new(x, y, 44, 40), Printable: true));
        Symbol("Backquote", "`", 0x29, 16, 90);
        Row("1234567890", [2, 3, 4, 5, 6, 7, 8, 9, 10, 11], 64, 90);
        Symbol("Minus", "-", 0x0c, 544, 90); Symbol("Equals", "=", 0x0d, 592, 90);
        Row("qwertyuiop", [0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19], 16, 136);
        Symbol("BracketLeft", "[", 0x1a, 496, 136); Symbol("BracketRight", "]", 0x1b, 544, 136);
        Row("asdfghjkl", [0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26], 40, 182);
        Symbol("Semicolon", ";", 0x27, 472, 182); Symbol("Quote", "'", 0x28, 520, 182);
        Symbol("Backslash", "\\", 0x2b, 592, 136);
        keys.Add(new("Shift", "Shift", 0x2a, new(16, 228, swedish ? 68 : 116, 40), false));
        if (swedish) Symbol("Iso", "<", 0x56, 88, 228);
        Row("zxcvbnm", [0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32], 136, 228);
        Symbol("Comma", ",", 0x33, 472, 228); Symbol("Period", ".", 0x34, 520, 228); Symbol("Slash", "/", 0x35, 568, 228);
        keys.Add(new("Ctrl", "Ctrl", 0x1d, new(16, 274, 68, 40), false));
        keys.Add(new("Alt", "Alt", 0x38, new(88, 274, 68, 40), false));
        keys.Add(new("AltGr", "AltGr", 0xe038, new(160, 274, 68, 40), false));
        keys.Add(new("Space", "Space", 0x39, new(232, 274, 284, 40)));
        keys.Add(new("Enter", "Enter", 0x1c, new(520, 274, 116, 40), false));
        void Side(string id, string label, ushort scan, int column, float y, bool repeat = true)
            => keys.Add(new(id, label, scan, new(656 + column * 58, y, 52, y == 50 ? 32 : 40), repeat));
        Side("Escape", "Esc", 0x01, 0, 50, false);
        Side("Tab", "Tab", 0x0f, 1, 50);
        keys.Add(new("Backspace", "Backspace", 0x0e, new(656, 90, 168, 40)));
        Side("Home", "Home", 0xe047, 0, 136);
        Side("PageUp", "PgUp", 0xe049, 1, 136);
        Side("Insert", "Ins", 0xe052, 2, 136, false);
        Side("End", "End", 0xe04f, 0, 182);
        Side("PageDown", "PgDn", 0xe051, 1, 182);
        Side("Delete", "Del", 0xe053, 2, 182);
        Side("Up", "↑", 0xe048, 1, 228);
        Side("Win", "Win", 0xe05b, 0, 228, false);
        Side("Left", "←", 0xe04b, 0, 274);
        Side("Down", "↓", 0xe050, 1, 274);
        Side("Right", "→", 0xe04d, 2, 274);
        return keys.ToArray();
    }
}
