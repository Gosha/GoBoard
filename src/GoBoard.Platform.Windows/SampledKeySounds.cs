using System.Buffers.Binary;
using GoBoard.Core;

namespace GoBoard.Platform.Windows;

internal static class SampledKeySounds
{
    public const int Variants = 4;

    public static byte[] Load(KeySound sound, int variant, float volume, Func<string, Stream> openResource = null, bool released = false)
    {
        sound = KeySounds.Canonical(sound);
        var folder = sound switch
        {
            KeySound.CherryMxBlue => "cherry-blue-pairs",
            KeySound.GateronYellowPairs => "gateron-pairs",
            _ => throw new ArgumentOutOfRangeException(nameof(sound))
        };
        var count = sound == KeySound.GateronYellowPairs ? Variants + 1 : Variants;
        if (variant < 0 || variant >= count) throw new ArgumentOutOfRangeException(nameof(variant));
        var file = KeySounds.HasPairedRelease(sound) ? $"{(released ? "release" : "press")}-{variant + 1}" : $"key-{variant + 1:00}";
        var name = $"GoBoard.Audio.{folder}.{file}.wav";
        using var stream = openResource == null
            ? typeof(SampledKeySounds).Assembly.GetManifestResourceStream(name) : openResource(name);
        if (stream == null) throw new InvalidDataException($"Missing sound resource: {name}");
        using var reader = new BinaryReader(stream);
        var wave = reader.ReadBytes(65537); // Far above our longest cut, but bound a malformed resource.
        if (wave.Length > 65536) throw new InvalidDataException("Keyboard sample is too large.");
        return ScalePcm(wave, volume);
    }

    internal static byte[] ScalePcm(byte[] wave, float volume)
    {
        // Validate the container rather than assuming every WAV has a 44-byte header.
        if (wave.Length < 44 || !wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !wave.AsSpan(8, 4).SequenceEqual("WAVE"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(4)) != wave.Length - 8)
            throw new InvalidDataException("Invalid keyboard WAV container.");
        var formatFound = false;
        var dataOffset = -1;
        var dataLength = 0;
        var offset = 12;
        while (offset < wave.Length)
        {
            if (wave.Length - offset < 8) throw new InvalidDataException("Truncated WAV chunk.");
            var size = BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(offset + 4));
            var padded = (long)size + (size & 1);
            var start = offset + 8;
            if (padded > wave.Length - start) throw new InvalidDataException("Truncated WAV data.");
            var tag = wave.AsSpan(offset, 4);
            if (tag.SequenceEqual("fmt "u8))
            {
                if (formatFound || size < 16 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(start)) != 1 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(start + 2)) != 1 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(start + 4)) != 44100 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(start + 8)) != 88200 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(start + 12)) != 2 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(start + 14)) != 16)
                    throw new InvalidDataException("Keyboard samples must be mono 44.1 kHz 16-bit PCM.");
                formatFound = true;
            }
            else if (tag.SequenceEqual("data"u8))
            {
                if (dataOffset >= 0 || size == 0 || size % 2 != 0 || size > 44100) // At most 500 ms, including paired previews.
                    throw new InvalidDataException("Invalid keyboard sample length.");
                dataOffset = start;
                dataLength = (int)size;
            }
            offset = start + (int)padded;
        }
        if (!formatFound || dataOffset < 0) throw new InvalidDataException("Missing WAV format or data.");
        var scaled = (byte[])wave.Clone();
        var gain = float.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 0;
        for (var i = dataOffset; i < dataOffset + dataLength; i += 2)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(i));
            var value = (short)Math.Clamp(Math.Round(sample * (double)gain), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(scaled.AsSpan(i), value);
        }
        return scaled;
    }
}
