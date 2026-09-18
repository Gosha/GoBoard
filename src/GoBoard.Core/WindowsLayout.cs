namespace GoBoard.Core;

internal sealed partial class WindowsLayout(nint handle)
{
    public const uint UsHandle = 0x04090409, SwedishHandle = 0x041d041d;
    public nint Handle { get; } = handle;
    private uint Id => unchecked((uint)(long)Handle);
    // Keep the actual Windows HKL separate from the geometry/legend fallback.
    public bool Supported => Id is SwedishHandle or UsHandle;
    public bool Swedish => Id == SwedishHandle;
    public IReadOnlyList<KeyboardKey> Keys => Swedish ? KeyboardLayout.SwedishKeys : KeyboardLayout.Keys;
    public string Name => Id switch { UsHandle => "US English", SwedishHandle => "Svenska", 0x08090809 => "UK", _ => "Unknown layout" };
    public string Status => Supported ? Name : "Using US English labels; output may differ.";
    public readonly record struct KeyLegend(string Text, bool Dead = false);

    public KeyLegend Legend(KeyboardKey key, bool shift, bool altGr, bool caps)
    {
        if (!key.Printable) return new(key.Id == "AltGr" && !Swedish ? "Alt" : key.Label);
        var state = (shift ? 1 : 0) | (altGr ? 2 : 0) | (caps ? 4 : 0);
        var entry = (Swedish ? SwedishLegends : UsLegends)[key.Scan];
        return new(entry.Text[state], (entry.DeadMask & (1 << state)) != 0);
    }
}
