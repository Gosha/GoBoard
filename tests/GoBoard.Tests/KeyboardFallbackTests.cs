using GoBoard.Core;
using Xunit;

namespace GoBoard.Tests;

public sealed class KeyboardFallbackTests
{
    private sealed class Sink : IKeySink
    {
        public readonly List<(ushort Scan, bool Down)> Events = new();
        public void Down(ushort scan) => Events.Add((scan, true));
        public void Up(ushort scan) => Events.Add((scan, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownLayoutsAlwaysUseUsGeometryAndAllLegendStates(bool swedish)
    {
        var state = new KeyboardState(new Sink());
        var known = new WindowsLayout((nint)(swedish ? WindowsLayout.SwedishHandle : WindowsLayout.UsHandle));
        state.SetLayout(known, 0);
        var fallback = new WindowsLayout((nint)WindowsLayout.UsHandle);
        double time = 1;
        // Include absent HKL, consecutive unknown layouts, and a US language variant.
        foreach (var handle in new uint[] { 0x08090809, 0, 0x00010409, 0xf0020409, 0x08090809 })
        {
            state.SetLayout(new WindowsLayout((nint)handle), time++);
            Assert.Equal((nint)handle, state.Layout.Handle);
            Assert.False(state.Layout.Supported);
            Assert.False(state.Layout.Swedish);
            Assert.Same(fallback.Keys, state.Layout.Keys);
            Assert.Contains("US English", state.Layout.Status);
            Assert.Contains("output may differ", state.Layout.Status);
            foreach (var key in fallback.Keys)
            for (var flags = 0; flags < 8; flags++)
                Assert.Equal(fallback.Legend(key, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0),
                    state.Layout.Legend(key, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0));
        }
        var restored = new WindowsLayout((nint)(swedish ? WindowsLayout.UsHandle : WindowsLayout.SwedishHandle));
        state.SetLayout(restored, time++);
        Assert.True(state.Layout.Supported);
        Assert.Same(restored.Keys, state.Layout.Keys);
        state.SetLayout(new WindowsLayout((nint)0x08090809), time);
        Assert.False(state.Layout.Swedish);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackSendsBalancedKeysShortcutsAndRepeatAndCancelsOnEveryLayoutChange(bool swedish)
    {
        var sink = new Sink();
        var state = new KeyboardState(sink);
        // Use US fallback both at startup and after observing Swedish.
        if (swedish) state.SetLayout(new WindowsLayout((nint)WindowsLayout.SwedishHandle), 0);
        state.SetLayout(new WindowsLayout((nint)0x08090809), 1);
        Assert.False(state.Layout.Swedish);
        state.Enter(0, 7, 2);
        double time = 3;
        void Press(string id, bool release = true)
        {
            var b = state.Layout.Keys.Single(k => k.Id == id).Bounds;
            Assert.True(state.Press(0, 7, b.X + b.Width / 2, OverlayGeometry.PanelHeight - b.Y - b.Height / 2, time, time));
            if (release) state.Up(0, 7, time + .01);
            time += .1;
        }
        void Expect(params ushort[] scans)
        {
            Assert.Equal(scans.Select(s => (s, true)).Concat(scans.Reverse().Select(s => (s, false))), sink.Events);
            sink.Events.Clear();
        }
        Press("a"); Expect(0x1e);
        Press("Left"); Expect(0xe04b);
        Press("Enter"); Expect(0x1c);
        Press("Win"); Press("Win"); Expect(0xe05b);
        Press("Ctrl"); Press("a"); Expect(0x1d, 0x1e);
        Press("Shift"); Press("a"); Expect(0x2a, 0x1e);
        Press("AltGr"); Press("2");
        Expect(0xe038, 0x03);

        foreach (var next in new uint[] { 0xf0020409, WindowsLayout.SwedishHandle, 0x08090809, WindowsLayout.UsHandle })
        {
            Press("Shift"); Press("Shift");
            Press("a", release: false); Expect(0x2a, 0x1e);
            state.Tick(time += 1); Expect(0x2a, 0x1e);
            state.SetLayout(new WindowsLayout((nint)next), time += 1);
            Assert.False(state.HasHeldKeys);
            Assert.False(state.Shift);
            Assert.All(state.Layout.Keys, key => Assert.False(state.Hovered(key)));
            state.Tick(time += 1);
            Assert.Empty(sink.Events);
            Press("a"); Expect(0x1e);
        }
    }
}
