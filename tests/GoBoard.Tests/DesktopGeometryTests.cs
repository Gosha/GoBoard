using GoBoard.Core;
using Xunit;

namespace GoBoard.Tests;

public sealed class DesktopGeometryTests
{
    [Theory]
    [InlineData(.5f, 1f)]
    [InlineData(1f, 1f)]
    [InlineData(1.5f, 1.5f)]
    [InlineData(1.5f, 2f)]
    public void DesktopPixelsHitTheSameKeysAtEverySizeAndDpi(float scale, float dpi)
    {
        var geometry = DesktopGeometry.Create(scale, dpi, 1200, 750);
        Assert.InRange(geometry.Width, 1, 1200);
        Assert.InRange(geometry.Height, 1, 750);
        Assert.Equal(OverlayGeometry.PanelWidth / (float)OverlayGeometry.PanelHeight,
            geometry.Width / (geometry.Height - geometry.HeaderHeight), precision: 4);
        foreach (var keys in new[] { KeyboardLayout.Keys, KeyboardLayout.SwedishKeys })
        foreach (var key in keys)
        {
            var b = key.Bounds;
            var point = geometry.ToKeyboard((b.X + b.Width / 2) * geometry.Width / OverlayGeometry.PanelWidth,
                geometry.HeaderHeight + (b.Y + b.Height / 2) * (geometry.Height - geometry.HeaderHeight) / OverlayGeometry.PanelHeight);
            Assert.Same(key, KeyboardLayout.HitOpenVr(point.X, point.Y, keys));
        }
        foreach (var point in new[] { (-1f, geometry.HeaderHeight), (10f, 10f), (geometry.Width, geometry.Height), (float.NaN, 50f) })
        {
            var hit = geometry.ToKeyboard(point.Item1, point.Item2);
            Assert.Null(KeyboardLayout.HitOpenVr(hit.X, hit.Y));
        }
    }
}
