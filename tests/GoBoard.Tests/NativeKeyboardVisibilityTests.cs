using GoBoard.Vr;
using Valve.VR;
using Xunit;

namespace GoBoard.Tests;

public sealed class NativeKeyboardVisibilityTests
{
    [Theory]
    [InlineData(NativeKeyboardVisibility.SystemKeyboardKey)]
    [InlineData(NativeKeyboardVisibility.GamepadKeyboardKey)]
    public void DetectsAlreadyOpenKeyboardAndRepeatedToggles(string key)
    {
        var api = new FakeOverlays();
        api.Handles[key] = 10;
        api.Visible.Add(10);
        var keyboard = new NativeKeyboardVisibility(api);

        // No open event was received by this instance.
        Assert.True(keyboard.IsVisible());
        for (var i = 0; i < 3; i++)
        {
            api.Visible.Clear();
            Assert.False(keyboard.IsVisible());
            api.Visible.Add(10);
            Assert.True(keyboard.IsVisible());
        }
    }

    [Fact]
    public void EitherKeyboardKeepsGoBoardHiddenUntilBothAreClosed()
    {
        var api = new FakeOverlays();
        api.Handles[NativeKeyboardVisibility.SystemKeyboardKey] = 10;
        api.Handles[NativeKeyboardVisibility.GamepadKeyboardKey] = 20;
        api.Visible.UnionWith([10, 20]);
        var keyboard = new NativeKeyboardVisibility(api);

        Assert.True(keyboard.IsVisible());
        api.Visible.Remove(10);
        Assert.True(keyboard.IsVisible());
        api.Visible.Remove(20);
        Assert.False(keyboard.IsVisible());
    }

    [Fact]
    public void MissingAndRecreatedKeyboardDoesNotLeaveStaleVisibility()
    {
        var api = new FakeOverlays();
        var keyboard = new NativeKeyboardVisibility(api);
        Assert.False(keyboard.IsVisible());

        api.Handles[NativeKeyboardVisibility.SystemKeyboardKey] = 10;
        api.Visible.Add(10);
        Assert.True(keyboard.IsVisible());
        api.Handles.Clear();
        Assert.False(keyboard.IsVisible());

        // The obsolete handle still reports visible in the fake; never reuse it.
        api.Handles[NativeKeyboardVisibility.SystemKeyboardKey] = 20;
        Assert.False(keyboard.IsVisible());
        api.Visible.Add(20);
        Assert.True(keyboard.IsVisible());
    }

    [Fact]
    public void FailedLookupAndInvalidHandleNeverQueryVisibility()
    {
        var api = new FakeOverlays { Error = EVROverlayError.RequestFailed };
        api.Handles[NativeKeyboardVisibility.SystemKeyboardKey] = 10;
        api.Visible.Add(10);
        var keyboard = new NativeKeyboardVisibility(api);
        Assert.False(keyboard.IsVisible());
        Assert.Empty(api.VisibilityQueries);

        api.Error = EVROverlayError.None;
        api.Handles[NativeKeyboardVisibility.SystemKeyboardKey] = OpenVR.k_ulOverlayHandleInvalid;
        Assert.False(keyboard.IsVisible());
        Assert.Empty(api.VisibilityQueries);
    }

    private sealed class FakeOverlays : INativeKeyboardOverlays
    {
        public Dictionary<string, ulong> Handles { get; } = new();
        public HashSet<ulong> Visible { get; } = new();
        public List<ulong> VisibilityQueries { get; } = new();
        public EVROverlayError Error { get; set; }

        public EVROverlayError FindOverlay(string key, ref ulong handle)
        {
            if (Error != EVROverlayError.None) return Error;
            if (!Handles.TryGetValue(key, out handle)) return EVROverlayError.UnknownOverlay;
            return EVROverlayError.None;
        }

        public bool IsOverlayVisible(ulong handle)
        {
            VisibilityQueries.Add(handle);
            return Visible.Contains(handle);
        }
    }
}
