namespace GoBoard.Platform.Windows;

// Tenth-semitone varispeed cuts are cached on settings changes. Runtime chooses
// a pitch for each press without resampling, allocations, or device-rate changes.
internal static class PairedPitch
{
    public const int Minimum = -32, Maximum = 2;
    public static int PressIndex(int variant, int steps) => steps == 0 ? variant : 12 + (steps - Minimum) * 8 + variant;
    public static byte[][] Build(byte[][] originals)
    {
        var bank = new byte[12 + (Maximum - Minimum + 1) * 8][];
        Array.Copy(originals, bank, 12);
        for (var steps = Minimum; steps <= Maximum; steps++)
            for (var i = 0; i < 8; i++) bank[12 + (steps - Minimum) * 8 + i] = Resample(originals[i], steps);
        return bank;
    }
    internal static byte[] Resample(byte[] wave, int steps)
    {
        if (steps < Minimum || steps > Maximum) throw new ArgumentOutOfRangeException(nameof(steps));
        if (steps == 0) return wave;
        var rate = Math.Pow(2, steps / 120d);
        var sourceFrames = (wave.Length - 44) / 2;
        var frames = (int)Math.Round(sourceFrames / rate);
        var result = new byte[44 + frames * 2];
        wave.AsSpan(0, 44).CopyTo(result);
        BitConverter.GetBytes(result.Length - 8).CopyTo(result, 4);
        BitConverter.GetBytes(frames * 2).CopyTo(result, 40);
        for (var i = 1; i < frames - 1; i++)
        {
            var position = Math.Min(sourceFrames - 1, i * rate);
            var a = (int)position;
            var b = Math.Min(sourceFrames - 1, a + 1);
            var first = BitConverter.ToInt16(wave, 44 + a * 2);
            var second = BitConverter.ToInt16(wave, 44 + b * 2);
            var value = (short)Math.Round(first + (second - first) * (position - a));
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(44 + i * 2), value);
        }
        return result;
    }
}
