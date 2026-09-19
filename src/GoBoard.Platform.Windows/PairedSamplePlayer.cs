using System.Runtime.InteropServices;

namespace GoBoard.Platform.Windows;

internal interface IPairedSamplePlayer : IDisposable
{
    bool Play(int sample);
    void Stop();
}

// Bounded overlapping voices for the auditioned pairs only. All native memory
// is allocated/prepared at settings changes, never on an input edge.
internal sealed class PairedSamplePlayer : IPairedSamplePlayer
{
    internal interface INative
    {
        uint Open(out nint handle);
        uint Prepare(nint handle, nint header);
        uint Write(nint handle, nint header);
        uint Reset(nint handle);
        uint Unprepare(nint handle, nint header);
        uint Close(nint handle);
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Header
    {
        public nint Data;
        public uint Length, Recorded;
        public nuint User;
        public uint Flags, Loops;
        public nint Next;
        public nuint Reserved;
    }
    private sealed class Voice
    {
        public nint Handle, Data, Header;
        public bool Prepared;
        public long Order;
    }
    private readonly Voice[] voices = new Voice[8];
    private readonly byte[][] samples;
    private readonly INative native;
    private long order;
    private bool disposed;
    internal static readonly int FlagsOffset = (int)Marshal.OffsetOf<Header>(nameof(Header.Flags));
    private static readonly int LengthOffset = (int)Marshal.OffsetOf<Header>(nameof(Header.Length));

    public PairedSamplePlayer(byte[][] waves, INative native = null)
    {
        this.native = native ?? new Native();
        // This bank is generated as canonical 44-byte-header PCM, unlike the
        // more general legacy loader. Refuse unexpected chunks before copying.
        samples = waves.Select(w =>
        {
            _ = SampledKeySounds.ScalePcm(w, 1);
            if (!w.AsSpan(36, 4).SequenceEqual("data"u8) || BitConverter.ToInt32(w, 40) != w.Length - 44)
                throw new InvalidDataException("Paired audio requires canonical PCM.");
            return w[44..];
        }).ToArray();
        var capacity = samples.Max(s => s.Length);
        try
        {
            for (var i = 0; i < voices.Length; i++)
            {
                var v = voices[i] = new Voice();
                Check(this.native.Open(out v.Handle));
                v.Data = Marshal.AllocHGlobal(capacity);
                v.Header = Marshal.AllocHGlobal(Marshal.SizeOf<Header>());
                Marshal.StructureToPtr(new Header { Data = v.Data, Length = (uint)capacity }, v.Header, false);
                Check(this.native.Prepare(v.Handle, v.Header));
                v.Prepared = true;
            }
        }
        catch { Dispose(); throw; }
    }
    private static bool Busy(Voice v) => (Marshal.ReadInt32(v.Header, FlagsOffset) & 0x10) != 0;
    private static void Check(uint result)
    {
        if (result != 0) throw new IOException($"Keyboard wave output failed ({result}).");
    }
    public bool Play(int sample)
    {
        if (disposed || (uint)sample >= samples.Length) return false;
        Voice chosen = null;
        foreach (var v in voices)
            if (!Busy(v)) { chosen = v; break; }
        if (chosen == null)
        {
            chosen = voices[0];
            foreach (var v in voices) if (v.Order < chosen.Order) chosen = v;
            // If all eight voices are busy, replace only the oldest tail.
            if (native.Reset(chosen.Handle) != 0 || Busy(chosen)) return false;
        }
        var data = samples[sample];
        Marshal.Copy(data, 0, chosen.Data, data.Length);
        Marshal.WriteInt32(chosen.Header, LengthOffset, data.Length);
        chosen.Order = ++order;
        return native.Write(chosen.Handle, chosen.Header) == 0;
    }
    public void Stop()
    {
        if (disposed) return;
        foreach (var v in voices) if (v != null && v.Handle != 0) native.Reset(v.Handle);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var v in voices)
        {
            if (v == null) continue;
            if (v.Handle != 0)
            {
                native.Reset(v.Handle);
                // Never free memory the driver still owns, even on device failure.
                // A failed unprepare retains its unmanaged block until process exit.
                if (v.Prepared && native.Unprepare(v.Handle, v.Header) != 0)
                {
                    Console.Error.WriteLine("Keyboard audio driver retained a buffer; leaving it allocated safely.");
                    continue;
                }
                if (native.Close(v.Handle) != 0) Console.Error.WriteLine("Keyboard audio device could not close.");
            }
            if (v.Header != 0) Marshal.FreeHGlobal(v.Header);
            if (v.Data != 0) Marshal.FreeHGlobal(v.Data);
        }
    }
    private sealed class Native : INative
    {
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct Format
        {
            public ushort Tag, Channels;
            public uint Rate, BytesPerSecond;
            public ushort Align, Bits, Extra;
        }
        public uint Open(out nint handle)
        {
            var format = new Format { Tag = 1, Channels = 1, Rate = 44100, BytesPerSecond = 88200, Align = 2, Bits = 16 };
            return waveOutOpen(out handle, uint.MaxValue, ref format, 0, 0, 0);
        }
        public uint Prepare(nint handle, nint header) => waveOutPrepareHeader(handle, header, (uint)Marshal.SizeOf<Header>());
        public uint Write(nint handle, nint header) => waveOutWrite(handle, header, (uint)Marshal.SizeOf<Header>());
        public uint Reset(nint handle) => waveOutReset(handle);
        public uint Unprepare(nint handle, nint header) => waveOutUnprepareHeader(handle, header, (uint)Marshal.SizeOf<Header>());
        public uint Close(nint handle) => waveOutClose(handle);
        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out nint handle, uint device, ref Format format, nint callback, nint instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(nint handle, nint header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutWrite(nint handle, nint header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutReset(nint handle);
        [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(nint handle, nint header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutClose(nint handle);
    }
}
