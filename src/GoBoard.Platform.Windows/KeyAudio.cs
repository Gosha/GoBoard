using System.Runtime.InteropServices;
using System.Text;
using GoBoard.Core;

namespace GoBoard.Platform.Windows;

// Called on the host's input/UI thread. Async playback keeps the VR loop moving;
// every pinned WAVE buffer stays alive until this process's playback is stopped.
internal sealed class KeyAudio : IDisposable
{
    private GCHandle[] downWaves = [];
    private GCHandle upWave;
    private int nextVariant;
    private bool warned, warnedSample, disposed;
    private BoardSettings settings;
    private readonly Func<nint, uint, bool> play;
    private readonly Func<string, Stream> openSample;
    private readonly Func<byte[][], IPairedSamplePlayer> createPairs;
    private readonly Func<int> randomPitchStep;
    private IPairedSamplePlayer pairs;
    private readonly Dictionary<uint, int> heldPairs = new(64);

    public KeyAudio(Func<nint, uint, bool> playback = null, Func<string, Stream> openSample = null,
        Func<byte[][], IPairedSamplePlayer> createPairs = null, Func<int> randomPitchStep = null)
    {
        play = playback ?? ((wave, flags) => PlaySound(wave, 0, flags));
        this.openSample = openSample;
        this.createPairs = createPairs ?? (waves => new PairedSamplePlayer(waves));
        this.randomPitchStep = randomPitchStep ?? (() => Random.Shared.Next(-2, 3));
        Apply(BoardSettings.Defaults);
    }

    public void Apply(BoardSettings next)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        next = next.Normalize();
        if (settings != null && settings.Sound == next.Sound && settings.SoundEnabled == next.SoundEnabled &&
            settings.VolumePercent == next.VolumePercent) return;
        ReleaseBuffers();
        settings = null;
        nextVariant = 0;
        if (!next.SoundEnabled || next.VolumePercent == 0) { settings = next; return; }
        var volume = next.VolumePercent / 100f;
        var sampled = KeySounds.IsSampled(next.Sound);
        byte[][] waves;
        if (sampled)
        {
            try
            {
                if (KeySounds.HasPairedRelease(next.Sound))
                {
                    var bank = new byte[12][];
                    for (var i = 0; i < 4; i++)
                    {
                        bank[i] = SampledKeySounds.Load(next.Sound, i, volume, openSample);
                        bank[i + 4] = SampledKeySounds.Load(next.Sound, i, volume, openSample, released: true);
                        bank[i + 8] = PairPreview(bank[i], bank[i + 4]);
                    }
                    pairs = createPairs(next.Sound == KeySound.CherryMxBlue ? bank : PairedPitch.Build(bank));
                    settings = next;
                    return;
                }
                waves = new byte[SampledKeySounds.Variants][];
                for (var i = 0; i < waves.Length; i++)
                    waves[i] = SampledKeySounds.Load(next.Sound, i, volume, openSample);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // Load the whole bank before pinning; a partial load cannot leak handles.
                if (!warnedSample)
                {
                    warnedSample = true;
                    Console.Error.WriteLine($"Keyboard samples unavailable; using Cushioned wood: {ex.Message}");
                }
                waves = [CreateClick(false, KeySound.CushionedWood, volume)];
            }
        }
        else waves = [CreateClick(false, next.Sound, volume)];
        try
        {
            downWaves = new GCHandle[waves.Length];
            for (var i = 0; i < waves.Length; i++) downWaves[i] = GCHandle.Alloc(waves[i], GCHandleType.Pinned);
            // Recorded cuts can contain both edges: key-up must not play or stop audio.
            if (!sampled) upWave = GCHandle.Alloc(CreateClick(true, next.Sound, volume), GCHandleType.Pinned);
            settings = next;
        }
        catch { ReleaseBuffers(); throw; }
    }

    public bool Click(bool released = false, uint pointerId = 0, KeyboardKey key = null)
    {
        if (disposed) return false;
        if (pairs != null)
        {
            if (released) return heldPairs.Remove(pointerId, out var variant) && pairs.Play(variant + 4);
            if (heldPairs.ContainsKey(pointerId)) return false;
            var blue = settings.Sound == KeySound.CherryMxBlue;
            var large = blue && KeySounds.UsesLargeKeySound(key);
            var pitch = blue ? 0 : Math.Clamp(KeySounds.BasePitchSteps(key) + Math.Clamp(randomPitchStep(), -2, 2), PairedPitch.Minimum, PairedPitch.Maximum);
            var selected = large ? 3 : PairedPitch.PressIndex(nextVariant, pitch);
            heldPairs.Add(pointerId, selected);
            if (!large) nextVariant = (nextVariant + 1) % (blue ? 3 : 4);
            return pairs.Play(selected);
        }
        if (downWaves.Length == 0) return false;
        var wave = released ? upWave : downWaves[nextVariant];
        if (!wave.IsAllocated) return false;
        if (!released) nextVariant = (nextVariant + 1) % downWaves.Length;
        // ASYNC | NODEFAULT | MEMORY. Rapid presses replace the previous click
        // rather than building an audio queue. Other applications are unaffected.
        var played = play(wave.AddrOfPinnedObject(), 0x0001 | 0x0002 | 0x0004);
        if (!played && !warned)
        {
            warned = true;
            Console.Error.WriteLine("Keyboard click audio unavailable; typing remains enabled.");
        }
        return played;
    }

    internal static byte[] CreateClick(bool released, KeySound sound = KeySound.CushionedWood, float volume = 1, int variant = 0)
    {
        if (KeySounds.IsSampled(sound)) return released && !KeySounds.HasPairedRelease(sound) ? [] : SampledKeySounds.Load(sound, variant, volume, released: released);
        volume = float.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 0;
        const int rate = 48000;
        var samples = released ? 1680 : 2880; // 35/60 ms, mono 16-bit PCM.
        var thud = sound == KeySound.SoftLowThud;
        var values = new double[samples];
        var noise = new Random(17);
        var alpha = 1 - Math.Exp(-2 * Math.PI * (thud ? 260 : 520) / rate);
        var attackTime = thud ? .0035 : .0028;
        double low = 0, smooth = 0, dc = 0, energy = 0, peak = 0;
        for (var i = 0; i < samples; i++)
        {
            var t = i / (double)rate;
            low += alpha * (noise.NextDouble() * 2 - 1 - low);
            smooth += alpha * (low - smooth);
            dc += .008 * (smooth - dc);
            var attack = Math.Pow(Math.Sin(Math.Min(1, t / attackTime) * Math.PI / 2), 2);
            var tail = Math.Pow(Math.Sin(Math.Min(1, (samples - 1 - i) / (rate * .006)) * Math.PI / 2), 2);
            var pitch = thud ? (released ? 220 : 170) : (released ? 380 : 290);
            var value = attack * tail * (
                (thud ? .28 : .40) * (smooth - dc) * Math.Exp(-t / (thud ? .0045 : .005))
                + (thud ? .30 : .24) * Math.Sin(2 * Math.PI * pitch * t) * Math.Exp(-t / (thud ? .003 : .0028))
                + (thud ? .035 : .012) * Math.Sin(2 * Math.PI * (thud ? 340 : 620) * t) * Math.Exp(-t / .0018));
            values[i] = value;
            energy += value * value;
            peak = Math.Max(peak, Math.Abs(value));
        }
        // Match the audition's signal energy, with a quieter release and peak cap.
        var targetEnergy = released ? .2147025504138874 : .6197242718860959;
        var level = (thud ? .68 : .78) * (released ? .65 : 1);
        var gain = Math.Min(Math.Sqrt(targetEnergy / energy) * level, .16 / peak) * Math.Clamp(volume, 0, 1);
        using var stream = new MemoryStream(44 + samples * 2);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVE"u8);
        writer.Write("fmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        foreach (var value in values)
            writer.Write((short)Math.Round(value * gain * short.MaxValue));
        writer.Flush();
        return stream.ToArray();
    }

    public void Dispose()
    {
        if (disposed) return;
        ReleaseBuffers();
        disposed = true;
    }

    private void ReleaseBuffers()
    {
        heldPairs.Clear();
        pairs?.Dispose();
        pairs = null;
        if (downWaves.Length == 0 && !upWave.IsAllocated) return;
        play(0, 0); // Synchronous stop before releasing any buffer referenced by native playback.
        foreach (var wave in downWaves) if (wave.IsAllocated) wave.Free();
        downWaves = [];
        if (upWave.IsAllocated) upWave.Free();
    }

    public bool Preview()
    {
        if (disposed) return false;
        if (pairs == null) return Click();
        var variant = nextVariant;
        nextVariant = (nextVariant + 1) % (settings.Sound == KeySound.CherryMxBlue ? 3 : 4);
        return pairs.Play(variant + 8);
    }

    public void Cancel(uint? pointerId = null)
    {
        if (pointerId.HasValue) heldPairs.Remove(pointerId.Value);
        else { heldPairs.Clear(); pairs?.Stop(); }
    }

    private static byte[] PairPreview(byte[] press, byte[] release)
    {
        const int releaseOffset = 44100 * 2 / 5; // Saved preview hold: 200 ms.
        var length = Math.Max(press.Length - 44, releaseOffset + release.Length - 44);
        var wave = new byte[44 + length];
        press.AsSpan(0, 44).CopyTo(wave);
        BitConverter.GetBytes(wave.Length - 8).CopyTo(wave, 4);
        BitConverter.GetBytes(length).CopyTo(wave, 40);
        press.AsSpan(44).CopyTo(wave.AsSpan(44));
        release.AsSpan(44).CopyTo(wave.AsSpan(44 + releaseOffset));
        return wave;
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(nint sound, nint module, uint flags);
}
