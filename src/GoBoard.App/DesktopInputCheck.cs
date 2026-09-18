using System.Diagnostics;
using System.Runtime.InteropServices;
using GoBoard.Core;
using GoBoard.Platform.Windows;

namespace GoBoard.App;

// Explicit integration check: input is guarded to disposable text/settings
// windows. The optional shell check opens Start and dismisses it without typing.
internal static class DesktopInputCheck
{
    // Launch this with the same STARTUPINFO as the PowerShell launcher. Unlike
    // Run(), it must not show any disposable window before the keyboard.
    public static int RunLaunch()
    {
        ApplicationConfiguration.Initialize();
        using var keyboard = new DesktopKeyboardForm();
        using var timer = new System.Windows.Forms.Timer { Interval = 300 };
        var stage = 0;
        var result = 1;
        timer.Tick += (_, _) =>
        {
            try
            {
                var p = keyboard.SettingsPoint;
                var packed = (nint)((p.Y << 16) | (p.X & 0xffff));
                if (stage++ == 0) { SendMessage(keyboard.Handle, 0x201, 1, packed); return; }
                if (stage == 2) { SendMessage(keyboard.Handle, 0x202, 0, packed); return; }
                var settings = Application.OpenForms.OfType<SettingsForm>().SingleOrDefault();
                Require(settings != null, "Settings click did not create a settings form.");
                var style = (long)GetWindowLongPtr(settings.Handle, -20);
                Console.WriteLine($"Settings launch: managed visible={settings.Visible}, native visible={IsWindowVisible(settings.Handle)}, taskbar={settings.ShowInTaskbar}, style={style:X}.");
                Require(IsWindowVisible(settings.Handle), "Settings was created but Windows kept it hidden.");
                Require(settings.ShowInTaskbar && (style & 0x40000) != 0 && (style & 0x80) == 0, "Settings has no normal taskbar entry.");
                Require((style & 8) == 0, "Settings is unexpectedly always on top.");
                Console.WriteLine("Desktop launcher check passed: Settings button shows a visible normal window with taskbar style.");
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
            timer.Stop();
            keyboard.Close();
        };
        keyboard.Shown += (_, _) => timer.Start();
        Application.Run(keyboard);
        return result;
    }


    public static int Run(bool shell = false)
    {
        ApplicationConfiguration.Initialize();
        var previous = WindowsKeyboard.Foreground().Window;
        try
        {
            foreach (var vk in new[] { 0x10, 0x11, 0x12, 0x5b, 0x5c })
                if ((GetAsyncKeyState(vk) & 0x8000) != 0) throw new InvalidOperationException("Release physical modifiers before running the check.");
            using var target = new Form { Text = "GoBoard disposable desktop typing check", Size = new Size(550, 230), StartPosition = FormStartPosition.CenterScreen };
            using var text = new TextBox { Multiline = true, Dock = DockStyle.Fill };
            target.Controls.Add(text);
            target.Show();
            FocusTestWindow(target.Handle, text.Handle);
            Pump(100);
            Require(WindowsKeyboard.Foreground().Window == target.Handle, "The disposable text window could not get focus; no input was sent.");
            // Exercise the normal production input path, without a test-only
            // exemption for windows owned by this process.
            using var keyboard = new DesktopKeyboardForm();
            Require(WindowsKeyboard.Foreground().Window == target.Handle, $"Constructing the keyboard changed focus (expected {target.Handle}, actual {WindowsKeyboard.Foreground().Window}, keyboard {keyboard.Handle}).");
            keyboard.Show();
            Require(WindowsKeyboard.Foreground().Window == target.Handle, $"Showing the keyboard stole focus (expected {target.Handle}, keyboard {keyboard.Handle}, actual {WindowsKeyboard.Foreground().Window}).");
            Pump(100);
            Require(keyboard.State.Layout.Supported, "This check requires the current Windows layout to be US or Swedish.");
            Require(WindowsKeyboard.Foreground().Window == target.Handle, "Showing the keyboard stole focus.");
            Require(((long)GetWindowLongPtr(keyboard.Handle, -20) & 0x08000000) != 0, "The keyboard is missing WS_EX_NOACTIVATE.");
            Require(SendMessage(keyboard.Handle, 0x21, target.Handle, (nint)(0x201 << 16 | 1)) == 3, "Mouse activation was not suppressed.");

            var expectedWindow = target.Handle;
            void Guard() => Require(WindowsKeyboard.Foreground().Window == expectedWindow, "Focus left the disposable test window; input check aborted.");
            void Mouse(uint message, Point p)
            {
                Guard();
                SendMessage(keyboard.Handle, message, message == 0x201 ? 1 : 0, (nint)((p.Y << 16) | (p.X & 0xffff)));
                Pump(20);
                Guard();
            }
            void Tap(string id)
            {
                var point = keyboard.KeyPoint(id);
                Mouse(0x200, point);
                Mouse(0x201, point);
                Mouse(0x202, point);
            }
            Tap("Shift");
            Require(keyboard.State.Shift, "Releasing the mouse cleared one-shot Shift.");
            Tap("g"); Tap("o"); Tap("Space"); Tap("Shift"); Tap("b");
            Tap("o"); Tap("a"); Tap("r"); Tap("d"); Tap("x"); Tap("Backspace");
            var expected = WindowsKeyboard.CapsLock ? "gO bOARD" : "Go Board";
            Require(text.Text == expected, $"Desktop text mismatch: expected '{expected}', received '{text.Text}'.");
            Tap("Ctrl"); Tap("a");
            Require(text.SelectionLength == text.TextLength, "Desktop Ctrl+A did not select the text.");
            Tap("x");
            var p = keyboard.KeyPoint("Backspace");
            Mouse(0x201, p);
            Pump(550);
            Mouse(0x202, p);
            Require(text.Text.Length == 0 && !keyboard.State.HasHeldKeys, "Release failed to stop repeat.");
            Tap("Shift"); Tap("Shift");
            Require(keyboard.State.Mode(0x2a) == ModifierMode.Locked, "Locked Shift did not survive two clicks.");
            // Mouse capture stolen mid-press must cancel armed modifiers/repeat.
            Mouse(0x201, keyboard.KeyPoint("a"));
            SendMessage(keyboard.Handle, 0x215, 0, text.Handle);
            Require(!keyboard.State.HasHeldKeys && !keyboard.State.Shift && !keyboard.HasOwnedKeys, "Capture loss left a chord active.");

            // Open Settings through the real keyboard button. It has no text
            // control, and shares the keyboard's process (the old blocked case).
            Mouse(0x201, keyboard.SettingsPoint);
            var settingsPoint = keyboard.SettingsPoint;
            Guard();
            SendMessage(keyboard.Handle, 0x202, 0, (nint)((settingsPoint.Y << 16) | (settingsPoint.X & 0xffff)));
            Pump(100);
            var settings = Application.OpenForms.OfType<SettingsForm>().Single();
            expectedWindow = settings.Handle;
            Guard();
            var downs = 0;
            var ups = 0;
            settings.KeyDown += (_, e) => { if (e.KeyCode == Keys.F8) downs++; };
            settings.KeyUp += (_, e) => { if (e.KeyCode == Keys.F8) ups++; };
            Tap("F8");
            Require(downs == 1 && ups == 1, "Settings without a text box did not receive a balanced F8 stroke.");
            Tap("Win");
            Require(keyboard.State.Mode(0xe05b) == ModifierMode.OneShot, "Windows key did not arm while Settings was focused.");
            if (shell)
            {
                // This press intentionally changes foreground focus. Only send
                // dismissal input after identifying the actual Start process.
                Guard();
                var winPoint = keyboard.KeyPoint("Win");
                var packed = (nint)((winPoint.Y << 16) | (winPoint.X & 0xffff));
                SendMessage(keyboard.Handle, 0x201, 1, packed);
                SendMessage(keyboard.Handle, 0x202, 0, packed);
                var wait = Stopwatch.StartNew();
                InputTarget start = default;
                var matched = false;
                var observed = "Settings";
                do
                {
                    Pump(50);
                    start = WindowsKeyboard.Foreground();
                    if (start.Window == 0 || start.Window == settings.Handle) continue;
                    using var process = Process.GetProcessById((int)start.Process);
                    observed = process.ProcessName;
                    matched = process.ProcessName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase);
                } while (!matched && wait.ElapsedMilliseconds < 3000);
                Require(matched, $"Double Windows click from Settings did not focus Start; observed {observed}; no dismissal input sent.");
                Require(!keyboard.HasOwnedKeys && (GetAsyncKeyState(0x5b) & 0x8000) == 0, "Opening Start left the Windows key held.");
                using var output = new WindowsKeyboard { Target = start };
                output.Stroke(0x01, []);
                Pump(200);
                Console.WriteLine("Desktop shell check passed: double Windows click from Settings opened Start; Escape sent to dismiss it.");
            }
            keyboard.Hide();
            Require(!keyboard.State.HasHeldKeys && !keyboard.HasOwnedKeys, "Hiding the keyboard left input active.");
            Require(!Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("openvr_api.dll", StringComparison.OrdinalIgnoreCase)),
                "Desktop mode loaded the OpenVR native library.");
            Console.WriteLine("Desktop input check passed: native mouse messages, focus preservation, text, Shift, Ctrl+A, repeat release, capture-loss cancellation, Settings input without a text box, Windows-key arming, and cleanup; no SteamVR required.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Desktop input check: {ex.Message}"); return 1; }
        finally { if (previous != 0) SetForegroundWindow(previous); }
    }
    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(1); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void FocusTestWindow(nint window, nint edit)
    {
        // Test harness only; normal desktop typing never changes foreground focus.
        var other = GetWindowThreadProcessId(WindowsKeyboard.Foreground().Window, out _);
        var current = GetCurrentThreadId();
        var attached = other != 0 && other != current && AttachThreadInput(current, other, true);
        try { SetForegroundWindow(window); SetFocus(edit); }
        finally { if (attached) AttachThreadInput(current, other, false); }
    }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
}
