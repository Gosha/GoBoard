namespace GoBoard.Core;

internal enum KeyboardGeometry { Auto, Ansi, Iso }

internal sealed partial class WindowsLayout(nint handle)
{
    public const uint UsHandle = 0x04090409, SwedishHandle = 0x041d041d;
    public nint Handle { get; } = handle;
    private uint Id => unchecked((uint)(long)Handle);
    // Keep the actual Windows HKL separate from the geometry/legend fallback.
    private readonly IReadOnlyDictionary<ushort, KeyLegend[]> generated;
    private readonly string generatedName, notice;
    private readonly bool iso, altGr;
    public bool Supported => generated != null || Id is SwedishHandle or UsHandle;
    public bool WindowsDerived => generated != null;
    public bool Swedish => Id == SwedishHandle;
    public bool Japanese => IsJapanese(Handle);
    public static bool IsJapanese(nint handle) => (unchecked((uint)(long)handle) & 0x3ff) == 0x11;
    public bool Iso => generated != null ? iso : Swedish;
    public bool HasAltGr => generated != null ? altGr : Swedish;
    public IReadOnlyList<KeyboardKey> Keys => Japanese ? KeyboardLayout.JapaneseKeys : Iso ? KeyboardLayout.IsoKeys : KeyboardLayout.Keys;
    public string Name => generatedName ?? (Japanese ? "Japanese IME" : Id switch { UsHandle => "US English", SwedishHandle => "Svenska", 0x08090809 => "UK", _ => "Unknown layout" });
    public string Notice => notice ?? (Supported ? null : "Using US English labels; output may differ.");
    public string Status => Notice ?? Name;
    public readonly record struct KeyLegend(string Text, bool Dead = false);

    public WindowsLayout(nint handle, string name, bool iso, bool altGr,
        IReadOnlyDictionary<ushort, KeyLegend[]> legends, string notice = null) : this(handle)
    {
        // Copy at the boundary: cached layouts are immutable snapshots.
        generated = legends.ToDictionary(p => p.Key, p => p.Value.ToArray());
        foreach (var key in (iso ? KeyboardLayout.IsoKeys : KeyboardLayout.Keys).Where(k => k.Printable))
            if (!generated.TryGetValue(key.Scan, out var states) || states.Length != 8 || states.Any(s => s.Text == null))
                throw new ArgumentException("Every printable key needs eight legend states.", nameof(legends));
        generatedName = name; this.iso = iso; this.altGr = altGr; this.notice = notice;
    }

    public KeyLegend Legend(KeyboardKey key, bool shift, bool altGr, bool caps)
    {
        if (!key.Printable) return new(key.Id == "AltGr" && !HasAltGr ? "Alt" : key.Label);
        var state = (shift ? 1 : 0) | (altGr ? 2 : 0) | (caps ? 4 : 0);
        if (generated != null) return generated[key.Scan][state];
        var entry = (Swedish ? SwedishLegends : UsLegends)[key.Scan];
        return new(entry.Text[state], (entry.DeadMask & (1 << state)) != 0);
    }
}
