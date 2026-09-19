using GoBoard.Core;
using GoBoard.Platform.Windows;
using Xunit;

namespace GoBoard.Tests;

public sealed class CherryBlueAudioTests
{
    private sealed class Player(byte[][] bank) : IPairedSamplePlayer
    {
        public byte[][] Bank = bank;
        public List<int> Played = [];
        public bool Play(int index) { Played.Add(index); return true; }
        public void Stop() { }
        public void Dispose() { }
    }
    [Theory]
    [InlineData("Space")]
    [InlineData("Enter")]
    [InlineData("Backspace")]
    [InlineData("Shift")]
    [InlineData("RightShift")]
    public void B07IsReservedForLargeKeysAndReleaseKeepsItsPair(string id)
    {
        Player player = null;
        using var audio = new KeyAudio((_, _) => true, createPairs: bank => player = new Player(bank),
            randomPitchStep: () => throw new Exception("Blue must preserve exported pitch"));
        audio.Apply(new() { Sound = KeySound.CherryMxBlue });
        Assert.Equal(12, player.Bank.Length);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(KeyAudio.CreateClick(false, KeySound.CherryMxBlue, variant: i), player.Bank[i]);
            Assert.Equal(KeyAudio.CreateClick(true, KeySound.CherryMxBlue, variant: i), player.Bank[i + 4]);
            Assert.Equal(player.Bank[i + 8], SampledKeySounds.ScalePcm(player.Bank[i + 8], 1));
        }
        var key = KeyboardLayout.Keys.Single(k => k.Id == id);
        Assert.True(audio.Click(pointerId: 1)); // BO3
        Assert.True(audio.Click(pointerId: 2, key: key)); // B07
        Assert.False(audio.Click(pointerId: 2, key: key));
        Assert.True(audio.Click(true, 1, key: key));
        Assert.True(audio.Click(true, 2));
        Assert.True(audio.Click(pointerId: 1)); // B01, unaffected by the large key
        Assert.True(audio.Click(true, 1));
        Assert.True(audio.Click(pointerId: 1)); // B03
        Assert.True(audio.Click(true, 1));
        Assert.True(audio.Click(pointerId: 1)); // Wrap to BO3
        Assert.True(audio.Click(true, 1));
        Assert.Equal(new[] { 0, 3, 4, 7, 1, 5, 2, 6, 0, 4 }, player.Played);
        Assert.True(audio.Preview());
        Assert.Equal(9, player.Played[^1]);
        Assert.False(KeySounds.UsesLargeKeySound(KeyboardLayout.Keys.Single(k => k.Id == "Tab")));
    }
    [Theory]
    [InlineData("GateronYellowModified", "GateronYellowPairs", "gateron-yellow")]
    [InlineData("CherryMxClear", "CherryMxBlue", "cherry-mx-clear")]
    public void RetiredPresetMigratesAndIsAbsentFromSelectorAndResources(string retired, string replacement, string resourceFolder)
    {
        var path = Path.Combine(Path.GetTempPath(), $"GoBoard.Migrate.{Guid.NewGuid():N}.json");
        try
        {
            var oldSound = Enum.Parse<KeySound>(retired);
            var newSound = Enum.Parse<KeySound>(replacement);
            File.WriteAllText(path, $$"""{"Sound":"{{retired}}","VolumePercent":40,"SizePercent":125}""");
            var store = new SettingsStore(path);
            Assert.Equal(newSound, store.Current.Sound);
            Assert.Equal(40, store.Current.VolumePercent);
            Assert.Equal(125, store.Current.SizePercent);
            Assert.DoesNotContain(oldSound, KeySounds.All);
            Assert.Equal(KeySounds.Name(newSound), KeySounds.Name(store.Current.Sound));
            Assert.True(store.Update(s => s with { VolumePercent = 50 }));
            Assert.DoesNotContain(retired, File.ReadAllText(path));
            Assert.Equal(KeyAudio.CreateClick(true, newSound), KeyAudio.CreateClick(true, oldSound));
            Assert.DoesNotContain(typeof(KeyAudio).Assembly.GetManifestResourceNames(), n => n.StartsWith($"GoBoard.Audio.{resourceFolder}."));
        }
        finally { File.Delete(path); }
    }
}
