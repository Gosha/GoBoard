using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using Xunit;

namespace GoBoard.Tests;

public sealed class PairedAudioTests
{
    private sealed class Player(byte[][] bank) : IPairedSamplePlayer
    {
        public byte[][] Bank = bank;
        public List<int> Played = [];
        public bool Disposed;
        public int Stops;
        public bool Play(int sample) { Played.Add(sample); return true; }
        public void Stop() => Stops++;
        public void Dispose() => Disposed = true;
    }
    [Fact]
    public void PairedPressesRetainTheirVariantAcrossInterleavedControllersAndCancel()
    {
        Player player = null;
        using var audio = new KeyAudio((_, _) => true, createPairs: bank => player = new Player(bank), randomPitchStep: () => 0);
        var settings = new BoardSettings { Sound = KeySound.GateronYellowPairs };
        audio.Apply(settings);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(KeyAudio.CreateClick(false, settings.Sound, variant: i), player.Bank[i]);
            Assert.Equal(KeyAudio.CreateClick(true, settings.Sound, variant: i), player.Bank[i + 4]);
            Assert.Contains(player.Bank[i + 4][44..], b => b != 0);
            Assert.Equal(player.Bank[i + 4][44..], player.Bank[i + 8][(44 + 17640)..]);
        }
        Assert.True(audio.Click(pointerId: 2));
        Assert.False(audio.Click(pointerId: 2)); // Held repeat does not consume a variant.
        Assert.True(audio.Click(pointerId: 3));
        Assert.True(audio.Click(true, 2));
        Assert.True(audio.Click(true, 3));
        Assert.False(audio.Click(true, 3));
        Assert.Equal(new[] { 0, 1, 4, 5 }, player.Played);
        audio.Apply(settings with { SizePercent = 125 });
        Assert.True(audio.Click(pointerId: 2));
        audio.Cancel(2);
        Assert.False(audio.Click(true, 2));
        Assert.True(audio.Preview());
        Assert.Equal(11, player.Played[^1]);
        Assert.True(audio.Click(pointerId: 3));
        audio.Cancel();
        Assert.Equal(1, player.Stops);
        Assert.False(audio.Click(true, 3));
        var old = player;
        audio.Apply(settings with { VolumePercent = 50 });
        Assert.True(old.Disposed);
        Assert.Equal(KeyAudio.CreateClick(false, settings.Sound, .5f), player.Bank[0]);
        audio.Apply(settings with { SoundEnabled = false });
        Assert.True(player.Disposed);
        Assert.False(audio.Click());
        Assert.False(audio.Click(true));
        Assert.False(audio.Preview());
    }
    [Fact]
    public void UnavailablePairedDeviceFallsBackWithoutOrphanedRelease()
    {
        using var audio = new KeyAudio((_, _) => true, createPairs: _ => throw new IOException("No device"));
        audio.Apply(new() { Sound = KeySound.GateronYellowPairs });
        Assert.True(audio.Click());
        Assert.False(audio.Click(true));
    }
    [Theory]
    [InlineData("Space", false, -29)]
    [InlineData("Enter", false, -12)]
    [InlineData("Enter", true, -16)]
    [InlineData("Backspace", false, 0)]
    [InlineData("Shift", false, -14)]
    [InlineData("RightShift", false, -14)]
    [InlineData("Shift", true, -7)]
    public void SizePitchAndJitterRemainPairedAcrossControllers(string id, bool iso, int expectedBase)
    {
        Player player = null;
        var draws = new Queue<int>([-2, 2, 0]);
        using var audio = new KeyAudio((_, _) => true, createPairs: bank => player = new Player(bank), randomPitchStep: () => draws.Dequeue());
        audio.Apply(new() { Sound = KeySound.GateronYellowPairs });
        var key = (iso ? KeyboardLayout.IsoKeys : KeyboardLayout.Keys).Single(k => k.Id == id);
        Assert.Equal(expectedBase, KeySounds.BasePitchSteps(key));
        Assert.True(audio.Click(pointerId: 3, key: key));
        Assert.False(audio.Click(pointerId: 3, key: key)); // Repeats must not draw a new jitter.
        Assert.True(audio.Click(pointerId: 4));
        Assert.True(audio.Click(true, 3));
        Assert.True(audio.Click(true, 4, key: key));
        Assert.True(audio.Click(pointerId: 4));
        var first = PairedPitch.PressIndex(0, expectedBase - 2);
        var second = PairedPitch.PressIndex(1, 2);
        Assert.Equal(new[] { first, second, first + 4, second + 4, 2 }, player.Played);
        Assert.Empty(draws);
        Assert.Equal(PairedPitch.Resample(player.Bank[0], expectedBase - 2), player.Bank[first]);
        Assert.Equal(PairedPitch.Resample(player.Bank[4], expectedBase - 2), player.Bank[first + 4]);
    }
    [Fact]
    public void PitchCachePreservesPcmBoundsFadesAndExpectedDuration()
    {
        var source = KeyAudio.CreateClick(false, KeySound.GateronYellowPairs);
        var sourceFrames = (source.Length - 44) / 2;
        var sourcePeak = Enumerable.Range(0, sourceFrames).Max(i => Math.Abs((int)BitConverter.ToInt16(source, 44 + i * 2)));
        for (var steps = PairedPitch.Minimum; steps <= PairedPitch.Maximum; steps++)
        {
            var shifted = PairedPitch.Resample(source, steps);
            Assert.Equal(shifted, SampledKeySounds.ScalePcm(shifted, 1));
            Assert.Equal((int)Math.Round(sourceFrames / Math.Pow(2, steps / 120d)), (shifted.Length - 44) / 2);
            Assert.Equal(0, BitConverter.ToInt16(shifted, 44));
            Assert.Equal(0, BitConverter.ToInt16(shifted, shifted.Length - 2));
            for (var i = 44; i < shifted.Length; i += 2) Assert.InRange(Math.Abs((int)BitConverter.ToInt16(shifted, i)), 0, sourcePeak);
        }
        Assert.Same(source, PairedPitch.Resample(source, 0));
        Assert.Equal(-30, KeySounds.BasePitchSteps(new("Space", "", 0x39, new(0, 0, 2000, 44))));
        Assert.Equal(0, KeySounds.BasePitchSteps(new("Tab", "", 0x0f, new(0, 0, 2000, 44))));
    }
    private sealed class Native : PairedSamplePlayer.INative
    {
        public Dictionary<nint, nint> Handles = [];
        public List<nint> Writes = [];
        public int Opens, Closed, Unprepared, Resets;
        public int FailOpen;
        public uint Open(out nint handle)
        {
            Opens++;
            if (Opens == FailOpen) { handle = 0; return 1; }
            handle = Opens; Handles.Add(handle, 0); return 0;
        }
        public uint Prepare(nint handle, nint header)
        { Handles[handle] = header; Marshal.WriteInt32(header, PairedSamplePlayer.FlagsOffset, 2); return 0; }
        public uint Write(nint handle, nint header)
        {
            Assert.Equal(0, Marshal.ReadInt32(header, PairedSamplePlayer.FlagsOffset) & 16);
            var value = Marshal.PtrToStructure<PairedSamplePlayer.Header>(header);
            Assert.InRange(value.Length, 1u, 26460u);
            _ = Marshal.ReadByte(value.Data, (int)value.Length - 1);
            Marshal.WriteInt32(header, PairedSamplePlayer.FlagsOffset, 18);
            Writes.Add(handle); return 0;
        }
        public uint Reset(nint handle)
        {
            Resets++;
            if (Handles[handle] != 0) Marshal.WriteInt32(Handles[handle], PairedSamplePlayer.FlagsOffset, 2);
            return 0;
        }
        public uint Unprepare(nint handle, nint header)
        {
            Assert.Equal(0, Marshal.ReadInt32(header, PairedSamplePlayer.FlagsOffset) & 16);
            _ = Marshal.ReadByte(Marshal.PtrToStructure<PairedSamplePlayer.Header>(header).Data);
            Unprepared++; Handles[handle] = 0; return 0;
        }
        public uint Close(nint handle) { Assert.Equal(0, Handles[handle]); Handles.Remove(handle); Closed++; return 0; }
    }
    [Fact]
    public void NativeVoicesOverlapReplaceOldestAndReturnBuffersBeforeFreeing()
    {
        var native = new Native();
        var player = new PairedSamplePlayer([KeyAudio.CreateClick(false, KeySound.GateronYellowPairs)], native);
        for (var i = 0; i < 8; i++) Assert.True(player.Play(0));
        Assert.Equal(8, native.Writes.Distinct().Count());
        Assert.Equal(0, native.Resets);
        Assert.True(player.Play(0));
        Assert.Equal(native.Writes[0], native.Writes[^1]);
        Assert.Equal(1, native.Resets);
        player.Stop();
        Assert.Equal(9, native.Resets);
        player.Dispose();player.Dispose();
        Assert.Equal(8, native.Unprepared);Assert.Equal(8, native.Closed);Assert.Empty(native.Handles);
        Assert.False(player.Play(0));
    }
    [Fact]
    public void PartialNativeInitializationCleansUpEarlierVoices()
    {
        var native = new Native { FailOpen = 3 };
        Assert.Throws<IOException>(() => new PairedSamplePlayer([KeyAudio.CreateClick(false, KeySound.GateronYellowPairs)], native));
        Assert.Equal(2, native.Unprepared);Assert.Equal(2, native.Closed);Assert.Empty(native.Handles);
    }
}
