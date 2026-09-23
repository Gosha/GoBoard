using GoBoard.Core;
using GoBoard.Presentation.Skia;
using Xunit;

namespace GoBoard.Tests;

public sealed class InactivityTests
{
    private static readonly InactivitySettings Hide = new() { Mode = InactivityMode.Hide };

    [Fact]
    public void IdleStartsOnLeaveAndStationaryHoverKeepsKeyboardOpen()
    {
        var state = new KeyboardInactivity();
        state.Update(Hide, 0, true, true, false);
        state.Update(Hide, 60, true, true, false);
        Assert.False(state.Dormant);
        state.Update(Hide, 61, true, false, false);
        state.Update(Hide, 61.9, true, false, false);
        Assert.False(state.Dormant);
        state.Update(Hide, 61.95, true, true, false);
        state.Update(Hide, 62, true, false, false);
        state.Update(Hide, 63, true, false, false);
        Assert.True(state.Dormant);
    }

    [Fact]
    public void RevealRequiresContinuousHoverAndRestoresImmediatelyAfterDwell()
    {
        var state = new KeyboardInactivity();
        state.Update(Hide, 0, true, false, false);
        state.Update(Hide, 1, true, false, false);
        state.Update(Hide, 1.1, true, false, true);
        state.Update(Hide, 1.2, true, false, false);
        state.Update(Hide, 2, true, false, true);
        state.Update(Hide, 2.19, true, false, true);
        Assert.True(state.Dormant);
        state.Update(Hide, 2.2, true, false, true);
        Assert.False(state.Dormant);
        Assert.Equal(1, state.Opacity);
        Assert.Equal(1, state.Scale);
        // Give the hand a full leave delay to cross from the handle to the keys.
        state.Update(Hide, 2.21, true, false, false);
        state.Update(Hide, 3.2, true, false, false);
        Assert.False(state.Dormant);
    }

    [Theory]
    [InlineData("Hide", 0, 1)]
    [InlineData("Minimize", 1, .2f)]
    [InlineData("Transparent", .15f, 1)]
    public void ModesReachDistinctVisualTargetsWithoutChangingSavedSettings(string mode, float alpha, float scale)
    {
        var settings = Hide with { Mode = Enum.Parse<InactivityMode>(mode) };
        var state = new KeyboardInactivity();
        state.Update(settings, 0, true, false, false);
        state.Update(settings, 1, true, false, false);
        Assert.True(state.Dormant); // Input stops at the start, not after the fade.
        state.Update(settings, 1.1, true, false, false);
        Assert.InRange(state.Progress, .499f, .501f);
        state.Update(settings, 1.3, true, false, false);
        Assert.Equal(alpha, state.Opacity, 5);
        Assert.Equal(scale, state.Scale, 5);
        Assert.Equal(20, settings.SizePercent);
    }

    [Fact]
    public void SettingsEditsAndManipulationRestoreTheKeyboard()
    {
        var state = new KeyboardInactivity();
        state.Update(Hide, 0, true, false, false);
        state.Update(Hide, 1, true, false, false);
        Assert.True(state.Dormant);
        state.Update(Hide, 12, true, false, false, manipulating: true);
        Assert.False(state.Dormant);
        state.Update(Hide, 100, true, false, false, manipulating: true);
        Assert.False(state.Dormant);
        state.Update(Hide, 101, true, false, false);
        state.Update(Hide, 102, true, false, false);
        Assert.True(state.Dormant);
        var changed = Hide with { Mode = InactivityMode.Off };
        state.Update(changed, 103, true, false, false);
        state.Update(changed, 1000, true, false, false);
        Assert.False(state.Dormant);
    }

    [Theory]
    [InlineData("Hide")]
    [InlineData("Minimize")]
    [InlineData("Transparent")]
    public void DashboardReopenPreservesIdleModeEvenDuringTransition(string mode)
    {
        var settings = Hide with { Mode = Enum.Parse<InactivityMode>(mode) };
        var state = new KeyboardInactivity();
        state.Update(settings, 0, true, false, false);
        state.Update(settings, 1, true, false, false);
        Assert.True(state.Dormant);
        Assert.Equal(0, state.Progress);
        state.Update(settings, 1.01, false, true, true, manipulating: true);
        Assert.True(state.Dormant);
        Assert.Equal(1, state.Progress);
        var opacity = state.Opacity;
        var scale = state.Scale;
        // A quick reopen must not replay the unfinished animation or stale hover.
        state.Update(settings, 1.02, true, false, false);
        Assert.True(state.Dormant);
        Assert.Equal(opacity, state.Opacity);
        Assert.Equal(scale, state.Scale);
        state.Update(settings, 2, false, false, false);
        state.Update(settings, 60, false, false, false);
        state.Update(settings, 61, true, false, false);
        Assert.True(state.Dormant);
        Assert.Equal(1, state.Progress);
    }

    [Fact]
    public void ClosingDashboardResetsRevealDwellWithoutWakingKeyboard()
    {
        var state = new KeyboardInactivity();
        state.Update(Hide, 0, true, false, false);
        state.Update(Hide, 1, true, false, false);
        state.Update(Hide, 2, true, false, true);
        state.Update(Hide, 2.15, false, false, true);
        state.Update(Hide, 60, false, false, true);
        state.Update(Hide, 61, true, false, true);
        state.Update(Hide, 61.19, true, false, true);
        Assert.True(state.Dormant);
        state.Update(Hide, 61.2, true, false, true);
        Assert.False(state.Dormant);
    }

    [Fact]
    public void AwakeKeyboardReopensAwakeWithFreshInactivityDelay()
    {
        var state = new KeyboardInactivity();
        state.Update(Hide, 0, true, false, false);
        state.Update(Hide, .9, false, false, false);
        state.Update(Hide, 60, false, false, false);
        state.Update(Hide, 61, true, false, false);
        state.Update(Hide, 61.99, true, false, false);
        Assert.False(state.Dormant);
        state.Update(Hide, 62, true, false, false);
        Assert.True(state.Dormant);
    }

    [Fact]
    public void DisablingInactivityWhileDashboardIsClosedReopensAwake()
    {
        var state = new KeyboardInactivity();
        state.Update(Hide, 0, true, false, false);
        state.Update(Hide, 1, true, false, false);
        state.Update(Hide, 2, false, false, false);
        var off = Hide with { Mode = InactivityMode.Off };
        state.Update(off, 3, false, false, false);
        state.Update(off, 4, true, false, false);
        Assert.False(state.Dormant);
        Assert.Equal(1, state.Opacity);
        Assert.Equal(1, state.Scale);
    }

    [Fact]
    public void InstantRevealAndTransitionWorkAtTheirBoundaries()
    {
        var settings = Hide with { TransitionMs = 0, RevealMs = 0, DelayMs = 250 };
        var state = new KeyboardInactivity();
        state.Update(settings, 0, true, false, false);
        state.Update(settings, .25, true, false, false);
        Assert.Equal(0, state.Opacity);
        state.Update(settings, .26, true, false, true);
        Assert.False(state.Dormant);
    }

    [Theory]
    [InlineData("Hide", false)]
    [InlineData("Minimize", true)]
    [InlineData("Transparent", false)]
    public void OnlySettledVisibleMiniatureAcceptsKeyboardHover(string mode, bool acceptsHover)
    {
        var settings = Hide with { Mode = Enum.Parse<InactivityMode>(mode), TransitionMs = 400 };
        var state = new KeyboardInactivity();
        state.Update(settings, 0, true, false, false);
        Assert.False(state.CanRevealFromKeyboard);
        state.Update(settings, 1, true, false, false);
        state.Update(settings, 1.2, true, false, false);
        Assert.False(state.CanRevealFromKeyboard);
        state.Update(settings, 1.4, true, false, false);
        Assert.Equal(acceptsHover, state.CanRevealFromKeyboard);
        state.Update(settings, 2, false, false, false);
        Assert.False(state.CanRevealFromKeyboard);
        state.Update(settings, 3, true, false, false);
        Assert.Equal(acceptsHover, state.CanRevealFromKeyboard);
        state.Wake();
        Assert.False(state.CanRevealFromKeyboard);
    }

    [Fact]
    public void MiniatureHoverUsesRevealDelayAndDoesNotLeaveAnActiveRevealTarget()
    {
        var settings = Hide with { Mode = InactivityMode.Minimize, TransitionMs = 0 };
        var state = new KeyboardInactivity();
        state.Update(settings, 0, true, false, false);
        state.Update(settings, 1, true, false, false);
        Assert.True(state.CanRevealFromKeyboard);
        state.Update(settings, 2, true, false, state.CanRevealFromKeyboard);
        state.Update(settings, 2.19, true, false, state.CanRevealFromKeyboard);
        Assert.True(state.Dormant);
        state.Update(settings, 2.2, true, false, state.CanRevealFromKeyboard);
        Assert.False(state.Dormant);
        Assert.False(state.CanRevealFromKeyboard);
        Assert.Equal(1, state.Scale);
    }

    [Fact]
    public void LosingOneHandRetainsTheOtherAndTrackingLossClearsHover()
    {
        var input = new GrabInput();
        input.Enter(7, 7, 1);
        input.Enter(8, 8, 1);
        input.Leave(7, 7, 2);
        Assert.True(input.HasFocus);
        input.RemoveUntracked(device => device == 7, 3);
        Assert.False(input.HasFocus);
        input.ObserveMotion(8, 8, 30, 30, 2.9, 3, true);
        Assert.False(input.HasFocus); // Old queued motion cannot revive a lost pointer.
    }

    [Fact]
    public void SettingsMergeTimingEditsAndRetainLastGoodValuesAfterFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GoBoard-inactivity-{Guid.NewGuid():N}.json");
        try
        {
            var first = new SettingsStore(path);
            var second = new SettingsStore(path);
            Assert.Equal(InactivityMode.Off, first.Current.Inactivity.Mode);
            Assert.True(first.Update(s => SettingsControls.Apply(SettingsAction.IdleMinimize, s)));
            Assert.True(second.Update(s => SettingsControls.Apply(SettingsAction.RevealLater, s)));
            Assert.True(first.Update(s => SettingsControls.Apply(SettingsAction.IdleOpacityLess, s)));
            var saved = new SettingsStore(path).Current;
            Assert.Equal(InactivityMode.Minimize, saved.Inactivity.Mode);
            Assert.Equal(250, saved.Inactivity.RevealMs);
            Assert.Equal(10, saved.Inactivity.OpacityPercent);
            File.WriteAllText(path, "{broken");
            Assert.False(first.Update(s => SettingsControls.Apply(SettingsAction.IdleHide, s)));
            Assert.Equal(saved, first.Current);
            Assert.Equal(new InactivitySettings(), SettingsControls.Apply(SettingsAction.ResetInactivity, saved).Inactivity);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NormalizationBoundsTimingsAndSupportsOldSettings()
    {
        var s = new BoardSettings { Inactivity = new() { Mode = (InactivityMode)999, DelayMs = -1,
            RevealMs = 99999, TransitionMs = -10, OpacityPercent = 100, SizePercent = 0 } }.Normalize().Inactivity;
        Assert.Equal(InactivityMode.Off, s.Mode);
        Assert.Equal(250, s.DelayMs);
        Assert.Equal(2000, s.RevealMs);
        Assert.Equal(0, s.TransitionMs);
        Assert.Equal(80, s.OpacityPercent);
        Assert.Equal(10, s.SizePercent);
        Assert.Equal(new InactivitySettings(), new BoardSettings { Inactivity = null }.Normalize().Inactivity);
    }

    [Fact]
    public void InactivitySettingsUseSharedHitTargetsAndSwitchPagesWithoutOldCaptures()
    {
        var pointers = new SettingsPointerState();
        var tab = SettingsControls.Tabs.Single(c => c.Action == SettingsAction.InactivityTab).Bounds;
        pointers.Process(1, tab.X + 5, tab.Y + 5, 1, 1, down: true);
        pointers.Process(1, tab.X + 5, tab.Y + 5, 1.1, 1.1, up: true);
        Assert.Equal(SettingsPage.Inactivity, pointers.Page);
        foreach (var c in SettingsControls.ForPage(pointers.Page))
        {
            var b = c.Bounds;
            Assert.Equal(c.Action, SettingsControls.Hit(b.X + b.Width / 2, b.Y + b.Height / 2, pointers.Page));
        }
        foreach (var mode in Enum.GetValues<InactivityMode>())
        {
            using var bitmap = SettingsPanel.Render(new() { Inactivity = new() { Mode = mode } }, pointers);
            Assert.Equal(SettingsControls.Width * 2, bitmap.Width);
        }
    }
}
