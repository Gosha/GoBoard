using GoBoard.Core;
using GoBoard.Vr;
using Xunit;

namespace GoBoard.Tests;

public sealed class AutostartControllerTests
{
    [Fact]
    public void ToggleWaitsForConfirmedSteamVrStateAndRejectsRepeatedClicks()
    {
        var calls = new List<string>();
        var completion = new TaskCompletionSource<AutostartState>();
        var controller = new AutostartController(execute: action =>
        {
            calls.Add(action);
            return completion.Task;
        });
        controller.Toggle();
        Assert.Empty(calls); // Unknown initial state cannot be toggled.
        controller.Update(0);
        Assert.Equal(new[] { "status" }, calls);
        Assert.True(controller.State.Busy);
        controller.Toggle();
        Assert.Single(calls);
        completion.SetResult(new(false));
        controller.Update(1);
        Assert.Equal("Register autostart", controller.State.ButtonLabel);

        completion = new();
        controller.Toggle();
        Assert.Equal("enable", calls[^1]);
        Assert.False(controller.State.Enabled); // Do not claim success before the API completes.
        Assert.False(controller.State.CanClick);
        controller.Toggle();
        Assert.Equal(2, calls.Count);
        completion.SetResult(new(true));
        controller.Update(2);
        Assert.Equal("Unregister autostart", controller.State.ButtonLabel);

        completion = new();
        controller.Toggle();
        Assert.Equal("unregister", calls[^1]);
        completion.SetResult(new(false));
        controller.Update(3);
        Assert.False(controller.State.Enabled);
    }

    [Fact]
    public void FailureOffersReadOnlyRetryAndRefreshTracksExternalChanges()
    {
        var calls = new List<string>();
        Task<AutostartState> result = Task.FromException<AutostartState>(new IOException("SteamVR unavailable"));
        var controller = new AutostartController(execute: action => { calls.Add(action); return result; });
        controller.Update(0);
        controller.Update(1);
        Assert.Null(controller.State.Enabled);
        Assert.Equal("SteamVR unavailable", controller.State.Error);
        Assert.Equal("Retry", controller.State.ButtonLabel);
        result = Task.FromResult(new AutostartState(true));
        controller.Toggle();
        Assert.Equal("status", calls[^1]);
        controller.Update(2);
        Assert.True(controller.State.Enabled);
        Assert.Null(controller.State.Error);
        result = Task.FromResult(new AutostartState(false));
        controller.Update(11);
        Assert.Equal(2, calls.Count);
        controller.Update(12);
        controller.Update(13);
        Assert.Equal("status", calls[^1]);
        Assert.False(controller.State.Enabled);
    }

    [Fact]
    public void AutostartIsGeneralOnlyAndDoesNotChangeSavedPreferences()
    {
        var control = SettingsControls.All.Single(c => c.Action == SettingsAction.Autostart);
        var x = control.Bounds.X + control.Bounds.Width / 2;
        var y = control.Bounds.Y + control.Bounds.Height / 2;
        Assert.Equal(SettingsAction.Autostart, SettingsControls.Hit(x, y));
        Assert.NotEqual(SettingsAction.Autostart, SettingsControls.Hit(x, y, SettingsPage.Effects));
        var settings = new BoardSettings { SizePercent = 120, SoundEnabled = false };
        Assert.Equal(settings, SettingsControls.Apply(SettingsAction.Autostart, settings));
        Assert.False(SettingsControls.AuditionsSound(SettingsAction.Autostart));
        var pointer = new SettingsPointerState();
        pointer.Process(1, x, y, 1, 1, down: true);
        pointer.Process(1, 0, 0, 1.1, 1.1);
        Assert.Null(pointer.Process(1, x, y, 1.2, 1.2, up: true));
    }
}
