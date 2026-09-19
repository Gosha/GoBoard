using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Platform.Windows;

namespace GoBoard.App;

internal static class SettingsInputCheck
{
    // Exercise the actual window with an isolated file, never the user's settings.
    public static int Run()
    {
        ApplicationConfiguration.Initialize();
        var path = Path.Combine(Path.GetTempPath(), "GoBoard.SettingsCheck." + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SettingsStore(path);
            Require(store.Update(_ => new BoardSettings { SoundEnabled = false }), "Create disposable settings");
            using var form = new SettingsForm(store: store);
            using var desktop = new DesktopKeyboardForm(previewOnly: true, store: new SettingsStore(path));
            _ = form.Handle;
            _ = desktop.Handle;
            foreach (var size in new[] { new Size(900, 650), new Size(1200, 650), new Size(560, 850) })
            {
                form.ClientSize = size;
                var v = SettingsViewport.Fit(form.ClientSize.Width, form.ClientSize.Height);
                Point Center(SettingsAction action)
                {
                    var b = form.ControlFor(action).Bounds;
                    return new((int)(v.X + (b.X + b.Width / 2) * v.Scale), (int)(v.Y + (b.Y + b.Height / 2) * v.Scale));
                }
                void Mouse(uint message, Point point) => SendMessage(form.Handle, message, message == 0x201 ? 1 : 0,
                    (nint)((point.Y << 16) | (point.X & 0xffff)));
                void Click(SettingsAction action)
                {
                    Mouse(0x201, Center(action));
                    Mouse(0x202, Center(action));
                }
                void Preview(string grid)
                {
                    if (Environment.GetEnvironmentVariable("GOBOARD_SHORTCUT_ARTIFACTS") is not { Length: > 0 } directory) return;
                    Directory.CreateDirectory(directory);
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(directory, $"desktop-shortcut-settings-{grid}-{size.Width}x{size.Height}.png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                }
                var before = store.Current.SizePercent;
                Click(SettingsAction.Larger);
                Require(store.Current.SizePercent == before + 5, "Scaled size button");
                Require(new SettingsStore(path).Current == store.Current, "Saved click");
                var geometry = store.Current.Geometry;
                Click(SettingsAction.Geometry);
                Require(store.Current.Geometry == (KeyboardGeometry)(((int)geometry + 1) % 3), "Arrangement selection");
                Require(new SettingsStore(path).Current.Geometry == store.Current.Geometry, "Saved arrangement");
                foreach (var (action, sound) in new[] { (SettingsAction.Thud, KeySound.SoftLowThud),
                    (SettingsAction.CherryBlue, KeySound.CherryMxBlue),
                    (SettingsAction.GateronYellow, KeySound.GateronYellowPairs), (SettingsAction.Wood, KeySound.CushionedWood) })
                {
                    Click(action);
                    Require(store.Current.Sound == sound, "Direct preset selection");
                    Require(new SettingsStore(path).Current.Sound == sound, "Saved sound preset");
                    Click(action);
                    Require(store.Current.Sound == sound, "Preview preserves selection");
                }
                Click(SettingsAction.SteamFlat);
                Require(store.Current.Theme == BoardThemes.SteamFlat, "Flat theme selection");
                Require(new SettingsStore(path).Current.Theme == BoardThemes.SteamFlat, "Saved theme selection");
                Click(SettingsAction.SteamSoft);
                Require(store.Current.Theme == BoardThemes.SteamSoft, "Soft theme selection");
                Click(SettingsAction.Louder);
                Require(store.Current.VolumePercent == 100, "Disabled volume button");
                Mouse(0x201, Center(SettingsAction.Larger));
                Mouse(0x200, new Point(1, 1));
                Mouse(0x202, Center(SettingsAction.Larger));
                Require(store.Current.SizePercent == before + 5, "Drag-away cancellation");
                Mouse(0x201, Center(SettingsAction.Larger));
                form.Capture = false;
                Mouse(0x202, Center(SettingsAction.Larger));
                Require(store.Current.SizePercent == before + 5, "Capture-loss cancellation");
                desktop.RefreshSettings();
                var beforeReset = store.Current;
                desktop.Location = new Point(-10000, -10000);
                Click(SettingsAction.ResetPosition);
                Require(store.Current.PositionResetId != beforeReset.PositionResetId, "Position reset request");
                Require(store.Current with { PositionResetId = beforeReset.PositionResetId } == beforeReset,
                    "Position reset preserves preferences");
                desktop.RefreshSettings();
                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                Require(desktop.Left == area.Left + (area.Width - desktop.Width) / 2 &&
                    desktop.Top == Math.Max(area.Top, area.Bottom - desktop.Height - 24), "Desktop position restored");
                desktop.Location = new Point(area.Left + 30, area.Top + 30);
                var moved = desktop.Location;
                desktop.RefreshSettings();
                Require(desktop.Location == moved, "Position reset is consumed once");
                var general = store.Current;
                Click(SettingsAction.EffectsTab);
                Click(SettingsAction.Spotlight);
                Click(SettingsAction.Lift);
                Click(SettingsAction.TransitionSlower);
                Require(!store.Current.Effects.Spotlight && store.Current.Effects.Transition == CharacterTransition.Lift &&
                    store.Current.Effects.TransitionMs == 180, "Effects tab controls");
                Require(new SettingsStore(path).Current == store.Current, "Saved effects selection and duration");
                Click(SettingsAction.ResetEffects);
                Require(store.Current == general, "Effects reset preserves general preferences");
                Click(SettingsAction.ShortcutsTab);
                Click(SettingsAction.ToggleShortcuts);
                Click(SettingsAction.Slot3);
                Click(SettingsAction.ShortcutCtrl);
                Click(SettingsAction.ChooseShortcutKey);
                Click(SettingsAction.KeyChoiceFirst + 0x1e);
                Require(store.Current.ProgrammableKeys.Enabled && store.Current.ProgrammableKeys.Key3 == new KeyboardShortcut(0x1e, Ctrl: true),
                    "Custom shortcut editor");
                Require(new SettingsStore(path).Current == store.Current, "Saved custom shortcut");
                desktop.RefreshSettings();
                Require(desktop.Shortcuts.State.Keys.Single(k => k.Id == "Shortcut3").Shortcut == store.Current.ProgrammableKeys.Key3,
                    "Desktop receives edited shortcut");
                var layout = WindowsLayoutProvider.Get(WindowsKeyboard.Foreground().Layout, store.Current.Geometry);
                var presetIndex = Array.FindIndex(ProgrammableKeys.Presets, p => p.ForLayout(layout) == store.Current.ProgrammableKeys.Key3);
                Click(SettingsAction.NextPreset);
                Require(store.Current.ProgrammableKeys.Key3 == ProgrammableKeys.Presets[(presetIndex + 1) % ProgrammableKeys.Presets.Length].ForLayout(layout), "Assign next preset from custom shortcut");
                Click(SettingsAction.MoreRows);
                Require(store.Current.ProgrammableKeys.Rows == 5, "Add fifth row");
                Click(SettingsAction.Slot10); Click(SettingsAction.NextPreset);
                Preview("2x5");
                var tenth = store.Current.ProgrammableKeys.Key10;
                Click(SettingsAction.FewerColumns);
                Require(store.Current.ProgrammableKeys.Columns == 1, "Remove second column");
                Preview("1x5");
                Click(SettingsAction.MoreColumns);
                Require(store.Current.ProgrammableKeys.Key10 == tenth, "Restore hidden column assignments");
                Click(SettingsAction.FewerRows);
                Require(store.Current.ProgrammableKeys.Rows == 4, "Remove fifth row");
                Click(SettingsAction.MoreRows);
                Click(SettingsAction.MoreColumns); Click(SettingsAction.MoreColumns);
                Require(store.Current.ProgrammableKeys.Columns == 4 && store.Current.ProgrammableKeys.Rows == 5, "Expand to twenty keys");
                Click(SettingsAction.Slot20); Click(SettingsAction.NextPreset);
                var twentieth = store.Current.ProgrammableKeys.Key20;
                Require(twentieth == ProgrammableKeys.Presets[0].Shortcut, "Edit twentieth key");
                Require(new SettingsStore(path).Current.ProgrammableKeys.Key20 == twentieth, "Save twentieth key");
                Preview("4x5");
                for (var i = 0; i < 3; i++) Click(SettingsAction.FewerColumns);
                for (var i = 0; i < 4; i++) Click(SettingsAction.FewerRows);
                Require(store.Current.ProgrammableKeys.Columns == 1 && store.Current.ProgrammableKeys.Rows == 1, "Shrink to one key");
                Preview("1x1");
                Click(SettingsAction.FewerColumns); Click(SettingsAction.FewerRows);
                Require(store.Current.ProgrammableKeys.Columns == 1 && store.Current.ProgrammableKeys.Rows == 1, "Minimum grid controls disabled");
                for (var i = 0; i < 3; i++) Click(SettingsAction.MoreColumns);
                for (var i = 0; i < 4; i++) Click(SettingsAction.MoreRows);
                Click(SettingsAction.MoreColumns); Click(SettingsAction.MoreRows);
                Require(store.Current.ProgrammableKeys.Columns == 4 && store.Current.ProgrammableKeys.Rows == 5, "Maximum grid controls disabled");
                Require(store.Current.ProgrammableKeys.Key20 == twentieth && store.Current.ProgrammableKeys.Key10 == tenth, "Restore hidden grid assignments");
                Click(SettingsAction.Slot20);
                for (var i = 0; i < Array.FindIndex(ProgrammableKeys.Presets, p => p.Shortcut.Scan == ProgrammableKeys.MediaStop); i++) Click(SettingsAction.NextPreset);
                Require(store.Current.ProgrammableKeys.Key20.Scan == ProgrammableKeys.MediaStop, "Choose Stop preset");
                Click(SettingsAction.ChooseShortcutKey);
                Click(SettingsAction.KeyChoiceFirst + ProgrammableKeys.MediaStop);
                Require(new SettingsStore(path).Current.ProgrammableKeys.Key20 == new KeyboardShortcut(ProgrammableKeys.MediaStop), "Save Stop from key picker");
                foreach (var scan in new[] { ProgrammableKeys.VolumeMute, ProgrammableKeys.BrowserBack, ProgrammableKeys.NumEnter, (ushort)0x52 })
                {
                    Click(SettingsAction.ChooseShortcutKey);
                    Preview("expanded-picker");
                    Click(SettingsAction.KeyChoiceFirst + scan);
                    Require(new SettingsStore(path).Current.ProgrammableKeys.Key20.Scan == scan, "Save extra key from picker");
                }
                Click(SettingsAction.ResetShortcuts);
                Click(SettingsAction.ToggleShortcuts);
                Require(store.Current == general, "Shortcut reset preserves other settings");
                Click(SettingsAction.GeneralTab);
            }
            Console.WriteLine("Shared settings input check passed: native mouse clicks, resized/letterboxed targets, persistence, themes, sound, desktop position reset, effects, preset/custom shortcut editing and desktop propagation, disabled controls, and capture cancellation. User settings were untouched.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Settings input check: {ex.Message}"); return 1; }
        finally { File.Delete(path); }
    }
    private static void Require(bool success, string operation)
    {
        if (!success) throw new InvalidOperationException(operation + " failed.");
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
