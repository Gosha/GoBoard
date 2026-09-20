using GoBoard.Core;
using Xunit;

namespace GoBoard.Tests;

public sealed class SettingsEditorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GoBoard.SettingsEditor.Tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(directory, "settings.json");
    private WindowsLayout layout = new((nint)WindowsLayout.UsHandle);
    private double time;

    private SettingsEditor Editor(SettingsStore store) => new(store, _ => layout);
    private SettingsControl Control(SettingsEditor editor, SettingsAction action) =>
        SettingsControls.ForPage(editor.Pointers.Page, editor.Current, editor.Pointers.Layout, editor.Pointers.ShortcutSlot)
            .Single(c => c.Action == action);
    private SettingsEditorEffect Edge(SettingsEditor editor, SettingsAction action, bool down = false, bool up = false, uint device = 0)
    {
        var b = Control(editor, action).Bounds;
        time += .05;
        return editor.Process(device, b.X + b.Width / 2, b.Y + b.Height / 2, time, time, down, up);
    }
    private SettingsEditorEffect Click(SettingsEditor editor, SettingsAction action, uint device = 0)
    {
        Assert.Equal(SettingsEditorEffect.None, Edge(editor, action, down: true, device: device));
        return Edge(editor, action, up: true, device: device);
    }

    [Fact]
    public void EditsMergeOtherHostsChangesAndRecheckLimitsAgainstTheLatestFile()
    {
        var store = new SettingsStore(SettingsPath);
        var editor = Editor(store);
        var other = new SettingsStore(SettingsPath);
        Assert.True(other.Update(s => s with { SizePercent = 145, Theme = BoardThemes.SteamFlat }));

        Assert.Equal(SettingsEditorEffect.None, Click(editor, SettingsAction.Larger));
        Assert.Equal(150, editor.Current.SizePercent);
        Assert.Equal(BoardThemes.SteamFlat, editor.Current.Theme);
        Assert.Equal(editor.Current, new SettingsStore(SettingsPath).Current);

        // The host still displays an enabled button when another editor reaches
        // the limit after the last reload. Its release must preserve that edit.
        Assert.True(store.Update(s => s with { SizePercent = 100 }));
        Edge(editor, SettingsAction.Larger, down: true);
        Assert.True(other.Update(s => s with { SizePercent = 150, VolumePercent = 30 }));
        Assert.Equal(SettingsEditorEffect.None, Edge(editor, SettingsAction.Larger, up: true));
        Assert.Equal(150, editor.Current.SizePercent);
        Assert.Equal(30, editor.Current.VolumePercent);
    }

    [Fact]
    public void SaveFailureKeepsAppliedSettingsAndSuppressesAuditionUntilASuccessfulEdit()
    {
        var store = new SettingsStore(SettingsPath);
        Assert.True(store.Update(s => s));
        var editor = Editor(store);
        var original = File.ReadAllText(SettingsPath);
        var before = editor.Current;
        File.WriteAllText(SettingsPath, "{broken");

        Assert.Equal(SettingsEditorEffect.None, Click(editor, SettingsAction.CherryBlue));
        Assert.Equal(before, editor.Current);
        Assert.Contains("Settings were not saved", editor.Error);
        Assert.Equal("{broken", File.ReadAllText(SettingsPath));

        // Reloading a repaired file must not silently clear the failed-action
        // feedback; the next successful edit clears it in both hosts.
        File.WriteAllText(SettingsPath, original);
        store.Reload();
        editor.Refresh();
        Assert.NotNull(editor.Error);
        Assert.Equal(SettingsEditorEffect.AuditionSound, Click(editor, SettingsAction.CherryBlue));
        Assert.Null(editor.Error);
        Assert.Equal(KeySound.CherryMxBlue, new SettingsStore(SettingsPath).Current.Sound);
        Assert.Equal(SettingsEditorEffect.AuditionSound, Click(editor, SettingsAction.CherryBlue));
    }

    [Fact]
    public void NavigationAutostartAndDisabledControlsDoNotWritePreferences()
    {
        var store = new SettingsStore(SettingsPath);
        Assert.True(store.Update(s => s with { SizePercent = 150, SoundEnabled = false }));
        var editor = Editor(store);
        var saved = File.ReadAllText(SettingsPath);
        // An exclusive reader permits reads but makes any unnecessary replace
        // fail, so this detects writes even when serialization is unchanged.
        using var guard = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Equal(SettingsEditorEffect.None, Click(editor, SettingsAction.Larger));
        Assert.Equal(SettingsEditorEffect.None, Click(editor, SettingsAction.Quieter));
        Assert.Equal(SettingsEditorEffect.ToggleAutostart, Click(editor, SettingsAction.Autostart));
        Assert.Equal(SettingsEditorEffect.None, Click(editor, SettingsAction.ShortcutsTab));
        Assert.Equal(SettingsEditorEffect.None, Click(editor, SettingsAction.ChooseShortcutPreset));
        Assert.Equal(SettingsEditorEffect.None, Click(editor, SettingsAction.BackToShortcut));
        Assert.Equal(SettingsPage.Shortcuts, editor.Pointers.Page);
        Assert.Null(editor.Error);
        Assert.Equal(saved, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void GridEditReconfiguresSelectionAndCancelsTheOtherHandsOldCaptureImmediately()
    {
        var store = new SettingsStore(SettingsPath);
        var editor = Editor(store);
        Click(editor, SettingsAction.ShortcutsTab);
        Click(editor, SettingsAction.Slot2);
        Assert.Equal(1, editor.Pointers.ShortcutSlot);
        var second = editor.Current.ProgrammableKeys.Key2;
        var first = editor.Current.ProgrammableKeys.Key1;
        Edge(editor, SettingsAction.ShortcutCtrl, down: true, device: 7);

        Click(editor, SettingsAction.FewerColumns, device: 8);
        Assert.Equal(0, editor.Pointers.ShortcutSlot);
        Assert.Equal(SettingsEditorEffect.None, Edge(editor, SettingsAction.ShortcutCtrl, up: true, device: 7));
        Assert.Equal(first, editor.Current.ProgrammableKeys.Key1);
        Assert.Equal(second, editor.Current.ProgrammableKeys.Key2);

        Click(editor, SettingsAction.ShortcutCtrl);
        Assert.Equal(first with { Ctrl = !first.Ctrl }, editor.Current.ProgrammableKeys.Key1);
        Assert.Equal(second, editor.Current.ProgrammableKeys.Key2);
    }

    [Fact]
    public void ReloadedGridChangeCancelsPendingSelectionBeforeRelease()
    {
        var store = new SettingsStore(SettingsPath);
        var editor = Editor(store);
        Click(editor, SettingsAction.ShortcutsTab);
        Click(editor, SettingsAction.Slot2);
        Edge(editor, SettingsAction.ShortcutShift, down: true);
        var other = new SettingsStore(SettingsPath);
        Assert.True(other.Update(s => s with { ProgrammableKeys = s.ProgrammableKeys with { Columns = 1 } }));
        store.Reload();

        Assert.Equal(SettingsEditorEffect.None, Edge(editor, SettingsAction.ShortcutShift, up: true));
        Assert.Equal(0, editor.Pointers.ShortcutSlot);
        Assert.Equal(other.Current, editor.Current);
        Assert.Equal(other.Current, new SettingsStore(SettingsPath).Current);
    }

    [Fact]
    public void LayoutChangeAndHostResetCancelPendingClicks()
    {
        var store = new SettingsStore(SettingsPath);
        var editor = Editor(store);
        Edge(editor, SettingsAction.CherryBlue, down: true);
        layout = new((nint)WindowsLayout.SwedishHandle);
        Assert.Equal(SettingsEditorEffect.None, Edge(editor, SettingsAction.CherryBlue, up: true));
        Assert.Same(layout, editor.Pointers.Layout);
        Assert.False(File.Exists(SettingsPath));

        Edge(editor, SettingsAction.CherryBlue, down: true);
        editor.Reset();
        Assert.Equal(SettingsEditorEffect.None, Edge(editor, SettingsAction.CherryBlue, up: true));
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void GeometryEditsRefreshTheObservedLayoutBeforeTheNextEvent()
    {
        var store = new SettingsStore(SettingsPath);
        var ansi = layout;
        var iso = new WindowsLayout((nint)WindowsLayout.SwedishHandle);
        var editor = new SettingsEditor(store, geometry => geometry == KeyboardGeometry.Iso ? iso : ansi);
        Click(editor, SettingsAction.Geometry);
        Click(editor, SettingsAction.Geometry);
        Assert.Equal(KeyboardGeometry.Iso, editor.Current.Geometry);
        Assert.Same(iso, editor.Pointers.Layout);
    }

    [Fact]
    public void PresetAndKeyChoicesPersistOnlyTheSelectedShortcutAndReturnToTheEditor()
    {
        var store = new SettingsStore(SettingsPath);
        var editor = Editor(store);
        Click(editor, SettingsAction.ShortcutsTab);
        Click(editor, SettingsAction.Slot3);
        Click(editor, SettingsAction.ChooseShortcutPreset);
        var index = Array.FindIndex(ProgrammableKeys.Presets, p => p.Label == "Copy");
        var other = new SettingsStore(SettingsPath);
        Assert.True(other.Update(s => s with { ProgrammableKeys = s.ProgrammableKeys.Set(0, new(0x1e, Win: true)) }));
        Assert.Equal(SettingsEditorEffect.None, Click(editor, SettingsAction.PresetChoiceFirst + index));
        Assert.Equal(SettingsPage.Shortcuts, editor.Pointers.Page);
        Assert.Equal(ProgrammableKeys.Presets[index].ForLayout(layout), editor.Current.ProgrammableKeys.Key3);
        Assert.Equal(other.Current.ProgrammableKeys.Key1, editor.Current.ProgrammableKeys.Key1);

        Click(editor, SettingsAction.ChooseShortcutKey);
        Assert.Equal(SettingsEditorEffect.None, Click(editor, SettingsAction.KeyChoiceFirst + 0x1e));
        Assert.Equal(SettingsPage.Shortcuts, editor.Pointers.Page);
        Assert.Equal(new KeyboardShortcut(0x1e, Ctrl: true), new SettingsStore(SettingsPath).Current.ProgrammableKeys.Key3);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
