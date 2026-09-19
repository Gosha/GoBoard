using System.Numerics;
using GoBoard.Core;
using Xunit;

namespace GoBoard.Tests;

public sealed class ScaledRelativePoseTests
{
    [Theory]
    [InlineData(.5f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void ResizingScalesTheWholeOffsetAndPreservesPanelOrientation(float placedScale)
    {
        var pose = new ScaledRelativePose();
        var local = Matrix4x4.CreateFromYawPitchRoll(.3f, -.2f, .1f) * Matrix4x4.CreateTranslation(-.6f, .2f, .15f);
        pose.SetLocal(local, placedScale);
        foreach (var scale in new[] { .5f, 1f, 1.5f, placedScale })
        {
            var actual = pose.AtScale(scale);
            var expected = local;
            expected.Translation *= scale / placedScale;
            Assert.True(RelativePose.Near(expected, actual));
            Assert.True(Matrix4x4.Decompose(actual, out var axes, out _, out _));
            Assert.True(Vector3.Distance(Vector3.One, axes) < .00001f);
        }
    }

    [Fact]
    public void DragAtNewScaleBecomesTheNewRelativePlacementWithoutDrift()
    {
        var pose = new ScaledRelativePose();
        var parent = Matrix4x4.CreateFromYawPitchRoll(.7f, .2f, -.1f) * Matrix4x4.CreateTranslation(2, 1, -3);
        var local = Matrix4x4.CreateRotationX(.4f) * Matrix4x4.CreateTranslation(-.8f, -.1f, .3f);
        var draggedWorld = local * parent;
        Matrix4x4.Invert(parent, out var inverse);
        pose.SetLocal(draggedWorld * inverse, 1.5f);
        Assert.True(RelativePose.Near(draggedWorld, pose.AtScale(1.5f) * parent));
        var reduced = local;
        reduced.Translation /= 3;
        Assert.True(RelativePose.Near(reduced * parent, pose.AtScale(.5f) * parent));
        // Live resize, cancellation, and committing the same size must not
        // compound the offset, even after many frames or hidden intervals.
        for (var i = 0; i < 10000; i++) _ = pose.AtScale(i % 2 == 0 ? .5f : 1.5f);
        Assert.True(RelativePose.Near(local, pose.AtScale(1.5f)));
        var nextParent = Matrix4x4.CreateRotationY(-.5f) * Matrix4x4.CreateTranslation(-1, 2, -2);
        Assert.True(RelativePose.Near(reduced * nextParent, pose.AtScale(.5f) * nextParent));
    }
}
