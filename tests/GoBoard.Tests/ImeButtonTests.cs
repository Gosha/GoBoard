using GoBoard.Core;
using Xunit;

namespace GoBoard.Tests;

public sealed class ImeButtonTests
{
    [Theory]
    [InlineData(0x04110411u, true)]
    [InlineData(0xe0010411u, true)]
    [InlineData(WindowsLayout.UsHandle, false)]
    [InlineData(WindowsLayout.SwedishHandle, false)]
    [InlineData(0x04120412u, false)]
    public void ToggleOnlyAppearsForJapaneseAndSharesSpacebarFootprint(uint handle, bool japanese)
    {
        var layout = new WindowsLayout((nint)handle);
        Assert.Equal(japanese, layout.Japanese);
        Assert.Equal(japanese, layout.Keys.Any(k => k.Id == "ImeToggle"));
        if (!japanese) return;
        var button = layout.Keys.Single(k => k.Id == "ImeToggle");
        var space = layout.Keys.Single(k => k.Id == "Space");
        var originalSpace = KeyboardLayout.Keys.Single(k => k.Id == "Space");
        Assert.Equal("あ/A", button.Label);
        Assert.False(button.Repeat);
        Assert.Equal(2, button.Bounds.X - space.Bounds.X - space.Bounds.Width);
        Assert.Equal(originalSpace.Bounds.X + originalSpace.Bounds.Width, button.Bounds.X + button.Bounds.Width);
        Assert.Same(button, KeyboardLayout.Hit(button.Bounds.X + 10, button.Bounds.Y + 10, layout.Keys));
    }

    [Fact]
    public void ToggleIsUnmodifiedNonRepeatingAndPreservesArmedModifiers()
    {
        var sink = new Sink();
        var state = new KeyboardState(sink);
        state.SetLayout(new WindowsLayout((nint)0x04110411), 0);
        state.Enter(0, 7, 1); state.Enter(1, 8, 1);
        double time = 2;
        void Press(string id, uint pointer = 0, bool release = true)
        {
            var b = state.Layout.Keys.Single(k => k.Id == id).Bounds;
            Assert.True(state.Press(pointer, pointer + 7, b.X + b.Width / 2, OverlayGeometry.PanelHeight - b.Y - b.Height / 2, time, time));
            if (release) state.Up(pointer, pointer + 7, time + .01);
            time += .1;
        }
        Press("Shift"); Press("Ctrl"); Press("Ctrl");
        Press("ImeToggle", release: false);
        // A second controller on the same held button must not toggle it back.
        Press("ImeToggle", pointer: 1);
        state.Tick(time + 5);
        Assert.Equal(new (ushort, bool)[] { (KeyboardLayout.ImeToggleKey, true), (KeyboardLayout.ImeToggleKey, false) }, sink.Events);
        Assert.Equal(ModifierMode.OneShot, state.Mode(0x2a));
        Assert.Equal(ModifierMode.Locked, state.Mode(0x1d));
        state.Up(0, 7, time);
        time += .1;
        sink.Events.Clear();
        Press("a");
        Assert.Equal(new (ushort, bool)[] { (0x1d, true), (0x2a, true), (0x1e, true), (0x1e, false), (0x2a, false), (0x1d, false) }, sink.Events);
        Assert.False(state.Shift);
    }

    [Fact]
    public void LayoutChangeCancelsCapturedToggleWithoutRetypingOrStaleRelease()
    {
        var sink = new Sink();
        var state = new KeyboardState(sink);
        state.SetLayout(new WindowsLayout((nint)0x04110411), 0);
        state.Enter(0, 7, 1);
        var b = state.Layout.Keys.Single(k => k.Id == "ImeToggle").Bounds;
        Assert.True(state.Press(0, 7, b.X + 10, OverlayGeometry.PanelHeight - b.Y - 10, 2, 2));
        state.SetLayout(new WindowsLayout((nint)WindowsLayout.UsHandle), 3);
        state.Tick(4); state.Up(0, 7, 5);
        Assert.False(state.HasHeldKeys);
        Assert.Equal(2, sink.Events.Count);
        Assert.DoesNotContain(state.Layout.Keys, k => k.Id == "ImeToggle");
    }

    private sealed class Sink : IKeySink
    {
        public readonly List<(ushort, bool)> Events = new();
        public void Down(ushort scan) => Events.Add((scan, true));
        public void Up(ushort scan) => Events.Add((scan, false));
    }
}
