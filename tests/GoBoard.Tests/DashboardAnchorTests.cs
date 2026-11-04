using System.Numerics;
using GoBoard.Core;
using Xunit;

namespace GoBoard.Tests;

public sealed class DashboardAnchorTests
{
    // Standing at (1, 1.7, 2), facing world +X (yawed 90 degrees right).
    private static readonly Matrix4x4 Head = Matrix4x4.CreateRotationY(-MathF.PI / 2) * Matrix4x4.CreateTranslation(1, 1.7f, 2);
    private static readonly Matrix4x4 BarInFront = Matrix4x4.CreateRotationY(-MathF.PI / 2) * Matrix4x4.CreateTranslation(2, 1.2f, 2);
    private static readonly Matrix4x4 BarAtOrigin = Matrix4x4.Identity;

    [Fact]
    public void BarNearAndInFrontIsFollowedImmediately()
    {
        var anchor = new DashboardAnchor();
        Assert.Equal(BarInFront, anchor.Resolve(0, BarInFront, Head));
        Assert.Equal(DashboardAnchorMode.Dashboard, anchor.Mode);
        // Later headset turns do not abandon a dashboard the user can see.
        var moved = BarInFront * Matrix4x4.CreateTranslation(-3, 0, 0);
        Assert.Equal(moved, anchor.Resolve(5, moved, Head));
    }

    [Fact]
    public void BarBehindTheHeadsetFallsBackAfterSettling()
    {
        var anchor = new DashboardAnchor();
        Assert.Null(anchor.Resolve(10, BarAtOrigin, Head));
        Assert.Null(anchor.Resolve(10 + DashboardAnchor.SettleSeconds / 2, BarAtOrigin, Head));
        var parent = anchor.Resolve(10 + DashboardAnchor.SettleSeconds, BarAtOrigin, Head);
        Assert.Equal(DashboardAnchorMode.Headset, anchor.Mode);
        Assert.NotNull(parent);

        // The default keyboard lands in front of and below the eyes, facing them.
        var keyboard = RelativePose.DefaultLocal * parent.Value;
        var expected = Head.Translation + Vector3.UnitX * DashboardAnchor.FallbackForward - Vector3.UnitY * DashboardAnchor.FallbackDrop;
        Assert.True(Vector3.Distance(expected, keyboard.Translation) < 0.0001f);
        var normal = new Vector3(keyboard.M31, keyboard.M32, keyboard.M33);
        Assert.True(Vector3.Dot(normal, Vector3.Normalize(Head.Translation - keyboard.Translation)) > 0.9f);

        // The fallback stays put when the head moves, so the keyboard can be reached.
        var turned = Matrix4x4.CreateRotationY(1) * Matrix4x4.CreateTranslation(0, 1.7f, 0);
        Assert.Equal(parent, anchor.Resolve(20, BarAtOrigin, turned));
    }

    [Fact]
    public void BarThatSettlesInFrontWithinTheWindowIsFollowed()
    {
        var anchor = new DashboardAnchor();
        Assert.Null(anchor.Resolve(0, BarAtOrigin, Head));
        Assert.Equal(BarInFront, anchor.Resolve(0.1, BarInFront, Head));
        Assert.Equal(DashboardAnchorMode.Dashboard, anchor.Mode);
    }

    [Fact]
    public void ClosingTheDashboardReevaluatesTheNextOpening()
    {
        var anchor = new DashboardAnchor();
        anchor.Resolve(0, BarAtOrigin, Head);
        anchor.Resolve(1, BarAtOrigin, Head);
        Assert.Equal(DashboardAnchorMode.Headset, anchor.Mode);
        anchor.Close();
        Assert.Equal(BarInFront, anchor.Resolve(2, BarInFront, Head));
    }

    [Fact]
    public void FarBarAndMissingHeadsetPose()
    {
        var far = BarInFront * Matrix4x4.CreateTranslation(DashboardAnchor.MaxDistance + 1, 0, 0);
        Assert.False(DashboardAnchor.IsPlausible(far, Head));
        // Without a headset pose, keep the existing dashboard-bar behavior.
        Assert.Equal(BarAtOrigin, new DashboardAnchor().Resolve(0, BarAtOrigin, null));
    }

    [Theory]
    [InlineData(-1.4f)]
    [InlineData(1.4f)]
    public void LookingSteeplyKeepsTheHeadingDirection(float pitch)
    {
        var head = Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(-MathF.PI / 2);
        Assert.True(Vector3.Distance(Vector3.UnitX, DashboardAnchor.Forward(head)) < 0.001f);
        var straight = Matrix4x4.CreateRotationX(-MathF.PI / 2) * Matrix4x4.CreateRotationY(-MathF.PI / 2);
        Assert.True(Vector3.Distance(Vector3.UnitX, DashboardAnchor.Forward(straight)) < 0.001f);
    }
}
