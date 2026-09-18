using System.Runtime.InteropServices;
using System.Text;
using GoBoard.Core;

namespace GoBoard.Platform.Windows;

// The auditioned "Cushioned wood" sound, with "Soft low thud" retained.
// Async playback keeps the VR
// loop moving; the pinned WAVE buffer stays alive until playback is stopped.
internal sealed class KeyAudio : IDisposable
{
    private GCHandle downWave;
    private GCHandle upWave;
    private bool warned;
    private BoardSettings settings;

    public KeyAudio()
    {
        Apply(BoardSettings.Defaults);
    }

    public void Apply(BoardSettings next)
    {
        next = next.Normalize();
        if (settings != null && settings.Sound == next.Sound && settings.SoundEnabled == next.SoundEnabled &&
            settings.VolumePercent == next.VolumePercent) return;
        Dispose(); // Stop playback before replacing pinned wave data.
        settings = next;
        if (!next.SoundEnabled || next.VolumePercent == 0) return;
        downWave = GCHandle.Alloc(CreateClick(false, next.Sound, next.VolumePercent / 100f), GCHandleType.Pinned);
        upWave = GCHandle.Alloc(CreateClick(true, next.Sound, next.VolumePercent / 100f), GCHandleType.Pinned);
    }

    public bool Click(bool released = false)
    {
        var wave = released ? upWave : downWave;
        if (!wave.IsAllocated) return false;
        // ASYNC | NODEFAULT | MEMORY. Rapid presses replace the previous click
        // rather than building an audio queue. Other applications are unaffected.
        var played = PlaySound(wave.AddrOfPinnedObject(), 0, 0x0001 | 0x0002 | 0x0004);
        if (!played && !warned)
        {
            warned = true;
            Console.Error.WriteLine("Keyboard click audio unavailable; typing remains enabled.");
        }
        return played;
    }

    internal static byte[] CreateClick(bool released, KeySound sound = KeySound.CushionedWood, float volume = 1)
    {
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
        if (!downWave.IsAllocated && !upWave.IsAllocated) return;
        PlaySound(0, 0, 0); // Stop this process's playback before unpinning.
        if (downWave.IsAllocated) downWave.Free();
        if (upWave.IsAllocated) upWave.Free();
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(nint sound, nint module, uint flags);
}
