using System.Numerics;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using Xunit;

namespace GoBoard.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GoBoard.Settings.Tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(directory, "settings.json");

    [Fact]
    public void ThemeSelectionPersistsMergesAndResetsAcrossEditors()
    {
        var desktop = new SettingsStore(SettingsPath);
        var vr = new SettingsStore(SettingsPath);
        Assert.True(desktop.Update(s => SettingsControls.Apply(SettingsAction.SteamFlat, s)));
        Assert.True(vr.Update(s => s with { VolumePercent = 40 }));
        Assert.True(desktop.Reload());
        Assert.Equal(BoardThemes.SteamFlat, desktop.Current.Theme);
        Assert.Equal(40, desktop.Current.VolumePercent);
        Assert.Equal(desktop.Current, new SettingsStore(SettingsPath).Current);
        Assert.True(vr.Update(s => SettingsControls.Apply(SettingsAction.SteamSoft, s)));
        Assert.True(desktop.Reload());
        Assert.Equal(BoardThemes.SteamSoft, desktop.Current.Theme);
        Assert.Equal(BoardThemes.Default, SettingsControls.Apply(SettingsAction.Defaults, vr.Current).Theme);
    }

    [Theory]
    [InlineData("{\"SizePercent\":120}")]
    [InlineData("{\"SizePercent\":120,\"Theme\":\"future-theme\"}")]
    [InlineData("{\"SizePercent\":120,\"Theme\":null}")]
    public void MissingOrUnknownThemesFallBackWithoutLosingOtherSettings(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, json);
        var store = new SettingsStore(SettingsPath);
        Assert.Null(store.Error);
        Assert.Equal(120, store.Current.SizePercent);
        Assert.Equal(BoardThemes.Default, store.Current.Theme);
    }

    [Fact]
    public void DesktopAndVrEditsMergeWithLatestSavedValues()
    {
        var desktop = new SettingsStore(SettingsPath);
        var vr = new SettingsStore(SettingsPath);
        Assert.True(desktop.Update(s => s with { SizePercent = 125 }));
        Assert.True(vr.Update(s => s with { SoundEnabled = false, VolumePercent = 30, Sound = KeySound.SoftLowThud }));
        Assert.True(desktop.Reload());
        Assert.Equal(125, desktop.Current.SizePercent);
        Assert.False(desktop.Current.SoundEnabled);
        Assert.Equal(30, desktop.Current.VolumePercent);
        Assert.Equal(KeySound.SoftLowThud, desktop.Current.Sound);
        Assert.Equal(desktop.Current, new SettingsStore(SettingsPath).Current);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentEditorsDoNotLoseRelativeChanges()
    {
        var first = new SettingsStore(SettingsPath);
        var second = new SettingsStore(SettingsPath);
        Assert.True(first.Update(_ => new BoardSettings { SizePercent = 50 }));
        await Task.WhenAll(Task.Run(() => Increment(first)), Task.Run(() => Increment(second)));
        first.Reload();
        Assert.Equal(70, first.Current.SizePercent);
        static void Increment(SettingsStore store)
        {
            for (var i = 0; i < 10; i++) Assert.True(store.Update(s => s with { SizePercent = s.SizePercent + 1 }));
        }
    }

    [Fact]
    public void CorruptFilePreservesLastWorkingValuesAndIsNotOverwritten()
    {
        var store = new SettingsStore(SettingsPath);
        Assert.True(store.Update(s => s with { SizePercent = 120 }));
        File.WriteAllText(SettingsPath, "{broken");
        Assert.False(store.Reload());
        Assert.Equal(120, store.Current.SizePercent);
        Assert.NotNull(store.Error);
        Assert.False(store.Update(s => s with { SoundEnabled = false }));
        Assert.Equal("{broken", File.ReadAllText(SettingsPath));
        File.WriteAllText(SettingsPath, "{\"SizePercent\": 90}");
        Assert.True(store.Reload());
        Assert.Null(store.Error);
        Assert.Equal(90, store.Current.SizePercent);
    }

    [Fact]
    public void ValuesAreClampedAndSaveFailureDoesNotChangeAppliedSettings()
    {
        var store = new SettingsStore(SettingsPath);
        Assert.True(store.Update(_ => new BoardSettings { SizePercent = 500, VolumePercent = -5, Sound = (KeySound)99 }));
        Assert.Equal(150, store.Current.SizePercent);
        Assert.Equal(0, store.Current.VolumePercent);
        Assert.Equal(KeySound.CushionedWood, store.Current.Sound);
        // A parent that is a file reliably fails, including when tests run elevated.
        var blocked = new SettingsStore(Path.Combine(SettingsPath, "blocked.json"));
        var previous = blocked.Current;
        Assert.False(blocked.Update(s => s with { SizePercent = 60 }));
        Assert.Equal(previous, blocked.Current);
        Assert.NotNull(blocked.Error);
    }

    [Theory]
    [InlineData(.5f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void ResizingPreservesGrabTargetGapAndCenter(float scale)
    {
        var offset = OverlayGeometry.GrabFromScaledPanel(scale);
        var panelBottom = -OverlayGeometry.PanelHeightInMeters * scale / 2;
        var grabTop = offset.M42 + OverlayGeometry.GrabWidthInMeters * OverlayGeometry.GrabHeight / OverlayGeometry.GrabWidth / 2;
        Assert.InRange(panelBottom - grabTop, .00749f, .00751f);
        Assert.Equal(0, offset.M41);
        var world = Matrix4x4.CreateRotationX(.3f) * Matrix4x4.CreateTranslation(1, 2, 3);
        var handle = offset * world;
        Assert.True(Matrix4x4.Invert(world, out var inverse));
        Assert.InRange(Math.Abs((handle * inverse).M42 - offset.M42), 0, .00001f);
    }

    [Fact]
    public void SettingsRequireMatchingPressReleaseAndRejectStaleEdges()
    {
        var p = new SettingsPointerState();
        const float x = 750, y = 185; // Larger
        Assert.Null(p.Process(7, x, y, 1, 1, down: true));
        Assert.Null(p.Process(8, x, y, 1.01, 1.01, up: true));
        Assert.Equal(SettingsAction.Larger, p.Process(7, x, y, 1.02, 1.02, up: true));
        Assert.Null(p.Process(7, x, y, 1, 1.03, down: true)); // Late pre-release down.
        Assert.Null(p.Process(7, x, y, 1.04, 1.04, up: true));
        p.Process(7, x, y, 2, 2, down: true);
        p.Process(7, 0, 0, 2.01, 2.01); // Leave button, then return.
        Assert.Null(p.Process(7, x, y, 2.02, 2.02, up: true));
        p.Process(7, x, y, 3, 3, down: true);
        p.Reset(); // Hidden dashboard cancels pending actions.
        Assert.Null(p.Process(7, x, y, 3.01, 3.01, up: true));
        Assert.Null(p.Process(7, x, y, 4, 4.3, down: true));
        Assert.Null(p.Process(7, x, y, 4.31, 4.31, up: true));
        Assert.Null(SettingsControls.Hit(float.NaN, y));
    }

    [Fact]
    public void SoundVolumeScalesBothPressAndReleaseWithoutChangingWaveLength()
    {
        foreach (var sound in Enum.GetValues<KeySound>())
        foreach (var released in new[] { false, true })
        {
            var full = KeyAudio.CreateClick(released, sound);
            var half = KeyAudio.CreateClick(released, sound, .5f);
            var mute = KeyAudio.CreateClick(released, sound, 0);
            Assert.Equal(full.Length, half.Length);
            for (var i = 44; i < full.Length; i += 2)
            {
                Assert.InRange(Math.Abs(BitConverter.ToInt16(half, i) - BitConverter.ToInt16(full, i) / 2d), 0, 1);
                Assert.Equal(0, BitConverter.ToInt16(mute, i));
            }
        }
    }

    [Fact]
    public void DashboardControlsHaveValidHitTargetsAtEverySizeAndSetting()
    {
        foreach (var c in SettingsControls.All)
        {
            var b = c.Bounds;
            Assert.Equal(c.Action, SettingsControls.Hit(b.X + b.Width / 2, b.Y + b.Height / 2));
            Assert.InRange(b.X + b.Width, 0, SettingsControls.Width);
            Assert.InRange(b.Y + b.Height, 0, SettingsControls.Height);
        }
        foreach (var size in new[] { 50, 100, 150 })
        foreach (var enabled in new[] { true, false })
        {
            using var bitmap = SettingsPanel.Render(new BoardSettings { SizePercent = size, SoundEnabled = enabled });
            Assert.Equal(SettingsControls.Width * 2, bitmap.Width);
            Assert.Equal(SettingsControls.Height * 2, bitmap.Height);
        }
        Assert.Equal(50, SettingsControls.Apply(SettingsAction.Smaller, new BoardSettings { SizePercent = 50 }).SizePercent);
        Assert.Equal(150, SettingsControls.Apply(SettingsAction.Larger, new BoardSettings { SizePercent = 150 }).SizePercent);
    }

    [Theory]
    [InlineData(900, 650)]
    [InlineData(1400, 650)]
    [InlineData(560, 850)]
    [InlineData(1800, 1300)]
    public void DesktopSettingsTargetsFollowTheRenderedPanelAndExcludeMargins(int width, int height)
    {
        var viewport = SettingsViewport.Fit(width, height);
        foreach (var control in SettingsControls.All)
        {
            var b = control.Bounds;
            var point = viewport.ToPanel(viewport.X + (b.X + b.Width / 2) * viewport.Scale,
                viewport.Y + (b.Y + b.Height / 2) * viewport.Scale);
            var pointer = new SettingsPointerState();
            pointer.Process(0, point.X, point.Y, 1, 1, down: true);
            Assert.Equal(control.Action, pointer.Process(0, point.X, point.Y, 2, 2, up: true));
        }
        var outside = viewport.ToPanel(viewport.X - 1, viewport.Y + viewport.Height / 2);
        Assert.Null(SettingsControls.Hit(outside.X, outside.Y));
        outside = viewport.ToPanel(viewport.X + viewport.Width / 2, viewport.Y - 1);
        Assert.Null(SettingsControls.Hit(outside.X, outside.Y));
        Assert.True(float.IsNaN(SettingsViewport.Fit(0, 0).ToPanel(0, 0).X));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
