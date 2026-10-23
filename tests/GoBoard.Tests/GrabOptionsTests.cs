using System.Numerics;
using GoBoard.Core;
using Xunit;

namespace GoBoard.Tests;

public sealed class GrabOptionsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GoBoard.GrabOptions.Tests", Guid.NewGuid().ToString("N"));

    private static (GrabPose Grab, Matrix4x4 Hand, Matrix4x4 Panel) Capture()
    {
        var panel = Matrix4x4.CreateRotationX(.3f) * Matrix4x4.CreateTranslation(.2f, 1.1f, -1.4f);
        var hand = Matrix4x4.CreateRotationY(.5f) * Matrix4x4.CreateTranslation(.1f, 1, -.3f);
        return (new GrabPose(panel, hand), hand, panel);
    }

    [Fact]
    public void PullingStickDownBringsKeyboardCloserAlongTheSameLine()
    {
        var (grab, hand, panel) = Capture();
        var before = grab.ControllerOffset.Translation;
        var closer = grab.WithStick(-1, .05f);
        Assert.NotSame(grab, closer);
        var after = closer.ControllerOffset.Translation;
        Assert.True(after.Length() < before.Length());
        Assert.True(Vector3.Dot(Vector3.Normalize(after), Vector3.Normalize(before)) > .9999f);
        // Orientation is unchanged; only the panel's distance from the controller moves.
        var rotated = closer.Update(hand);
        Assert.True(RelativePose.Near(rotated with { M41 = 0, M42 = 0, M43 = 0 }, panel with { M41 = 0, M42 = 0, M43 = 0 }));
        Assert.True(grab.WithStick(1, .05f).ControllerOffset.Translation.Length() > before.Length());
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(.15f)]
    [InlineData(-.2f)]
    [InlineData(float.NaN)]
    public void StickDeadzoneAndInvalidInputKeepTheCapture(float stick)
    {
        var (grab, _, _) = Capture();
        Assert.Same(grab, grab.WithStick(stick, .05f));
        Assert.Same(grab, grab.WithStick(-1, 0));
        Assert.Same(grab, grab.WithStick(-1, float.PositiveInfinity));
    }

    [Fact]
    public void StickDistanceIsClampedAndFrameHitchesAreBounded()
    {
        var (grab, _, _) = Capture();
        var distance = grab.ControllerOffset.Translation.Length();
        // A long stall moves at most one 100 ms step: a threefold rate gives 3^0.1.
        var hitch = grab.WithStick(1, 5).ControllerOffset.Translation.Length();
        Assert.Equal(distance * MathF.Pow(3, .1f), hitch, 4);
        for (var i = 0; i < 200; i++) grab = grab.WithStick(-1, .1f);
        Assert.Equal(GrabPose.MinDistance, grab.ControllerOffset.Translation.Length(), 4);
        Assert.Same(grab, grab.WithStick(-1, .1f));
        for (var i = 0; i < 400; i++) grab = grab.WithStick(1, .1f);
        Assert.Equal(GrabPose.MaxDistance, grab.ControllerOffset.Translation.Length(), 3);
    }

    [Fact]
    public void StickDoesNotSnapACaptureOutsideTheLimits()
    {
        var hand = Matrix4x4.CreateTranslation(0, 1, 0);
        var near = new GrabPose(Matrix4x4.CreateTranslation(0, 1, -.05f), hand);
        Assert.Same(near, near.WithStick(-1, .1f));
        Assert.True(near.WithStick(1, .1f).ControllerOffset.Translation.Length() > .05f);
        var far = new GrabPose(Matrix4x4.CreateTranslation(0, 1, -4), hand);
        Assert.Same(far, far.WithStick(1, .1f));
        Assert.True(far.WithStick(-1, .1f).ControllerOffset.Translation.Length() < 4);
    }

    [Fact]
    public void GripStartsOnlyOnAFreshPressWhilePointingAtTheKeyboard()
    {
        var grips = new GripPresses();
        uint[] none = [], left = [3];
        Assert.Null(grips.Update([(3, false), (4, false)], left));
        // Holding the grip while moving onto the keyboard never starts a grab.
        Assert.Null(grips.Update([(3, true), (4, false)], none));
        Assert.Null(grips.Update([(3, true), (4, false)], left));
        Assert.Null(grips.Update([(3, false), (4, false)], left));
        Assert.Equal(3u, grips.Update([(3, true), (4, false)], left));
        Assert.Null(grips.Update([(3, true), (4, false)], left));
        // The other hand's press elsewhere is ignored; its own press on the keyboard starts.
        Assert.Null(grips.Update([(3, true), (4, true)], left));
        Assert.Null(grips.Update([(3, true), (4, false)], new uint[] { 4 }));
        Assert.Equal(4u, grips.Update([(3, true), (4, true)], new uint[] { 3, 4 }));
    }

    [Fact]
    public void GrabOptionsAreOptInTogglesThatPersist()
    {
        var path = Path.Combine(directory, "settings.json");
        Assert.False(BoardSettings.Defaults.GripGrabEnabled);
        Assert.False(BoardSettings.Defaults.StickDistanceEnabled);
        var editor = new SettingsStore(path);
        Assert.True(editor.Update(s => SettingsControls.Apply(SettingsAction.ToggleGripGrab, s)));
        Assert.True(editor.Update(s => SettingsControls.Apply(SettingsAction.ToggleStickDistance, s)));
        var host = new SettingsStore(path);
        Assert.True(host.Current.GripGrabEnabled);
        Assert.True(host.Current.StickDistanceEnabled);
        Assert.False(SettingsControls.Apply(SettingsAction.ToggleGripGrab, host.Current).GripGrabEnabled);
        var reset = SettingsControls.Apply(SettingsAction.Defaults, host.Current);
        Assert.False(reset.GripGrabEnabled || reset.StickDistanceEnabled);
    }

    [Fact]
    public void GrabTabOpensItsPageAndControlsHitTest()
    {
        var tabs = SettingsControls.Tabs;
        Assert.Equal(5, tabs.Length);
        for (var i = 1; i < tabs.Length; i++)
            Assert.True(tabs[i].Bounds.X >= tabs[i - 1].Bounds.X + tabs[i - 1].Bounds.Width);
        Assert.True(tabs[^1].Bounds.X + tabs[^1].Bounds.Width <= 804);
        var pointers = new SettingsPointerState();
        var tab = tabs.Single(c => c.Action == SettingsAction.GrabTab).Bounds;
        pointers.Process(0, tab.X + 10, tab.Y + 10, 1, 1, down: true);
        pointers.Process(0, tab.X + 10, tab.Y + 10, 1.1, 1.1, up: true);
        Assert.Equal(SettingsPage.Grab, pointers.Page);
        foreach (var control in SettingsControls.Grab)
        {
            var b = control.Bounds;
            Assert.Equal(control.Action, SettingsControls.Hit(b.X + b.Width / 2, b.Y + b.Height / 2, SettingsPage.Grab));
            Assert.NotEqual(control.Action, SettingsControls.Hit(b.X + b.Width / 2, b.Y + b.Height / 2));
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch (DirectoryNotFoundException) { }
    }
}
