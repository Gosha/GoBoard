using GoBoard.Core;
using GoBoard.Presentation.Skia;
using GoBoard.Platform.Windows;
using Xunit;

namespace GoBoard.Tests;

public sealed class KeyboardDesignTests
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
    public void TargetsAreInsidePanelAndNeverOverlap(bool swedish)
    {
        var keys = swedish ? KeyboardLayout.SwedishKeys : KeyboardLayout.Keys;
        Assert.Equal(OverlayGeometry.PanelPadding, keys.Min(k => k.Bounds.X));
        Assert.Equal(OverlayGeometry.PanelPadding, keys.Min(k => k.Bounds.Y));
        Assert.Equal(OverlayGeometry.PanelWidth - OverlayGeometry.PanelPadding, keys.Max(k => k.Bounds.X + k.Bounds.Width));
        Assert.Equal(OverlayGeometry.PanelHeight - OverlayGeometry.PanelPadding, keys.Max(k => k.Bounds.Y + k.Bounds.Height));
        foreach (var key in keys)
        {
            var b = key.Bounds;
            Assert.True(b.X >= 0 && b.Y >= 0 && b.X + b.Width <= OverlayGeometry.PanelWidth && b.Y + b.Height <= OverlayGeometry.PanelHeight, key.Id);
            Assert.True(b.Width >= 40 && b.Height >= 40, key.Id);
            Assert.Same(key, KeyboardLayout.HitOpenVr(b.X + b.Width / 2, OverlayGeometry.PanelHeight - b.Y - b.Height / 2, keys));
        }
        for (var i = 0; i < keys.Count; i++)
        for (var j = i + 1; j < keys.Count; j++)
        {
            var a = keys[i].Bounds; var b = keys[j].Bounds;
            for (float y = Math.Max(a.Y, b.Y) + .5f; y < Math.Min(a.Y + a.Height, b.Y + b.Height); y++)
            for (float x = Math.Max(a.X, b.X) + .5f; x < Math.Min(a.X + a.Width, b.X + b.Width); x++)
                Assert.False(keys[i].Contains(x, y) && keys[j].Contains(x, y), $"{keys[i].Id} overlaps {keys[j].Id} at {x}, {y}");
        }
        var aliases = keys.GroupBy(k => k.Scan).Where(g => g.Count() > 1).Select(g => g.Key).Order().ToArray();
        Assert.Equal(new ushort[] { 0x1d, 0x2a }, aliases);
        Assert.Single(keys, k => k.Id == "Insert");
        Assert.Null(KeyboardLayout.Hit(float.PositiveInfinity, 20, keys));
    }

    [Fact]
    public void NavigationMatchesDesktopThreeByTwoCluster()
    {
        foreach (var keys in new[] { KeyboardLayout.Keys, KeyboardLayout.SwedishKeys })
        {
            KeyBounds Bounds(string id) => keys.Single(k => k.Id == id).Bounds;
            var top = new[] { "Insert", "Home", "PageUp" }.Select(Bounds).ToArray();
            var bottom = new[] { "Delete", "End", "PageDown" }.Select(Bounds).ToArray();
            var system = new[] { "PrintScreen", "ScrollLock", "Pause" }.Select(Bounds).ToArray();
            Assert.True(top[0].X < top[1].X && top[1].X < top[2].X);
            Assert.All(top, b => Assert.Equal(top[0].Y, b.Y));
            Assert.All(bottom, b => Assert.Equal(bottom[0].Y, b.Y));
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(top[i].X, bottom[i].X);
                Assert.Equal(top[i].X, system[i].X);
                Assert.Equal(Bounds("F12").Y, system[i].Y);
                Assert.True(system[i].Y + system[i].Height < top[i].Y);
                Assert.True(bottom[i].Y >= top[i].Y + top[i].Height);
            }
            Assert.Equal(top[1].X, Bounds("Up").X);
            Assert.Equal(top[1].X, Bounds("Down").X);
            Assert.Equal(top[0].X, Bounds("Left").X);
            Assert.Equal(top[2].X, Bounds("Right").X);
        }
    }

    [Fact]
    public void IsoEnterNotchRoutesPunctuationAndCancelsEnterRepeatCapture()
    {
        var keys = KeyboardLayout.SwedishKeys;
        var enter = keys.Single(k => k.Id == "Enter").Bounds;
        var punctuation = keys.Single(k => k.Id == "Backslash").Bounds;
        var left = enter.X + 6; var top = enter.Y + 28; var lower = enter.Y + 72;
        Assert.Equal("Enter", KeyboardLayout.Hit(left, top, keys)?.Id);
        Assert.Equal("Enter", KeyboardLayout.Hit(enter.X + 46, lower, keys)?.Id);
        Assert.Equal("Backslash", KeyboardLayout.Hit(left, lower, keys)?.Id);
        Assert.Null(KeyboardLayout.Hit(punctuation.X + punctuation.Width + 1, lower, keys));
        var sink = new Sink(); var state = new KeyboardState(sink);
        state.SetLayout(new WindowsLayout((nint)WindowsLayout.SwedishHandle), 0);
        state.Enter(0, 7, 1);
        Assert.True(state.Press(0, 7, left, OverlayGeometry.PanelHeight - top, 2, 2));
        state.Move(0, 7, left, OverlayGeometry.PanelHeight - lower);
        Assert.False(state.HasHeldKeys);
        Assert.Equal(new (ushort, bool)[] { (0x1c, true), (0x1c, false) }, sink.Events);
        Assert.False(state.Press(0, 7, left, OverlayGeometry.PanelHeight - lower, 3, 3));
    }

    [Fact]
    public void ModifierAliasesShareModesAndNeverDuplicateChordScans()
    {
        var sink = new Sink(); var state = new KeyboardState(sink);
        state.Enter(0, 7, 1); state.Enter(1, 8, 1);
        double time = 2;
        void Tap(string id, uint cursor = 0)
        {
            var b = state.Layout.Keys.Single(k => k.Id == id).Bounds;
            Assert.True(state.Press(cursor, cursor + 7, b.X + b.Width / 2, OverlayGeometry.PanelHeight - b.Y - b.Height / 2, time, time));
            state.Up(cursor, cursor + 7, time + .01); time += .1;
        }
        Tap("RightShift"); Assert.True(state.Shift);
        Tap("Shift", 1); Assert.Equal(ModifierMode.Locked, state.Mode(0x2a));
        Tap("RightShift"); Assert.False(state.Shift);
        Tap("Ctrl"); Tap("RightCtrl", 1);
        Assert.Equal(ModifierMode.Locked, state.Mode(0x1d));
        Tap("RightShift"); Tap("a", 1);
        Assert.Equal(new (ushort, bool)[] { (0x1d, true), (0x2a, true), (0x1e, true), (0x1e, false), (0x2a, false), (0x1d, false) }, sink.Events);
        Assert.False(state.Shift);
        Tap("RightCtrl"); Assert.Equal(ModifierMode.Idle, state.Mode(0x1d));
        state.SetLayout(new WindowsLayout((nint)WindowsLayout.SwedishHandle), time);
        Assert.False(state.HasHeldKeys);
    }

    [Theory]
    [InlineData("Caps", 0x3a)]
    [InlineData("Menu", 0xe05d)]
    [InlineData("Insert", 0xe052)]
    [InlineData("PrintScreen", KeyboardLayout.PrintScreenScan)]
    [InlineData("ScrollLock", KeyboardLayout.ScrollLockScan)]
    [InlineData("Pause", KeyboardLayout.PauseScan)]
    public void AddedAndRetainedActionKeysSendBalancedNonRepeatingStrokes(string id, int scan)
    {
        var sink = new Sink(); var state = new KeyboardState(sink);
        state.Enter(0, 7, 1);
        var b = state.Layout.Keys.Single(k => k.Id == id).Bounds;
        Assert.True(state.Press(0, 7, b.X + b.Width / 2, OverlayGeometry.PanelHeight - b.Y - b.Height / 2, 2, 2));
        state.Tick(5); state.Tick(10); state.Up(0, 7, 11);
        Assert.Equal(new (ushort, bool)[] { ((ushort)scan, true), ((ushort)scan, false) }, sink.Events);
    }

    [Theory]
    [InlineData(KeyboardLayout.PrintScreenScan, 0, 0x37, 9)]
    [InlineData(KeyboardLayout.ScrollLockScan, 0, 0x46, 8)]
    [InlineData(KeyboardLayout.PauseScan, 0x13, 0, 0)]
    public void SystemKeysEncodeCorrectWindowsDownAndUp(int scan, int virtualKey, int hardwareScan, int flags)
    {
        var down = WindowsKeyboard.MakeInput((ushort)scan, false);
        var up = WindowsKeyboard.MakeInput((ushort)scan, true);
        Assert.Equal(1u, down.Type);
        Assert.Equal((ushort)virtualKey, down.VirtualKey);
        Assert.Equal((ushort)hardwareScan, down.Scan);
        Assert.Equal((uint)flags, down.Flags);
        Assert.Equal(down.Type, up.Type);
        Assert.Equal(down.VirtualKey, up.VirtualKey);
        Assert.Equal(down.Scan, up.Scan);
        Assert.Equal(down.Flags | 2u, up.Flags);
        Assert.Equal(down.ExtraInfo, up.ExtraInfo);
        // Pause's physical-key guard must also query VK_PAUSE, not Num Lock.
        if (scan == KeyboardLayout.PauseScan)
            Assert.Equal(0x13u, WindowsKeyboard.VirtualKeyForScan((ushort)scan, 0));
    }

    [Theory]
    [InlineData("us")]
    [InlineData("sv")]
    public void EveryProductionPreviewStateRendersWithoutInput(string language)
    {
        foreach (var state in PanelPreview.States)
        {
            using var bitmap = PanelPreview.Render(language, state);
            Assert.Equal(OverlayGeometry.PanelWidth * OverlayGeometry.RasterScale, bitmap.Width);
            Assert.Equal(OverlayGeometry.PanelHeight * OverlayGeometry.RasterScale, bitmap.Height);
        }
        Assert.Throws<ArgumentException>(() => PanelPreview.Render(language, "invalid"));
    }
}
