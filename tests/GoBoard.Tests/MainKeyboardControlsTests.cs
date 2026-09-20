using GoBoard.Core;
using GoBoard.Presentation.Skia;
using Xunit;

namespace GoBoard.Tests;

public sealed class MainKeyboardControlsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsToggleChangesOnlyFloatingButtonVisibility(bool numpad)
    {
        var settings = new BoardSettings { NumpadEnabled = numpad, SizePercent = 125 };
        var hidden = SettingsControls.Apply(SettingsAction.ToggleNumpadButton, settings);
        Assert.Equal(settings with { NumpadButtonEnabled = false }, hidden);
        Assert.False(MainKeyboardControls.Visible(KeyboardAction.ToggleNumpad, hidden));
        Assert.True(MainKeyboardControls.Visible(KeyboardAction.ResetPosition, hidden));
        var keypadChanged = MainKeyboardControls.Apply(KeyboardAction.ToggleNumpad, hidden);
        Assert.Equal(hidden with { NumpadEnabled = !numpad }, keypadChanged);
        Assert.Equal(settings, SettingsControls.Apply(SettingsAction.ToggleNumpadButton, hidden));
        Assert.Contains(SettingsControls.All, c => c.Action == SettingsAction.ToggleNumpadButton);
        Assert.DoesNotContain(SettingsControls.All, c => c.Action == SettingsAction.ToggleNumpad);
    }

    [Fact]
    public void ExistingSettingsKeepButtonVisibleAndNewPreferencePersistsIndependently()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GoBoard-button-setting-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"NumpadEnabled\":true}");
            var settings = new SettingsStore(path);
            Assert.True(settings.Current.NumpadEnabled);
            Assert.True(settings.Current.NumpadButtonEnabled);
            Assert.True(settings.Update(s => SettingsControls.Apply(SettingsAction.ToggleNumpadButton, s)));
            var other = new SettingsStore(path);
            Assert.False(other.Current.NumpadButtonEnabled);
            Assert.True(other.Update(s => MainKeyboardControls.Apply(KeyboardAction.ToggleNumpad, s)));
            Assert.True(settings.Reload());
            Assert.False(settings.Current.NumpadEnabled);
            Assert.False(settings.Current.NumpadButtonEnabled);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NumpadActionMergesOnlyItsSetting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GoBoard-main-controls-{Guid.NewGuid():N}.json");
        try
        {
            var main = new SettingsStore(path);
            var other = new SettingsStore(path);
            var shortcuts = new ProgrammableKeySettings { Enabled = true, Key1 = new(0x4f) };
            var resetId = Guid.NewGuid();
            Assert.True(other.Update(s => s with { Theme = BoardThemes.SteamFlat, ProgrammableKeys = shortcuts, PositionResetId = resetId }));
            foreach (var expected in new[] { true, false })
            {
                Assert.True(main.Update(s => MainKeyboardControls.Apply(KeyboardAction.ToggleNumpad, s)));
                var saved = new SettingsStore(path).Current;
                Assert.Equal(expected, saved.NumpadEnabled);
                Assert.Equal(shortcuts, saved.ProgrammableKeys);
                Assert.Equal(BoardThemes.SteamFlat, saved.Theme);
                Assert.Equal(resetId, saved.PositionResetId);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(.5f, false)] [InlineData(1f, false)] [InlineData(1.5f, false)]
    [InlineData(.5f, true)] [InlineData(1f, true)] [InlineData(1.5f, true)]
    public void VrTargetsClearKeyboardHandleAndResizeGrip(float scale, bool numpad)
    {
        var num = MainKeyboardControls.Offset(KeyboardAction.ToggleNumpad, numpad, scale);
        var numSize = MainKeyboardControls.WidthInMeters(KeyboardAction.ToggleNumpad, scale);
        Assert.True(num.M41 - numSize / 2 > OverlayGeometry.WidthInMeters(numpad) * scale / 2);
        Assert.Equal(OverlayGeometry.PanelHeightInMeters * scale / 2, num.M42 + numSize / 2, 5);
        var reset = MainKeyboardControls.Offset(KeyboardAction.ResetPosition, numpad, scale);
        var size = MainKeyboardControls.WidthInMeters(KeyboardAction.ResetPosition, scale);
        Assert.True(reset.M41 - size / 2 > OverlayGeometry.GrabWidthInMeters / 2);
        Assert.True(reset.M42 + size / 2 < -OverlayGeometry.PanelHeightInMeters * scale / 2);
        var resize = OverlayGeometry.ResizeFromScaledPanel(scale, numpad);
        Assert.True(reset.M41 + size / 2 < resize.M41 - OverlayGeometry.ResizeSizeInMeters / 2);
    }

    [Theory]
    [InlineData(.5f)] [InlineData(1f)] [InlineData(1.5f)] [InlineData(3f)]
    public void DesktopControlsClearKeyboardAndFollowItsWidth(float scale)
    {
        var width = OverlayGeometry.Width(true) * scale;
        var num = MainKeyboardControls.DesktopBounds(KeyboardAction.ToggleNumpad, width, 42 * scale, scale);
        var reset = MainKeyboardControls.DesktopBounds(KeyboardAction.ResetPosition, width, 42 * scale, scale);
        Assert.True(num.X > width);
        Assert.True(reset.X + reset.Width < 0);
        Assert.Equal(42 * scale, num.Y);
        Assert.Equal(21 * scale, reset.Y + reset.Height / 2);
    }

    [Fact]
    public void FloatingControlsRequireFreshOwnedReleaseAndCancelOffTarget()
    {
        var state = new FloatingButtonState();
        bool Event(uint device, double time, bool down = false, bool up = false, bool leave = false) =>
            state.Process(device, device, 22, 22, time, time, down, up, leave);
        Assert.False(Event(7, 1, down: true));
        Assert.True(state.Pressed);
        Assert.False(Event(8, 1.1, up: true));
        Assert.Equal(7u, state.CapturedDevice);
        Assert.True(Event(7, 1.2, up: true));
        Assert.False(Event(7, 1.3, up: true));
        Assert.False(Event(7, 2, down: true));
        Assert.False(Event(7, 2.1, leave: true));
        Assert.False(Event(7, 2.2, up: true));
        Assert.False(Event(7, 3, down: true));
        state.Reset(3.1); // Hide, geometry change, tracking loss, grab or focus change.
        Assert.False(Event(7, 3.2, up: true));
        Assert.False(state.Process(7, 7, 22, 22, 3.05, 3.2, down: true));
        Assert.False(state.Process(7, 7, 22, 22, double.NaN, 4, down: true));
        Assert.False(state.Process(7, 7, 22, 22, 3, 4, down: true));
        Assert.False(state.Pressed);
        Assert.False(Event(7, 5, down: true));
        Assert.False(state.Process(7, 7, -1, 22, 5.1, 5.1, up: true));
        Assert.False(state.Pressed);
    }

    [Theory]
    [InlineData(BoardThemes.SteamSoft)]
    [InlineData(BoardThemes.SteamFlat)]
    public void ResetIsOnlyADownArrowWithTransparentBackgroundInEveryState(string theme)
    {
        foreach (var state in new[] { (false, false), (true, false), (true, true) })
        {
            using var bitmap = MainKeyboardControlRenderer.Render(KeyboardAction.ResetPosition, false, state.Item1, state.Item2, theme);
            Assert.Equal(132, bitmap.Width);
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (x < 29 || x > 102 || y < 23 || y > 107) Assert.Equal(0, bitmap.GetPixel(x, y).Alpha);
            Assert.True(bitmap.GetPixel(66, 92).Alpha > 0);
            Assert.Equal(0, bitmap.GetPixel(36, 36).Alpha);
        }
    }
}
