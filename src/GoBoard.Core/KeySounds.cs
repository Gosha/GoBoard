namespace GoBoard.Core;

internal static class KeySounds
{
    // Keep retired enum values readable for existing settings, but never offer them.
    private static readonly KeySound[] Presets = [KeySound.CushionedWood, KeySound.SoftLowThud, KeySound.CherryMxBlue, KeySound.GateronYellowPairs];
    public static IReadOnlyList<KeySound> All => Presets;
    public static KeySound Canonical(KeySound sound) => sound switch
    {
        KeySound.GateronYellowModified => KeySound.GateronYellowPairs,
        KeySound.CherryMxClear => KeySound.CherryMxBlue,
        _ => sound
    };
    public static bool IsSampled(KeySound sound) => sound is
        KeySound.CherryMxBlue or KeySound.CherryMxClear or KeySound.GateronYellowModified or KeySound.GateronYellowPairs;
    public static bool HasPairedRelease(KeySound sound) => Canonical(sound) is KeySound.GateronYellowPairs or KeySound.CherryMxBlue;
    public static bool UsesLargeKeySound(KeyboardKey key) => key?.Scan is 0x39 or 0x1c or 0xe01c or 0x0e or 0x2a or 0x36;
    // Logical key area, independent of the user's overall keyboard size setting.
    // Doubling the area lowers the trial sound by one semitone.
    public static int BasePitchSteps(KeyboardKey key)
    {
        if (key == null || key.Scan is not (0x39 or 0x1c or 0xe01c or 0x2a or 0x36)) return 0;
        var area = key.Bounds.Width * key.Bounds.Height - key.CutoutWidth * (key.Bounds.Height - key.CutoutTop);
        return -(int)Math.Clamp(Math.Round(10 * Math.Log2(Math.Max(1, area / (44d * 44)))), 0, 30);
    }
    public static string Name(KeySound sound) => Canonical(sound) switch
    {
        KeySound.SoftLowThud => "Soft low thud",
        KeySound.CherryMxBlue => "Cherry MX Blue",
        KeySound.GateronYellowPairs => "Gateron Yellow",
        _ => "Cushioned wood"
    };
    public static KeySound Next(KeySound sound, bool previous = false)
    {
        var index = Array.IndexOf(Presets, Canonical(sound));
        if (index < 0) index = 0;
        return Presets[(index + (previous ? Presets.Length - 1 : 1)) % Presets.Length];
    }
}
