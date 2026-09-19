namespace GoBoard.Core;

// A shortcut is one balanced physical key chord, never a text/script macro.
internal sealed record KeyboardShortcut(ushort Scan, bool Ctrl = false, bool Alt = false, bool Shift = false, bool Win = false)
{
    public ushort[] Modifiers => new (ushort Scan, bool On)[]
        { (0xe05b, Win), (0x1d, Ctrl), (0x38, Alt), (0x2a, Shift) }.Where(m => m.On).Select(m => m.Scan).ToArray();
    public string ChordLabel => ChordFor(null);
    public string ChordFor(WindowsLayout layout) => Scan == 0 ? "Unassigned" : string.Join("+", new[] { Win ? "Win" : null, Ctrl ? "Ctrl" : null,
        Alt ? "Alt" : null, Shift ? "Shift" : null, ProgrammableKeys.KeyName(Scan, layout, Shift) }.Where(s => s != null));
    public string Label => LabelFor(null);
    public string LabelFor(WindowsLayout layout) => ProgrammableKeys.Presets.FirstOrDefault(p => p.ForLayout(layout) == this)?.Label ?? ChordFor(layout);
    public KeyboardShortcut Normalize() => ProgrammableKeys.IsAllowed(Scan) ? this : new(0);
}

// Named fields give settings snapshots value equality and let concurrent editors
// merge just the selected slot without replacing another editor's assignments.
internal sealed record ProgrammableKeySettings
{
    public const int MaxColumns = 4, MaxRows = 5, Capacity = MaxColumns * MaxRows;
    public bool Enabled { get; init; }
    public int Columns { get; init; } = 2;
    public int Rows { get; init; } = 4;
    public KeyboardShortcut Key1 { get; init; } = ProgrammableKeys.Presets[0].Shortcut;
    public KeyboardShortcut Key2 { get; init; } = ProgrammableKeys.Presets[1].Shortcut;
    public KeyboardShortcut Key3 { get; init; } = ProgrammableKeys.Presets[2].Shortcut;
    public KeyboardShortcut Key4 { get; init; } = ProgrammableKeys.Presets[3].Shortcut;
    public KeyboardShortcut Key5 { get; init; } = ProgrammableKeys.Presets[4].Shortcut;
    public KeyboardShortcut Key6 { get; init; } = ProgrammableKeys.Presets[5].Shortcut;
    public KeyboardShortcut Key7 { get; init; } = ProgrammableKeys.Presets[6].Shortcut;
    public KeyboardShortcut Key8 { get; init; } = ProgrammableKeys.Presets[7].Shortcut;
    public KeyboardShortcut Key9 { get; init; } = new(0);
    public KeyboardShortcut Key10 { get; init; } = new(0);
    public KeyboardShortcut Key11 { get; init; } = new(0);
    public KeyboardShortcut Key12 { get; init; } = new(0);
    public KeyboardShortcut Key13 { get; init; } = new(0);
    public KeyboardShortcut Key14 { get; init; } = new(0);
    public KeyboardShortcut Key15 { get; init; } = new(0);
    public KeyboardShortcut Key16 { get; init; } = new(0);
    public KeyboardShortcut Key17 { get; init; } = new(0);
    public KeyboardShortcut Key18 { get; init; } = new(0);
    public KeyboardShortcut Key19 { get; init; } = new(0);
    public KeyboardShortcut Key20 { get; init; } = new(0);
    // Stable row/column slots retain assignments when a column or row is hidden.
    // Keep the original ten saved fields in the first two columns for compatibility.
    public static int SlotAt(int row, int column) => column < 2 ? row * 2 + column : 10 + row * 2 + column - 2;
    public static (int Row, int Column) PositionOf(int slot) => slot < 10 ? (slot / 2, slot % 2) : ((slot - 10) / 2, 2 + (slot - 10) % 2);
    public IEnumerable<int> VisibleSlots => Enumerable.Range(0, Rows).SelectMany(row => Enumerable.Range(0, Columns).Select(col => SlotAt(row, col)));
    public int NumberFor(int slot) { var (row, column) = PositionOf(slot); return row * Columns + column + 1; }
    public KeyboardShortcut Get(int slot) => slot switch
    { 0 => Key1, 1 => Key2, 2 => Key3, 3 => Key4, 4 => Key5, 5 => Key6, 6 => Key7, 7 => Key8, 8 => Key9, 9 => Key10,
      10 => Key11, 11 => Key12, 12 => Key13, 13 => Key14, 14 => Key15, 15 => Key16, 16 => Key17, 17 => Key18, 18 => Key19, 19 => Key20,
      _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
    public ProgrammableKeySettings Set(int slot, KeyboardShortcut key) => slot switch
    { 0 => this with { Key1 = key }, 1 => this with { Key2 = key }, 2 => this with { Key3 = key }, 3 => this with { Key4 = key },
      4 => this with { Key5 = key }, 5 => this with { Key6 = key }, 6 => this with { Key7 = key }, 7 => this with { Key8 = key },
      8 => this with { Key9 = key }, 9 => this with { Key10 = key },
      10 => this with { Key11 = key }, 11 => this with { Key12 = key }, 12 => this with { Key13 = key }, 13 => this with { Key14 = key },
      14 => this with { Key15 = key }, 15 => this with { Key16 = key }, 16 => this with { Key17 = key }, 17 => this with { Key18 = key },
      18 => this with { Key19 = key }, 19 => this with { Key20 = key }, _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
    public ProgrammableKeySettings Normalize()
    {
        var result = this with { Columns = Math.Clamp(Columns, 1, MaxColumns), Rows = Math.Clamp(Rows, 1, MaxRows) };
        for (var i = 0; i < Capacity; i++) result = result.Set(i, (Get(i) ?? (i < 8 ? ProgrammableKeys.Presets[i].Shortcut : new(0))).Normalize());
        return result;
    }
}

internal static class ProgrammableKeys
{
    public const int KeyWidth = 86, KeyHeight = 54, Gap = 2, Padding = 4, ToggleSize = 44, StatusHeight = 24;
    // Reserved internal identities; Windows maps these to media virtual keys.
    public const ushort MediaNext = 0xffb0, MediaPrevious = 0xffb1, MediaStop = 0xffb2, MediaPlayPause = 0xffb3;
    public const ushort VolumeMute = 0xffad, VolumeDown = 0xffae, VolumeUp = 0xffaf;
    public const ushort BrowserBack = 0xffa6, BrowserForward = 0xffa7, BrowserRefresh = 0xffa8, BrowserStop = 0xffa9,
        BrowserSearch = 0xffaa, BrowserFavorites = 0xffab, BrowserHome = 0xffac;
    public const ushort NumLock = 0xe045, NumEnter = 0xe01c, NumDivide = 0xe035;
    public sealed record Preset(string Label, KeyboardShortcut Shortcut, string Letter = null)
    {
        // Resolve letter-based convenience presets from cached layout labels at selection time.
        public KeyboardShortcut ForLayout(WindowsLayout layout)
        {
            if (Letter == null || layout == null) return Shortcut;
            var key = layout.Keys.FirstOrDefault(k => k.Printable &&
                string.Equals(layout.Legend(k, false, false, false).Text, Letter, StringComparison.OrdinalIgnoreCase));
            return key == null ? Shortcut : Shortcut with { Scan = key.Scan };
        }
    }
    public static readonly (ushort Scan, string Label)[] SystemKeys =
    [
        (MediaPrevious, "Prev track"), (MediaPlayPause, "Play / Pause"), (MediaStop, "Stop"), (MediaNext, "Next track"),
        (VolumeMute, "Volume mute"), (VolumeDown, "Volume down"), (VolumeUp, "Volume up"),
        (BrowserBack, "Browser back"), (BrowserForward, "Browser forward"), (BrowserRefresh, "Browser refresh"),
        (BrowserStop, "Browser stop"), (BrowserHome, "Browser home"), (BrowserSearch, "Browser search"), (BrowserFavorites, "Browser favorites")
    ];
    // Separate identities for keypad digits/navigation, operators and Enter.
    public static readonly KeyboardKey[] NumpadKeys = CreateNumpad();
    private static KeyboardKey[] CreateNumpad()
    {
        var keys = new List<KeyboardKey>();
        void Key(string name, string label, ushort scan, int col, int row, int cols = 1, int rows = 1)
            => keys.Add(new(name, label, scan, new(col * 44, row * 44, cols * 44 - 2, rows * 44 - 2), Repeat: false));
        Key("NumLock", "NumLk", NumLock, 0, 0); Key("NumDivide", "/", NumDivide, 1, 0);
        Key("NumMultiply", "*", 0x37, 2, 0); Key("NumSubtract", "−", 0x4a, 3, 0);
        Key("Num7", "7", 0x47, 0, 1); Key("Num8", "8", 0x48, 1, 1); Key("Num9", "9", 0x49, 2, 1);
        Key("NumAdd", "+", 0x4e, 3, 1, rows: 2);
        Key("Num4", "4", 0x4b, 0, 2); Key("Num5", "5", 0x4c, 1, 2); Key("Num6", "6", 0x4d, 2, 2);
        Key("Num1", "1", 0x4f, 0, 3); Key("Num2", "2", 0x50, 1, 3); Key("Num3", "3", 0x51, 2, 3);
        Key("NumEnter", "Enter", NumEnter, 3, 3, rows: 2);
        Key("Num0", "0", 0x52, 0, 4, cols: 2); Key("NumDecimal", "Dec", 0x53, 2, 4);
        return keys.ToArray();
    }
    public static readonly Preset[] Presets =
    [
        new("Desktop ←", new(0xe04b, Ctrl: true, Win: true)),
        new("Desktop →", new(0xe04d, Ctrl: true, Win: true)),
        new("Play / Pause", new(MediaPlayPause)),
        new("Next track", new(MediaNext)),
        new("Prev track", new(MediaPrevious)),
        new("Task view", new(0x0f, Win: true)),
        new("Language", new(0x39, Win: true)),
        new("Dictation", new(0x23, Win: true)),
        new("Stop", new(MediaStop)),
        new("Volume mute", new(VolumeMute)), new("Volume down", new(VolumeDown)), new("Volume up", new(VolumeUp)),
        new("Browser back", new(BrowserBack)), new("Browser forward", new(BrowserForward)),
        new("Browser refresh", new(BrowserRefresh)), new("Browser stop", new(BrowserStop)),
        new("Browser home", new(BrowserHome)), new("Browser search", new(BrowserSearch)), new("Browser favorites", new(BrowserFavorites)),
        new("Copy", new(0x2e, Ctrl: true), "C"), new("Paste", new(0x2f, Ctrl: true), "V"),
        new("Cut", new(0x2d, Ctrl: true), "X"), new("Undo", new(0x2c, Ctrl: true), "Z"),
        new("Redo", new(0x15, Ctrl: true), "Y"), new("Select all", new(0x1e, Ctrl: true), "A"),
        new("Capture region", new(0x1f, Shift: true, Win: true), "S"),
        new("Capture window", new(KeyboardLayout.PrintScreenScan, Alt: true)),
        new("Save screenshot", new(KeyboardLayout.PrintScreenScan, Win: true)),
        new("Snap left", new(0xe04b, Win: true)), new("Snap right", new(0xe04d, Win: true)),
        new("Maximize", new(0xe048, Win: true)), new("Minimize / restore", new(0xe050, Win: true))
    ];
    public static bool IsMedia(ushort scan) => scan is MediaNext or MediaPrevious or MediaStop or MediaPlayPause;
    public static bool IsSystemKey(ushort scan) => SystemKeys.Any(k => k.Scan == scan);
    public static bool IsAllowed(ushort scan) => scan == 0 || IsSystemKey(scan) || NumpadKeys.Any(k => k.Scan == scan) ||
        KeyboardLayout.IsoKeys.Any(k => !k.IsModifier && k.Scan == scan);
    public static (ushort Scan, string Label)[] ChoicesFor(WindowsLayout layout = null, bool shift = false)
    {
        layout ??= new((nint)WindowsLayout.UsHandle);
        return layout.Keys.Where(k => !k.IsModifier && k.Scan != KeyboardLayout.ImeToggleKey)
            .Select(k => (k.Scan, KeyName(k.Scan, layout, shift)))
            .Concat(NumpadKeys.Select(k => (k.Scan, KeyName(k.Scan, layout))))
            .Concat(SystemKeys).Append(((ushort)0, "None")).ToArray();
    }
    public static string KeyName(ushort scan, WindowsLayout layout = null, bool shift = false)
    {
        if (scan == 0) return "Unassigned";
        if (IsSystemKey(scan)) return SystemKeys.First(k => k.Scan == scan).Label;
        if (NumpadKeys.FirstOrDefault(k => k.Scan == scan) is { } numpad)
            return scan == NumLock ? "Num Lock" : numpad.Id == "NumDecimal" ? "Num decimal" : "Num " + numpad.Label;
        layout ??= new((nint)WindowsLayout.UsHandle);
        var key = layout.Keys.FirstOrDefault(k => k.Scan == scan);
        if (key == null) return scan == 0x56 ? "ISO key" : "Unknown";
        if (!key.Printable) return key.Id is "Space" or "Left" or "Right" or "Up" or "Down" ? key.Id : key.Label;
        var legend = layout.Legend(key, shift, false, false);
        return string.IsNullOrEmpty(legend.Text) ? key.Label : legend.Text.ToUpperInvariant();
    }
    public static int Width(ProgrammableKeySettings settings) => Padding * 2 + settings.Columns * (KeyWidth + Gap) - Gap;
    public static int Height(ProgrammableKeySettings settings, bool footer = true) => Padding * 2 + settings.Rows * (KeyHeight + Gap) - Gap + (footer ? StatusHeight : 0);
    public static float MetersPerUnit => OverlayGeometry.PanelWidthInMeters / OverlayGeometry.PanelWidth;
    public static IReadOnlyList<KeyboardKey> CreatePanel(ProgrammableKeySettings settings, WindowsLayout layout) =>
        settings.VisibleSlots.Select(i => new KeyboardKey($"Shortcut{i + 1}", settings.Get(i).LabelFor(layout),
            (ushort)(0xff80 + i), new(Padding + ProgrammableKeySettings.PositionOf(i).Column * (KeyWidth + Gap),
                Padding + ProgrammableKeySettings.PositionOf(i).Row * (KeyHeight + Gap), KeyWidth, KeyHeight),
            Repeat: false, Shortcut: settings.Get(i))).ToArray();
}
