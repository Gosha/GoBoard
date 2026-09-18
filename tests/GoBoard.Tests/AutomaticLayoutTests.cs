using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using Xunit;

namespace GoBoard.Tests;

public sealed class AutomaticLayoutTests
{
    [Theory]
    [InlineData("00000409", WindowsLayout.UsHandle)]
    [InlineData("0000041d", WindowsLayout.SwedishHandle)]
    public void NativeTablesMatchEveryExistingLegendState(string id, uint handle)
    {
        var layout = WindowsLayoutProvider.FromKlid((nint)handle, id);
        var reference = new WindowsLayout((nint)handle);
        Assert.Equal(reference.Keys, layout.Keys);
        Assert.Equal(reference.HasAltGr, layout.HasAltGr);
        foreach (var key in reference.Keys)
        for (var state = 0; state < 8; state++)
        {
            var expected = reference.Legend(key, (state & 1) != 0, (state & 2) != 0, (state & 4) != 0);
            var actual = layout.Legend(key, (state & 1) != 0, (state & 2) != 0, (state & 4) != 0);
            Assert.Equal(expected.Text == "—" ? "" : expected.Text, actual.Text);
            Assert.Equal(expected.Dead, actual.Dead);
        }
    }

    [Theory]
    [InlineData("00000407", "y", 0, "z", false)]
    [InlineData("00000407", "z", 1, "Y", false)]
    [InlineData("00000407", "q", 2, "@", false)]
    [InlineData("00000407", "e", 2, "€", false)]
    [InlineData("00000407", "Backquote", 0, "^", true)]
    [InlineData("0000040c", "q", 0, "a", false)]
    [InlineData("0000040c", "a", 0, "q", false)]
    [InlineData("0000040c", "1", 0, "&", false)]
    [InlineData("0000040c", "1", 4, "1", false)]
    [InlineData("0000040c", "0", 2, "@", false)]
    [InlineData("0000040c", "BracketLeft", 0, "^", true)]
    [InlineData("00020409", "Quote", 0, "'", true)]
    [InlineData("00020409", "Quote", 1, "\"", true)]
    [InlineData("00020409", "e", 2, "é", false)]
    [InlineData("00020409", "e", 6, "É", false)]
    [InlineData("00000809", "3", 1, "£", false)]
    public void KnownMappingsComeFromInstalledWindowsFiles(string id, string key, int flags, string text, bool dead)
    {
        var layout = WindowsLayoutProvider.FromKlid((nint)0x12345678, id);
        Assert.True(layout.Supported);
        Assert.Equal(new WindowsLayout.KeyLegend(text, dead), Legend(layout, key, flags));
    }

    [Fact]
    public void GeometryOverrideIsIndependentOfLanguageAndAltGr()
    {
        var international = WindowsLayoutProvider.FromKlid(1, "00020409");
        Assert.False(international.Iso);
        Assert.True(international.HasAltGr);
        var german = WindowsLayoutProvider.FromKlid(2, "00000407", KeyboardGeometry.Ansi);
        Assert.False(german.Iso);
        Assert.True(german.HasAltGr);
        Assert.Equal("z", Legend(german, "y").Text);
        var generic = WindowsLayoutProvider.FromKlid(3, "00000419");
        Assert.True(generic.Iso);
        Assert.Contains("generic ISO", generic.Notice);
        Assert.Equal("й", Legend(generic, "q").Text);
        Assert.Null(WindowsLayoutProvider.FromKlid(3, "00000419", KeyboardGeometry.Iso).Notice);
    }

    [Fact]
    public void CachedForegroundLayoutPreservesFocusAndCallingThreadLanguage()
    {
        var before = WindowsKeyboard.Foreground();
        var threadLayout = GetKeyboardLayout(0);
        var layout = WindowsLayoutProvider.Get(threadLayout);
        Assert.True(layout.Supported);
        Assert.True(layout.WindowsDerived);
        Assert.Same(layout, WindowsLayoutProvider.Get(threadLayout));
        Assert.Equal(threadLayout, GetKeyboardLayout(0));
        Assert.Equal(before, WindowsKeyboard.Foreground());
        Assert.Equal(threadLayout, layout.Handle);
    }

    [Theory]
    [InlineData("00000407")]
    [InlineData("0000040c")]
    [InlineData("00020409")]
    public void NewLayoutsSendAltGrAndCancelRepeatOnGeometryChange(string id)
    {
        var sink = new Sink();
        var state = new KeyboardState(sink);
        state.SetLayout(WindowsLayoutProvider.FromKlid(1, id), 0);
        state.Enter(0, 7, 1);
        void Press(string key, double time, bool release = true)
        {
            var b = state.Layout.Keys.Single(k => k.Id == key).Bounds;
            Assert.True(state.Press(0, 7, b.X + b.Width / 2, OverlayGeometry.PanelHeight - b.Y - b.Height / 2, time, time));
            if (release) state.Up(0, 7, time + .01);
        }
        Press("AltGr", 2); Press("e", 3, false);
        Assert.Equal(new (ushort, bool)[] { (0x1d, true), (0xe038, true), (0x12, true),
            (0x12, false), (0xe038, false), (0x1d, false) }, sink.Events);
        state.SetLayout(WindowsLayoutProvider.FromKlid(1, id, KeyboardGeometry.Ansi), 4);
        sink.Events.Clear(); state.Tick(5);
        Assert.Empty(sink.Events);
        Assert.False(state.AltGr);
        Assert.False(state.HasHeldKeys);
    }

    [Theory]
    [InlineData("00000407")]
    [InlineData("0000040c")]
    [InlineData("00020409")]
    public void GeneratedLayoutsRenderAllModifierStates(string id)
    {
        var state = new KeyboardState(new Sink());
        state.SetLayout(WindowsLayoutProvider.FromKlid(1, id), 0);
        for (var flags = 0; flags < 8; flags++)
        {
            using var bitmap = Panel.Render(state, (flags & 1) != 0, altGr: (flags & 2) != 0, caps: (flags & 4) != 0);
            Assert.Equal(Panel.LayoutWidth * Panel.RasterScale, bitmap.Width);
        }
    }

    [Fact]
    public void UnsafeDllNamesAndImeLayoutsAreRejected()
    {
        Assert.Throws<NotSupportedException>(() => new KeyboardTables(@"..\kbdus.dll"));
        Assert.Throws<NotSupportedException>(() => new KeyboardTables("user32.dll"));
        Assert.Throws<NotSupportedException>(() => WindowsLayoutProvider.FromKlid(1, "00000411"));
        Assert.Throws<ArgumentException>(() => WindowsLayoutProvider.FromKlid(1, "../bad"));
        Assert.False(WindowsLayoutProvider.Get(0).Supported);
    }

    private static WindowsLayout.KeyLegend Legend(WindowsLayout layout, string key, int state = 0)
        => layout.Legend(layout.Keys.Single(k => k.Id == key), (state & 1) != 0, (state & 2) != 0, (state & 4) != 0);
    private sealed class Sink : IKeySink
    {
        public readonly List<(ushort, bool)> Events = new();
        public void Down(ushort scan) => Events.Add((scan, true));
        public void Up(ushort scan) => Events.Add((scan, false));
    }
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
}
