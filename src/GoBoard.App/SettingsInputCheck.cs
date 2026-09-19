using System.Runtime.InteropServices;
using GoBoard.Core;

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
                    var b = SettingsControls.All.Concat(SettingsControls.Tabs).Concat(SettingsControls.Effects).Single(c => c.Action == action).Bounds;
                    return new((int)(v.X + (b.X + b.Width / 2) * v.Scale), (int)(v.Y + (b.Y + b.Height / 2) * v.Scale));
                }
                void Mouse(uint message, Point point) => SendMessage(form.Handle, message, message == 0x201 ? 1 : 0,
                    (nint)((point.Y << 16) | (point.X & 0xffff)));
                void Click(SettingsAction action)
                {
                    Mouse(0x201, Center(action));
                    Mouse(0x202, Center(action));
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
                Require(store.Current.Effects.Spotlight && store.Current.Effects.Transition == CharacterTransition.Lift &&
                    store.Current.Effects.TransitionMs == 320, "Effects tab controls");
                Require(new SettingsStore(path).Current == store.Current, "Saved effects selection and duration");
                Click(SettingsAction.ResetEffects);
                Require(store.Current == general, "Effects reset preserves general preferences");
                Click(SettingsAction.GeneralTab);
            }
            Console.WriteLine("Shared settings input check passed: native mouse clicks, resized/letterboxed targets, persistence, themes, sound, desktop position reset, effects tab and reset, disabled controls, and capture cancellation. User settings were untouched.");
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
