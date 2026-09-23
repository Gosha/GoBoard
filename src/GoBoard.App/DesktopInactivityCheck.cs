using System.Diagnostics;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Platform.Windows;

namespace GoBoard.App;

// Explicit native check: disposable windows/settings, no keyboard injection.
internal static class DesktopInactivityCheck
{
    private const int SW_SHOW = 5;
    public static int Run()
    {
        ApplicationConfiguration.Initialize();
        var previous = WindowsKeyboard.Foreground().Window;
        var cursor = Cursor.Position;
        var path = Path.Combine(Path.GetTempPath(), $"GoBoard-idle-check-{Guid.NewGuid():N}.json");
        try
        {
            using var target = new Form { Text = "GoBoard disposable inactivity check", StartPosition = FormStartPosition.Manual,
                Bounds = Screen.FromPoint(cursor).WorkingArea };
            target.Show();
            ShowWindow(target.Handle, SW_SHOW); // Consume a launcher's initial SW_HIDE hint.
            var foregroundThread = GetWindowThreadProcessId(WindowsKeyboard.Foreground().Window, out _);
            var currentThread = GetCurrentThreadId();
            var attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
            try { SetForegroundWindow(target.Handle); }
            finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
            var store = new SettingsStore(path);
            using var keyboard = new DesktopKeyboardForm(store: store);
            keyboard.Show();
            void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
            void Pump(int milliseconds)
            {
                var timer = Stopwatch.StartNew();
                do
                {
                    Application.DoEvents();
                    Require(WindowsKeyboard.Foreground().Window == target.Handle, "Focus left the disposable target; inactivity check stopped.");
                    Thread.Sleep(5);
                } while (timer.ElapsedMilliseconds < milliseconds);
            }
            Pump(80);
            var original = keyboard.Bounds;
            foreach (var mode in new[] { InactivityMode.Hide, InactivityMode.Minimize, InactivityMode.Transparent })
            {
                Cursor.Position = new(target.Left + 20, target.Top + 50);
                Require(store.Update(s => s with { Inactivity = new() { Mode = mode, DelayMs = 250, TransitionMs = 50, RevealMs = 200 } }), "Could not save disposable settings.");
                keyboard.RefreshSettings();
                Pump(500);
                Require(keyboard.Inactivity.Dormant && !keyboard.Visible, $"{mode} failed to become idle.");
                Require(keyboard.Inactivity.RevealWindow.Visible, $"{mode} hid the reveal prompt.");
                Require(!keyboard.HasOwnedKeys && !keyboard.State.HasHeldKeys, "Idle keyboard retained owned input.");
                Require(keyboard.FloatingControls.Windows.All(w => !w.Visible), "Idle keyboard retained floating controls.");
                var image = keyboard.Inactivity.IdleWindow;
                if (mode == InactivityMode.Hide) Require(!image.Visible, "Hide mode retained the keyboard image.");
                else
                {
                    Require(image.Visible, $"{mode} did not show its idle image.");
                    var center = new Point(image.Left + image.Width / 2, image.Top + image.Height / 2);
                    var hit = WindowFromPoint(center);
                    Require(hit != image.Handle && hit != keyboard.Handle, $"{mode} blocked the window behind it.");
                    if (mode == InactivityMode.Minimize) Require(image.Width < original.Width / 2, "Minimize did not shrink the keyboard.");
                    else Require(image.Opacity is > .14 and < .16, "Transparent mode did not apply idle opacity.");
                }
                var prompt = keyboard.Inactivity.RevealWindow;
                if (Environment.GetEnvironmentVariable("GOBOARD_INACTIVITY_ARTIFACTS") is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                    var bounds = Rectangle.Union(original, prompt.Bounds);
                    using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.FromArgb(28, 30, 34));
                        foreach (var surface in new[] { image, prompt }.Where(w => w.Visible))
                        {
                            using var rendered = new Bitmap(surface.Width, surface.Height);
                            surface.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
                            using var attributes = new System.Drawing.Imaging.ImageAttributes();
                            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = (float)surface.Opacity });
                            graphics.DrawImage(rendered, new Rectangle(surface.Left - bounds.Left, surface.Top - bounds.Top, surface.Width, surface.Height),
                                0, 0, rendered.Width, rendered.Height, GraphicsUnit.Pixel, attributes);
                        }
                    }
                    bitmap.Save(Path.Combine(directory, $"inactivity-{mode.ToString().ToLowerInvariant()}.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                // Hover the actual miniature, away from the floating prompt.
                // Full-size transparent keyboards must stay passive at this point.
                var imageCenter = new Point(image.Left + image.Width / 2, image.Top + image.Height / 2);
                if (mode == InactivityMode.Transparent)
                {
                    Cursor.Position = imageCenter;
                    Pump(350);
                    Require(keyboard.Inactivity.Dormant, "Transparent keyboard incorrectly revealed on image hover.");
                }
                var revealPoint = mode == InactivityMode.Minimize ? imageCenter :
                    new Point(prompt.Left + prompt.Width / 2, prompt.Top + prompt.Height / 2);
                if (mode == InactivityMode.Minimize) Require(!prompt.Bounds.Contains(revealPoint), "Miniature check overlaps the prompt.");
                Cursor.Position = revealPoint;
                Pump(60);
                Require(keyboard.Inactivity.Dormant, "Reveal ignored its hover delay.");
                var revealWait = Stopwatch.StartNew();
                while (keyboard.Inactivity.Dormant || !keyboard.Visible)
                {
                    Pump(20);
                    Require(Cursor.Position == revealPoint, "Mouse moved away from the disposable reveal target; check stopped.");
                    Require(revealWait.ElapsedMilliseconds < 1500,
                        $"{mode} failed to reveal: idle={keyboard.Inactivity.Dormant}, visible={keyboard.Visible}.");
                }
                Require(keyboard.Bounds == original, "Reveal changed saved position or size.");
                Pump(350);
                Require(!keyboard.Inactivity.Dormant, "Stationary hover incorrectly counted as inactivity.");
            }
            Require(store.Update(s => s with { Inactivity = new() }), "Could not reset disposable settings.");
            keyboard.RefreshSettings(); Pump(80);
            Require(!keyboard.Inactivity.RevealWindow.Visible && keyboard.Visible, "Off did not restore ordinary visibility.");
            keyboard.Close();
            Console.WriteLine("Desktop inactivity check passed: Hide, Minimize and Transparent; miniature hover reveal, passive transparent image, prompt hover, reveal delay, stationary hover, click-through, controls, position restoration, focus preservation and Off. No keys injected or user settings changed.");
            return 0;
        }
        finally
        {
            Cursor.Position = cursor;
            if (previous != 0) SetForegroundWindow(previous);
            File.Delete(path);
        }
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
}
