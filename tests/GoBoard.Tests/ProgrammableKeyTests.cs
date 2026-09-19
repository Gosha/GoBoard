using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using SkiaSharp;
using Xunit;

namespace GoBoard.Tests;

public sealed class ProgrammableKeyTests
{
    public static IEnumerable<object[]> GridSizes => Enumerable.Range(1, 4)
        .SelectMany(columns => Enumerable.Range(1, 5).Select(rows => new object[] { columns, rows }));
    [Theory]
    [MemberData(nameof(GridSizes))]
    public void FloatingGridHasIndependentGeometryAndRetainsHiddenAssignments(int columns, int rows)
    {
        var settings = new ProgrammableKeySettings { Enabled = true, Columns = columns, Rows = rows, Key10 = new(0x1a, Ctrl: true), Key20 = new(0x1e, Ctrl: true) };
        var state = new KeyboardState(new Sink(), shortcutsOnly: true);
        state.SetShortcuts(settings, 0);
        var main = new KeyboardState(new Sink());
        main.SetShortcuts(settings, 0);
        Assert.Same(main.Layout.Keys, main.Keys);
        Assert.Equal(OverlayGeometry.PanelWidth, main.Width);
        Assert.Equal(columns * rows, state.Keys.Count);
        var board = new BoardSettings { ProgrammableKeys = settings };
        var controls = SettingsControls.ForPage(SettingsPage.Shortcuts, board).Where(c => SettingsControls.SlotIndex(c.Action) >= 0).ToArray();
        Assert.Equal(columns * rows, controls.Length);
        Assert.Equal(Enumerable.Range(1, columns * rows), settings.VisibleSlots.Select(settings.NumberFor));
        foreach (var control in controls)
        {
            var b = control.Bounds;
            Assert.Equal(control.Action, SettingsControls.Hit(b.X + b.Width / 2, b.Y + b.Height / 2, SettingsPage.Shortcuts, board));
            Assert.InRange(b.X + b.Width, 64, 394);
            Assert.InRange(b.Y + b.Height, 388, 690);
        }
        foreach (var key in state.Keys)
        {
            var b = key.Bounds;
            Assert.Same(key, state.Hit(b.X + b.Width / 2, state.Height - b.Y - b.Height / 2));
            Assert.InRange(b.X + b.Width, 0, state.Width);
            Assert.InRange(b.Y + b.Height, 0, state.Height - ProgrammableKeys.StatusHeight);
        }
        foreach (var theme in new[] { BoardThemes.SteamSoft, BoardThemes.SteamFlat })
        {
            using var renderer = new AnimatedKeyboardRenderer();
            using var bitmap = renderer.Render(state, false, null, false, false, false, theme, new(), 1);
            Assert.Equal(state.Width * Panel.RasterScale, bitmap.Width);
            Assert.Equal(state.Height * Panel.RasterScale, bitmap.Height);
            if (Environment.GetEnvironmentVariable("GOBOARD_SHORTCUT_ARTIFACTS") is { Length: > 0 } directory &&
                (columns, rows) is (1, 1) or (4, 5))
            {
                Directory.CreateDirectory(directory);
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                using var file = File.Create(Path.Combine(directory, $"shortcut-grid-{columns}x{rows}-{theme}.png"));
                png.SaveTo(file);
            }
        }
        state.SetShortcuts(settings with { Columns = 1, Rows = 1 }, 2);
        Assert.DoesNotContain(state.Keys, k => k.Id == "Shortcut10");
        Assert.DoesNotContain(state.Keys, k => k.Id == "Shortcut20");
        state.SetShortcuts(state.Shortcuts with { Columns = 4, Rows = 5 }, 3);
        Assert.Equal(settings.Key10, state.Keys.Single(k => k.Id == "Shortcut10").Shortcut);
        Assert.Equal(settings.Key20, state.Keys.Single(k => k.Id == "Shortcut20").Shortcut);
        Assert.False(Press(state, "Shortcut9", 4));
        Assert.Equal(8, new ProgrammableKeySettings().VisibleSlots.Count());
    }

    [Theory]
    [InlineData("0000041d", 0x1a, "Å")]
    [InlineData("00000407", 0x27, "Ö")]
    [InlineData("0000040c", 0x10, "A")]
    public void PickerAndShortcutLabelsFollowWindowsLayout(string klid, ushort scan, string label)
    {
        var layout = WindowsLayoutProvider.FromKlid(0, klid);
        var choice = SettingsControls.KeyChoices(layout).Single(c => c.Action == SettingsAction.KeyChoiceFirst + scan);
        Assert.Equal(label, choice.Label);
        foreach (var key in SettingsControls.KeyChoices(layout))
        {
            var b = key.Bounds;
            var hit = SettingsControls.Hit(b.X + b.Width / 2, b.Y + b.Height / 2, SettingsPage.ShortcutKey, layout: layout);
            if (key.Selectable) Assert.Equal(key.Action, hit);
            else Assert.Null(hit);
            if (key.CutoutWidth > 0)
                Assert.False(key.Contains(b.X + key.CutoutWidth / 2, b.Y + b.Height - 2));
        }
        var settings = SettingsControls.Apply(choice.Action, new(), 0);
        Assert.Equal(scan, settings.ProgrammableKeys.Key1.Scan);
        Assert.EndsWith(label, settings.ProgrammableKeys.Key1.ChordFor(layout));
        var state = new KeyboardState(new Sink(), shortcutsOnly: true);
        state.SetLayout(layout, 0); state.SetShortcuts(settings.ProgrammableKeys, 1);
        Assert.EndsWith(label, state.Keys[0].Label);
        var pointers = new SettingsPointerState();
        pointers.Configure(settings, layout);
        void Click(SettingsAction action, double time)
        {
            var b = SettingsControls.ForPage(pointers.Page, settings, layout).Single(c => c.Action == action).Bounds;
            pointers.Process(0, b.X + 5, b.Y + 5, time, time, down: true);
            pointers.Process(0, b.X + 5, b.Y + 5, time + .1, time + .1, up: true);
        }
        Click(SettingsAction.ShortcutsTab, 2); Click(SettingsAction.ChooseShortcutKey, 3);
        if (Environment.GetEnvironmentVariable("GOBOARD_SHORTCUT_ARTIFACTS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var bitmap = SettingsPanel.Render(settings, pointers);
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(directory, $"shortcut-picker-{klid}.png")); png.SaveTo(file);
        }
        pointers.Process(0, choice.Bounds.X + 5, choice.Bounds.Y + 5, 4, 4, down: true);
        pointers.Configure(settings, new((nint)WindowsLayout.UsHandle));
        Assert.Null(pointers.Process(0, choice.Bounds.X + 5, choice.Bounds.Y + 5, 4.1, 4.1, up: true));
    }

    [Fact]
    public void LauncherRejectsStaleOtherHandAndCancelledClicks()
    {
        var button = new ShortcutLauncherState();
        Assert.False(button.Expanded);
        button.Process(7, 7, 10, 10, 1, 1, down: true);
        Assert.False(button.Process(8, 8, 10, 10, 1.1, 1.1, up: true));
        Assert.True(button.Process(7, 7, 10, 10, 1.2, 1.2, up: true));
        Assert.True(button.Expanded);
        button.Process(7, 7, 10, 10, 2, 2, down: true);
        button.Process(7, 7, -1, -1, 2.1, 2.1, leave: true);
        Assert.False(button.Process(7, 7, 10, 10, 2.2, 2.2, up: true));
        button.Reset(3, collapse: true);
        Assert.False(button.Process(7, 7, 10, 10, 2.99, 3.1, down: true));
        Assert.False(button.Process(7, 7, 10, 10, 3.2, 3.2, up: true));
        Assert.False(button.Expanded);
    }
    private sealed class Sink : IKeySink
    {
        public readonly List<(ushort Scan, bool Down)> Events = new();
        public bool Fail;
        public void Down(ushort scan) { if (Fail) throw new InvalidOperationException("Input failed"); Events.Add((scan, true)); }
        public void Up(ushort scan) => Events.Add((scan, false));
    }
    private static bool Press(KeyboardState state, string id, double time, uint pointer = 0)
    {
        var b = state.Keys.Single(k => k.Id == id).Bounds;
        state.Enter(pointer, pointer + 7, time);
        return state.Press(pointer, pointer + 7, b.X + b.Width / 2, state.Height - b.Y - b.Height / 2, time, time);
    }

    [Fact]
    public void PresetsSendBalancedChordsOnceAndPreserveLogicalModifiers()
    {
        var sink = new Sink();
        var state = new KeyboardState(sink, shortcutsOnly: true);
        var main = new KeyboardState(sink);
        state.SetShortcuts(new() { Enabled = true }, 0);
        Assert.True(Press(main, "Shift", 1)); main.Up(0, 7, 1.1);
        for (var i = 0; i < ProgrammableKeys.Presets.Length; i++)
        {
            sink.Events.Clear();
            var time = 2 + i * 10;
            var shortcut = ProgrammableKeys.Presets[i].Shortcut;
            state.SetShortcuts(state.Shortcuts with { Key1 = shortcut }, time - 1);
            Assert.True(Press(state, "Shortcut1", time));
            // Two hands sharing a slot do not trigger it twice while captured.
            Assert.True(Press(state, "Shortcut1", time + .1, 1));
            state.Tick(time + 5);
            var keys = shortcut.Modifiers.Append(shortcut.Scan).ToArray();
            Assert.Equal(keys.Select(k => (k, true)).Concat(keys.Reverse().Select(k => (k, false))), sink.Events);
            Assert.True(main.Shift);
            state.Up(0, 7, time + 6); state.Up(1, 8, time + 6);
        }
    }

    [Fact]
    public void CustomChordAndOrdinaryKeyHaveIndependentCaptureAndFailureDoesNotConsumeModifiers()
    {
        var sink = new Sink(); var state = new KeyboardState(sink, shortcutsOnly: true);
        var main = new KeyboardState(sink);
        state.SetShortcuts(new() { Enabled = true, Key1 = new(0x1e, Ctrl: true, Shift: true) }, 0);
        Assert.True(Press(main, "Win", 1)); main.Up(0, 7, 1.1);
        sink.Fail = true;
        Assert.Throws<InvalidOperationException>(() => Press(state, "Shortcut1", 2));
        Assert.Equal(ModifierMode.OneShot, main.Mode(0xe05b));
        Assert.False(state.HasHeldKeys);
        sink.Fail = false;
        Assert.True(Press(state, "Shortcut1", 3));
        Assert.True(Press(main, "a", 3.1, 1));
        Assert.Equal(new (ushort, bool)[] { (0x1d, true), (0x2a, true), (0x1e, true), (0x1e, false), (0x2a, false), (0x1d, false),
            (0xe05b, true), (0x1e, true), (0x1e, false), (0xe05b, false) }, sink.Events);
        Assert.True(state.Pressed(state.Keys.Single(k => k.Id == "Shortcut1")));
        state.SetShortcuts(state.Shortcuts with { Key1 = new(0x30) }, 4);
        Assert.False(state.HasHeldKeys);
        Assert.All(state.VisualPointers, p => Assert.False(p.Focused));
        Assert.False(Press(state, "Shortcut1", 3.95, 2)); // Newly seen pointer, queued old event.
        state.Up(0, 7, 4.1); state.Tick(5);
        Assert.Equal(10, sink.Events.Count);
        state.SetShortcuts(state.Shortcuts with { Enabled = false }, 6);
        Assert.False(state.HasHeldKeys);
        Assert.Equal(8, state.Keys.Count); // Hiding the launcher preserves palette assignments.
    }

    [Theory]
    [InlineData(ProgrammableKeys.MediaNext, 0xb0)]
    [InlineData(ProgrammableKeys.MediaPrevious, 0xb1)]
    [InlineData(ProgrammableKeys.MediaStop, 0xb2)]
    [InlineData(ProgrammableKeys.MediaPlayPause, 0xb3)]
    [InlineData(ProgrammableKeys.VolumeMute, 0xad)] [InlineData(ProgrammableKeys.VolumeDown, 0xae)] [InlineData(ProgrammableKeys.VolumeUp, 0xaf)]
    [InlineData(ProgrammableKeys.BrowserBack, 0xa6)] [InlineData(ProgrammableKeys.BrowserForward, 0xa7)]
    [InlineData(ProgrammableKeys.BrowserRefresh, 0xa8)] [InlineData(ProgrammableKeys.BrowserStop, 0xa9)]
    [InlineData(ProgrammableKeys.BrowserSearch, 0xaa)] [InlineData(ProgrammableKeys.BrowserFavorites, 0xab)] [InlineData(ProgrammableKeys.BrowserHome, 0xac)]
    public void SystemKeysUseVirtualKeysOnBothEdges(ushort scan, int vk)
    {
        foreach (var up in new[] { false, true })
        {
            var input = WindowsKeyboard.MakeInput(scan, up);
            Assert.Equal(vk, input.VirtualKey); Assert.Equal(0, input.Scan);
            Assert.Equal(up ? 2u : 0u, input.Flags);
            Assert.Equal((uint)vk, WindowsKeyboard.VirtualKeyForScan(scan, 0));
        }
    }

    [Fact]
    public void PresetNavigationIncludesStopAndWrapsBothWays()
    {
        var settings = new BoardSettings();
        for (var i = 1; i < ProgrammableKeys.Presets.Length; i++)
        {
            settings = SettingsControls.Apply(SettingsAction.NextPreset, settings);
            Assert.Equal(ProgrammableKeys.Presets[i].Shortcut, settings.ProgrammableKeys.Key1);
        }
        Assert.Contains(ProgrammableKeys.Presets, p => p.Shortcut == new KeyboardShortcut(ProgrammableKeys.MediaStop));
        settings = SettingsControls.Apply(SettingsAction.NextPreset, settings);
        Assert.Equal(ProgrammableKeys.Presets[0].Shortcut, settings.ProgrammableKeys.Key1);
        settings = SettingsControls.Apply(SettingsAction.PreviousPreset, settings);
        Assert.Equal(ProgrammableKeys.Presets[^1].Shortcut, settings.ProgrammableKeys.Key1);
        Assert.Equal(settings, settings.Normalize());
    }

    [Theory]
    [InlineData(0x52, 0x52, 8)] [InlineData(0x4f, 0x4f, 8)] [InlineData(0x47, 0x47, 8)]
    [InlineData(0x53, 0x53, 8)] [InlineData(0x37, 0x37, 8)] [InlineData(0x4a, 0x4a, 8)] [InlineData(0x4e, 0x4e, 8)]
    [InlineData(ProgrammableKeys.NumEnter, 0x1c, 9)] [InlineData(ProgrammableKeys.NumDivide, 0x35, 9)]
    public void KeypadPacketsRetainPhysicalAndExtendedIdentity(ushort scan, int lowScan, uint flags)
    {
        Assert.True(ProgrammableKeys.IsAllowed(scan));
        foreach (var up in new[] { false, true })
        {
            var input = WindowsKeyboard.MakeInput(scan, up);
            Assert.Equal(0, input.VirtualKey); Assert.Equal(lowScan, input.Scan);
            Assert.Equal(flags | (up ? 2u : 0u), input.Flags);
        }
        Assert.NotEqual(SettingsControls.KeyChoices().Single(c => c.Action == SettingsAction.KeyChoiceFirst + 0x4f).Action,
            SettingsControls.KeyChoices().Single(c => c.Action == SettingsAction.KeyChoiceFirst + 0xe04f).Action);
    }

    [Fact]
    public void NumLockUsesItsExplicitVirtualKey()
    {
        foreach (var up in new[] { false, true })
        {
            var input = WindowsKeyboard.MakeInput(ProgrammableKeys.NumLock, up);
            Assert.Equal(0x90, input.VirtualKey); Assert.Equal(0x45, input.Scan);
            Assert.Equal(up ? 3u : 1u, input.Flags);
        }
    }

    [Theory]
    [InlineData("0000040c", "Select all", 0x10)] [InlineData("0000040c", "Undo", 0x11)]
    [InlineData("00000407", "Undo", 0x15)] [InlineData("00000407", "Redo", 0x2c)]
    public void LetterPresetsResolveAgainstSelectedLayout(string klid, string preset, ushort scan)
    {
        var layout = WindowsLayoutProvider.FromKlid(0, klid);
        var settings = new BoardSettings();
        var index = Array.FindIndex(ProgrammableKeys.Presets, p => p.Label == preset);
        for (var i = 0; i < index; i++) settings = SettingsControls.Apply(SettingsAction.NextPreset, settings, layout: layout);
        Assert.Equal(scan, settings.ProgrammableKeys.Key1.Scan);
        Assert.Equal(preset, settings.ProgrammableKeys.Key1.LabelFor(layout));
    }

    [Theory]
    [MemberData(nameof(GridSizes))]
    public void DesktopPaletteEndsAtKeyPaddingWithoutAStatusFooter(int columns, int rows)
    {
        var settings = new ProgrammableKeySettings { Columns = columns, Rows = rows };
        var state = new KeyboardState(new Sink(), shortcutsOnly: true, shortcutFooter: false);
        state.SetShortcuts(settings, 0);
        Assert.Equal(ProgrammableKeys.Padding, state.Height - state.Keys.Max(k => k.Bounds.Y + k.Bounds.Height));
        Assert.Equal(ProgrammableKeys.Height(settings) - ProgrammableKeys.StatusHeight, state.Height);
        using var renderer = new AnimatedKeyboardRenderer();
        using var image = renderer.Render(state, false, "Input blocked", false, false, false, BoardThemes.SteamSoft, new(), 1);
        Assert.Equal(state.Height * Panel.RasterScale, image.Height);
        foreach (var key in state.Keys)
        {
            var b = key.Bounds;
            Assert.Same(key, state.Hit(b.X + b.Width / 2, state.Height - b.Y - b.Height / 2));
        }
    }

    [Fact]
    public void EditorsMergeIndividualSlotsAndNormalizationHasStableEquality()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GoBoard-shortcuts-{Guid.NewGuid():N}.json");
        try
        {
            var desktop = new SettingsStore(path); var vr = new SettingsStore(path);
            Assert.True(desktop.Update(s => SettingsControls.Apply(SettingsAction.ToggleShortcuts, s)));
            Assert.True(vr.Update(s => SettingsControls.Apply(SettingsAction.ShortcutAlt, s, 0)));
            Assert.True(desktop.Update(s => SettingsControls.Apply(SettingsAction.ShortcutShift, s, 1)));
            Assert.True(vr.Update(s => SettingsControls.Apply(SettingsAction.ShortcutCtrl, s, 19)));
            Assert.True(desktop.Update(s => SettingsControls.Apply(SettingsAction.KeyChoiceFirst + 0x1e, s, 19)));
            Assert.True(vr.Update(s => s with { VolumePercent = 30 }));
            desktop.Reload();
            Assert.True(desktop.Current.ProgrammableKeys.Enabled);
            Assert.True(desktop.Current.ProgrammableKeys.Key1.Alt);
            Assert.True(desktop.Current.ProgrammableKeys.Key2.Shift);
            Assert.Equal(new KeyboardShortcut(0x1e, Ctrl: true), desktop.Current.ProgrammableKeys.Key20);
            Assert.Equal(30, desktop.Current.VolumePercent);
            Assert.False(desktop.Reload());
            Assert.Equal(desktop.Current, desktop.Current.Normalize());
            File.WriteAllText(path, "{\"ProgrammableKeys\":{\"Enabled\":true,\"Key1\":null,\"Key2\":{\"Scan\":65535},\"Key10\":{\"Scan\":30,\"Ctrl\":true}}}");
            Assert.True(desktop.Reload());
            Assert.Equal(ProgrammableKeys.Presets[0].Shortcut, desktop.Current.ProgrammableKeys.Key1);
            Assert.Equal(new KeyboardShortcut(0), desktop.Current.ProgrammableKeys.Key2);
            Assert.Equal(new KeyboardShortcut(0x1e, Ctrl: true), desktop.Current.ProgrammableKeys.Key10);
            Assert.Equal(new KeyboardShortcut(0), desktop.Current.ProgrammableKeys.Key20);
            var expanded = desktop.Current.ProgrammableKeys with { Columns = 4, Rows = 5 };
            Assert.Equal(expanded.Key10, expanded.Get(ProgrammableKeySettings.SlotAt(4, 1)));
            Assert.Equal((1, 1), ((expanded with { Columns = 0, Rows = -1 }).Normalize().Columns,
                (expanded with { Columns = 0, Rows = -1 }).Normalize().Rows));
            Assert.Equal(expanded, (expanded with { Columns = 10, Rows = 10 }).Normalize());
            var good = desktop.Current;
            File.WriteAllText(path, "{broken");
            Assert.False(desktop.Update(s => SettingsControls.Apply(SettingsAction.ShortcutCtrl, s)));
            Assert.Equal(good, desktop.Current);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SettingsSelectionAndKeyPickerKeepTheSelectedSlot()
    {
        var pointers = new SettingsPointerState(); double time = 1;
        SettingsAction? Click(SettingsAction action)
        {
            var b = SettingsControls.ForPage(pointers.Page).Single(c => c.Action == action).Bounds;
            pointers.Process(7, b.X + 5, b.Y + 5, time, time, down: true); time += .1;
            var result = pointers.Process(7, b.X + 5, b.Y + 5, time, time, up: true); time += .1;
            return result;
        }
        Click(SettingsAction.ShortcutsTab); Click(SettingsAction.Slot5);
        Assert.Equal(4, pointers.ShortcutSlot);
        Click(SettingsAction.ChooseShortcutKey);
        Assert.Equal(SettingsPage.ShortcutKey, pointers.Page);
        if (Environment.GetEnvironmentVariable("GOBOARD_SHORTCUT_ARTIFACTS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var picker = SettingsPanel.Render(new(), pointers);
            using var image = SKImage.FromBitmap(picker);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(directory, "shortcut-key-picker.png"));
            data.SaveTo(file);
        }
        var choice = SettingsAction.KeyChoiceFirst + 0x2e;
        var action = Click(choice);
        Assert.Equal(SettingsPage.Shortcuts, pointers.Page);
        var settings = SettingsControls.Apply(action.Value, new(), pointers.ShortcutSlot);
        Assert.Equal(0x2e, settings.ProgrammableKeys.Key5.Scan);
        Assert.Equal(ProgrammableKeys.Presets[0].Shortcut, settings.ProgrammableKeys.Key1);
        foreach (var page in Enum.GetValues<SettingsPage>())
        foreach (var control in SettingsControls.ForPage(page))
        {
            Assert.InRange(control.Bounds.Y + control.Bounds.Height, 0, SettingsControls.Height);
            Assert.InRange(control.Bounds.X + control.Bounds.Width, 0, SettingsControls.Width);
        }
    }

}
