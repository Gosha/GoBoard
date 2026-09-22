using System.Runtime.InteropServices;
using System.Security.Cryptography;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using SkiaSharp;
using Xunit;

namespace GoBoard.Tests;

public sealed class KeyAudioTests
{
    [Fact]
    public void EveryRecordedVariantIsEmbeddedDistinctAndVolumeSafe()
    {
        var hashes = new HashSet<string>();
        foreach (var sound in KeySounds.All.Where(KeySounds.IsSampled))
        {
            foreach (var released in new[] { false, true })
            for (var variant = 0; variant < 4; variant++)
            {
                var full = KeyAudio.CreateClick(released, sound, variant: variant);
                var half = KeyAudio.CreateClick(released, sound, .5f, variant);
                var mute = KeyAudio.CreateClick(released, sound, 0, variant);
                Assert.True(hashes.Add(Convert.ToHexString(SHA256.HashData(full))));
                Assert.Equal(44100, BitConverter.ToInt32(full, 24));
                Assert.Equal(1, BitConverter.ToInt16(full, 22));
                Assert.Equal(16, BitConverter.ToInt16(full, 34));
                Assert.Equal(full.Length - 44, BitConverter.ToInt32(full, 40));
                Assert.InRange(full.Length, 2200, 26504);
                Assert.Equal(full[..44], half[..44]);
                Assert.Equal(full[..44], mute[..44]);
                Assert.Equal(0, BitConverter.ToInt16(full, 44));
                Assert.Equal(0, BitConverter.ToInt16(full, full.Length - 2));
                var peak = 0;
                for (var i = 44; i < full.Length; i += 2)
                {
                    var value = BitConverter.ToInt16(full, i);
                    peak = Math.Max(peak, Math.Abs((int)value));
                    Assert.InRange(Math.Abs(BitConverter.ToInt16(half, i) - value / 2d), 0, 1);
                    Assert.Equal(0, BitConverter.ToInt16(mute, i));
                }
                Assert.InRange(peak, 1, short.MaxValue);
            }
        }
        Assert.Equal(16, hashes.Count); // Four press/release pairs in each live recorded preset.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrCorruptBankFallsBackAfterPartialLoad(bool corrupt)
    {
        var loads = 0;
        var played = new List<byte[]>();
        using var audio = new KeyAudio((pointer, _) =>
        {
            if (pointer != 0) played.Add(CopyWave(pointer));
            return true;
        }, name => ++loads == 3
            ? (corrupt ? new MemoryStream(new byte[44]) : null)
            : typeof(KeyAudio).Assembly.GetManifestResourceStream(name));
        audio.Apply(new() { Sound = KeySound.CherryMxBlue, VolumePercent = 30 });
        Assert.Equal(3, loads);
        Assert.True(audio.Click());
        Assert.False(audio.Click(true));
        Assert.Single(played);
        Assert.Equal(KeyAudio.CreateClick(false, KeySound.CushionedWood, .3f), played[0]);
        // A subsequent synthesized setting still has its distinct release sound.
        audio.Apply(new() { Sound = KeySound.SoftLowThud });
        Assert.True(audio.Click(true));
        Assert.Equal(KeyAudio.CreateClick(true, KeySound.SoftLowThud), played[^1]);
    }

    [Fact]
    public void StopSeesLiveBufferBeforeDisposalAndFurtherPlaybackIsDisabled()
    {
        nint active = 0;
        byte[] lastPlayed = null;
        var stopped = 0;
        var audio = new KeyAudio((pointer, _) =>
        {
            if (pointer != 0) { active = pointer; lastPlayed = CopyWave(pointer); }
            else if (active != 0)
            {
                Assert.Equal(lastPlayed, CopyWave(active));
                active = 0;
                stopped++;
            }
            return true;
        });
        audio.Apply(new() { Sound = KeySound.CushionedWood });
        Assert.True(audio.Click());
        GC.Collect(); // Native pointer remains rooted and pinned while a tail is active.
        Assert.Equal(lastPlayed, CopyWave(active));
        audio.Apply(new() { Sound = KeySound.SoftLowThud });
        Assert.Equal(1, stopped);
        Assert.True(audio.Click());
        audio.Dispose();
        Assert.Equal(2, stopped);
        audio.Dispose();
        Assert.Equal(2, stopped);
        Assert.False(audio.Click());
        Assert.Throws<ObjectDisposedException>(() => audio.Apply(new()));
    }

    [Fact]
    public void PcmValidationRejectsTruncatedAndWrongFormatFilesAndPreservesExtraChunks()
    {
        var valid = KeyAudio.CreateClick(false, KeySound.CherryMxBlue);
        Assert.Throws<InvalidDataException>(() => SampledKeySounds.ScalePcm(valid[..^2], 1));
        var badFormat = (byte[])valid.Clone();
        badFormat[22] = 2; // Unsupported stereo.
        Assert.Throws<InvalidDataException>(() => SampledKeySounds.ScalePcm(badFormat, 1));
        var badSize = (byte[])valid.Clone();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(badSize, 40);
        Assert.Throws<InvalidDataException>(() => SampledKeySounds.ScalePcm(badSize, 1));
        // A padded metadata chunk before fmt/data must survive scaling untouched.
        byte[] junk = [.. "JUNK"u8.ToArray(), 1, 0, 0, 0, 42, 0];
        byte[] extended = [.. valid[..12], .. junk, .. valid[12..]];
        BitConverter.GetBytes(extended.Length - 8).CopyTo(extended, 4);
        var scaled = SampledKeySounds.ScalePcm(extended, .5f);
        Assert.Equal(extended[..54], scaled[..54]);
        Assert.Equal(SampledKeySounds.ScalePcm(valid, .5f)[44..], scaled[54..]);
        Assert.Equal(valid, SampledKeySounds.ScalePcm(valid, 2));
        Assert.Equal(SampledKeySounds.ScalePcm(valid, 0), SampledKeySounds.ScalePcm(valid, float.NaN));
    }

    [Fact]
    public void SoundSelectorPersistsAllPresetsAndPreservesEffects()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GoBoard.Audio.{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            var initial = new BoardSettings { Effects = new() { Spotlight = true, Transition = CharacterTransition.Lift } };
            Assert.True(store.Update(_ => initial));
            var seen = new HashSet<KeySound>();
            foreach (var (action, sound) in new[] { (SettingsAction.GateronYellow, KeySound.GateronYellowPairs),
                (SettingsAction.Thud, KeySound.SoftLowThud), (SettingsAction.CherryBlue, KeySound.CherryMxBlue),
                (SettingsAction.Wood, KeySound.CushionedWood) })
            {
                Assert.True(store.Update(s => SettingsControls.Apply(action, s)));
                Assert.Equal(sound, store.Current.Sound);
                Assert.True(seen.Add(store.Current.Sound));
                Assert.Equal(store.Current, new SettingsStore(path).Current);
                Assert.Equal(initial.Effects, store.Current.Effects);
                Assert.Contains($"\"{store.Current.Sound}\"", File.ReadAllText(path));
                Assert.Equal(store.Current, SettingsControls.Apply(action, store.Current));
                Assert.True(SettingsControls.AuditionsSound(action));
            }
            Assert.Equal(KeySound.CushionedWood, store.Current.Sound);
            Assert.Equal(KeySounds.All.Count, seen.Count);
            Assert.Equal(KeySound.SoftLowThud, SettingsControls.Apply(SettingsAction.Defaults, store.Current).Sound);
            Assert.False(SettingsControls.AuditionsSound(SettingsAction.Spotlight));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PresetNamesFitAndSettingsRenderInDesktopAndVr()
    {
        var buttons = SettingsControls.All.Where(c => SettingsControls.SoundFor(c.Action).HasValue).ToArray();
        Assert.Equal(KeySounds.All, buttons.Select(c => SettingsControls.SoundFor(c.Action).Value));
        using var face = SKTypeface.FromFamilyName("Segoe UI");
        using var font = new SKFont(face, 19);
        var output = Environment.GetEnvironmentVariable("GOBOARD_AUDIO_PREVIEW_DIR");
        foreach (var button in buttons)
            Assert.True(font.MeasureText(button.Label) <= button.Bounds.Width - 24);
        foreach (var sound in Enum.GetValues<KeySound>())
        {
            foreach (var desktop in new[] { false, true })
            foreach (var theme in new[] { BoardThemes.SteamSoft, BoardThemes.SteamFlat })
            {
                using var bitmap = SettingsPanel.Render(new() { Sound = sound, Theme = theme }, desktopMode: desktop);
                Assert.Equal(SettingsControls.Width * 2, bitmap.Width);
                Assert.Equal(SettingsControls.Height * 2, bitmap.Height);
                foreach (var button in buttons)
                {
                    var selected = SettingsControls.SoundFor(button.Action) == KeySounds.Canonical(sound);
                    Assert.Equal(selected ? new SKColor(0x1a, 0x9f, 0xff) : new SKColor(0x1b, 0x2c, 0x39),
                        bitmap.GetPixel((int)(button.Bounds.X + 12) * 2, (int)(button.Bounds.Y + 12) * 2));
                }
                if (output == null) continue;
                Directory.CreateDirectory(output);
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                var name = sound == KeySound.GateronYellowModified ? "sounds" : $"sounds-{sound}";
                if (theme == BoardThemes.SteamFlat) name += "-flat";
                using var file = File.Create(Path.Combine(output, $"{name}-{(desktop ? "desktop" : "vr")}.png"));
                encoded.SaveTo(file);
            }
        }
    }

    private static byte[] CopyWave(nint pointer)
    {
        var bytes = new byte[Marshal.ReadInt32(pointer, 4) + 8];
        Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return bytes;
    }
}
