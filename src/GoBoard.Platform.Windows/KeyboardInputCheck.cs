using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GoBoard.Core;

namespace GoBoard.Platform.Windows;

// Text tests use our disposable window. The separate shell check opens and
// dismisses Run/Start without commands or input to any existing user document.
internal static class KeyboardInputCheck
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProc(nint window, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style; public WindowProc Procedure; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string Menu, Name;
    }
    private static readonly WindowProc Procedure = DefWindowProc;
    private static Action<Message> observe;
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public nint Window; public uint Id; public nuint WParam; public nint LParam;
        public uint Time; public int X, Y; public uint Private;
    }

    public static void Run(bool shell = false, bool layouts = false)
    {
        foreach (var vk in new[] { 0x10, 0x11, 0x12, 0x5b, 0x5c })
            if ((GetAsyncKeyState(vk) & 0x8000) != 0) throw new InvalidOperationException("Release physical modifiers before running the input check.");
        var previous = WindowsKeyboard.Foreground().Window;
        var previousLayout = GetKeyboardLayout(0);
        var cls = new WindowClass { Procedure = Procedure, Instance = GetModuleHandle(null), Name = "GoBoardInputCheck" };
        if (RegisterClass(ref cls) == 0) throw new InvalidOperationException("Could not register input-check window.");
        if (LoadLibrary("Msftedit.dll") == 0) throw new InvalidOperationException("Could not load Windows Rich Edit.");
        var frame = CreateWindowEx(0, cls.Name, "GoBoard disposable shortcut check", 0x10cf0000, 150, 150, 600, 300, 0, 0, cls.Instance, 0);
        if (frame == 0) throw new InvalidOperationException("Could not create input-check window.");
        using var output = new WindowsKeyboard();
        try
        {
            var edit = CreateWindowEx(0, "RICHEDIT50W", "", 0x50801044, 12, 12, 555, 225, frame, 0, 0, 0);
            if (edit == 0) throw new InvalidOperationException("Could not create input-check text field.");
            FocusTestWindow(frame, edit); Pump(100);
            var loaded = new nint[32];
            var loadedCount = GetKeyboardLayoutList(loaded.Length, loaded);
            var us = loaded.Take(loadedCount).FirstOrDefault(h => unchecked((uint)(long)h) == WindowsLayout.UsHandle);
            if (us == 0) throw new InvalidOperationException("US layout must already be loaded for this test; no language settings were changed.");
            ActivateKeyboardLayout(us, 0); Pump(20); // This test thread only; restored in finally.
            output.Target = WindowsKeyboard.Foreground();
            if (output.Target.Window != frame) throw new InvalidOperationException("Input-check window could not get focus; no test input was sent.");
            var caps = WindowsKeyboard.CapsLock;
            var state = new KeyboardState(output);
            state.SetLayout(new WindowsLayout(output.Target.Layout), Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            var received = new List<(int Key, bool Down, bool Ctrl, bool Shift, bool Extended)>();
            observe = m =>
            {
                if (m.Window == edit && m.Id is 0x100 or 0x101 or 0x104 or 0x105)
                    received.Add(((int)m.WParam, m.Id is 0x100 or 0x104, (GetKeyState(0x11) & 0x8000) != 0,
                        (GetKeyState(0x10) & 0x8000) != 0, ((long)m.LParam & (1L << 24)) != 0));
            };
            static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            state.Enter(0, 7, Now()); state.Enter(1, 8, Now());
            Pump(5);
            void Press(string id, uint cursor = 1, uint device = 8)
            {
                var current = WindowsKeyboard.Foreground();
                if (current != output.Target)
                    throw new InvalidOperationException($"Before {id}: input target changed from {output.Target} to {current} ({WindowsKeyboard.TargetName(current)}).");
                var b = state.Layout.Keys.Single(k => k.Id == id).Bounds;
                if (!state.Press(cursor, device, b.X + b.Width / 2, OverlayGeometry.PanelHeight - b.Y - b.Height / 2, Now(), Now()))
                    throw new InvalidOperationException($"Integration test press rejected: {id}");
                Pump(8);
            }
            void Up(uint cursor = 1, uint device = 8) { state.Up(cursor, device, Now()); Pump(8); }
            void Tap(string id) { Press(id); Up(); }
            if (!shell && !layouts)
            {
                Tap("Shift"); Tap("g");
                Tap("o"); Tap("Shift"); Tap("b");
                foreach (var id in new[] { "o", "a", "r", "d", "Space", "t", "e", "s", "t", "Enter", "x", "Backspace" }) Tap(id);
                Press("Shift", 0, 7);
                state.Cancel(Now()); Pump(15); // Dashboard-hide/focus-change cleanup.
                state.Enter(1, 8, Now());
                Tap("x");
                Tap("Space"); Tap("Shift"); Tap("Shift"); Tap("a"); Tap("b"); Tap("Shift"); Tap("c");
                var expected = "GoBoard test\r\nx ABc";
                if (caps) expected = new string(expected.Select(c => char.IsLetter(c) ? (char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)) : c).ToArray());
                var text = new StringBuilder(512);
                GetWindowText(edit, text, text.Capacity);
                if (text.ToString().Replace("\r\n", "\n").Replace('\r', '\n') != expected.Replace("\r\n", "\n")) throw new InvalidOperationException($"Input-check text mismatch. Expected {expected.Replace("\r\n", "\\n")}; got {text.ToString().Replace("\r\n", "\\n")}.");
                SetWindowText(edit, "alpha beta gamma");
                SendMessage(edit, 0xb1, -1, -1);
                Tap("Ctrl"); Tap("Left");
                void Selection(int start, int end, string operation)
                {
                    var packed = (long)SendMessage(edit, 0xb0, 0, 0);
                    if ((packed & 0xffff) != start || ((packed >> 16) & 0xffff) != end)
                        throw new InvalidOperationException($"{operation} selected {packed & 0xffff}..{(packed >> 16) & 0xffff}, expected {start}..{end}.");
                }
                Selection(11, 11, "Ctrl+Left");
                Tap("Ctrl"); Tap("Shift"); Tap("Left"); Selection(6, 11, "Ctrl+Shift+Left");
                // Rich Edit includes its final paragraph mark in Select All.
                Tap("Ctrl"); Tap("a"); Selection(0, 17, "Ctrl+A");
                received.Clear(); Tap("Ctrl"); Tap("Shift"); Tap("f");
                if (!received.Any(e => e.Key == 0x46 && e.Down && e.Ctrl && e.Shift))
                    throw new InvalidOperationException("Windows did not deliver Ctrl+Shift+F with both modifiers active.");
                received.Clear();
                for (var i = 1; i <= 12; i++) Tap($"F{i}");
                for (var i = 1; i <= 12; i++)
                    if (received.Count(e => e.Key == 0x6f + i && e.Down) != 1 || received.Count(e => e.Key == 0x6f + i && !e.Down) != 1)
                        throw new InvalidOperationException($"F{i} did not arrive as exactly one down/up pair.");
                Tap("Escape");
                received.Clear(); Tap("Left");
                if (!received.Any(e => e.Key == 0x25 && e.Down && e.Extended)) throw new InvalidOperationException("Arrow key lost its extended flag.");
                if (output.HasOwnedKeys || (GetAsyncKeyState(0x10) & 0x8000) != 0) throw new InvalidOperationException("Input check left keys held.");
                Console.WriteLine("Windows input check passed: text, one-shot/locked Shift, Ctrl+A selection, Ctrl+Left word motion, Ctrl+Shift+Left selection, received Ctrl+Shift+F, all F1–F12 down/up pairs, extended arrows, no held keys.");
            }
            if (layouts)
            {
                var swedish = loaded.Take(loadedCount).FirstOrDefault(h => unchecked((uint)(long)h) == WindowsLayout.SwedishHandle);
                if (swedish == 0) throw new InvalidOperationException("Swedish layout must already be loaded for this test; no language settings were changed.");
                foreach (var hkl in new[] { swedish, us, swedish })
                {
                    if (WindowsKeyboard.Foreground().Window != frame) throw new InvalidOperationException("Focus left the owned layout-test window.");
                    ActivateKeyboardLayout(hkl, 0); Pump(20);
                    output.Target = WindowsKeyboard.Foreground();
                    if (output.Target.Layout != hkl) throw new InvalidOperationException("Foreground HKL did not follow the test window language.");
                    state.SetLayout(new WindowsLayout(output.Target.Layout), Now());
                    state.Enter(1, 8, Now()); Pump(5);
                    SetWindowText(edit, "");
                    var layout = state.Layout;
                    void Legend(string id, string expected, bool shift = false, bool altGr = false, bool capsLock = false, bool dead = false)
                    {
                        var key = layout.Keys.Single(k => k.Id == id);
                        var actual = layout.Legend(key, shift, altGr, capsLock);
                        if (actual.Text != expected || actual.Dead != dead) throw new InvalidOperationException($"{layout.Name} legend {id}: expected {expected}/{dead}, got {actual}.");
                    }
                    if (layout.Swedish)
                    {
                        // The first label lookup while an accent
                        // is pending must neither inherit nor consume that accent.
                        Tap("Equals"); Legend("BracketRight", "^", shift: true, dead: true); Tap("e");
                        var accent = new StringBuilder(32); GetWindowText(edit, accent, accent.Capacity);
                        if (accent.ToString() != (caps ? "É" : "é")) throw new InvalidOperationException("Cold legend lookup changed dead-key composition.");
                        SetWindowText(edit, "");
                    }
                    Legend("2", layout.Swedish ? "\"" : "@", shift: true);
                    Legend("7", layout.Swedish ? "/" : "&", shift: true);
                    Legend("2", "2", capsLock: true); // Caps affects letters, not the number row.
                    Legend("Comma", ","); Legend("Period", "."); Legend("Slash", layout.Swedish ? "-" : "/");
                    if (layout.Swedish)
                    {
                        Legend("BracketLeft", "å"); Legend("Quote", "ä"); Legend("Semicolon", "ö");
                        Legend("BracketLeft", "Å", shift: true); Legend("Quote", "Ä", capsLock: true);
                        Legend("2", "@", altGr: true); Legend("e", "€", altGr: true); Legend("Iso", "|", altGr: true);
                        Legend("Equals", "´", dead: true);
                        foreach (var id in new[] { "BracketLeft", "Quote", "Semicolon" }) Tap(id);
                        Tap("Shift"); Tap("Shift");
                        foreach (var id in new[] { "BracketLeft", "Quote", "Semicolon" }) Tap(id);
                        Tap("Shift"); Tap("Space");
                        Tap("AltGr"); Tap("2"); Tap("AltGr"); Tap("e"); Tap("AltGr"); Tap("Iso"); Tap("Space");
                        Tap("AltGr"); Tap("AltGr"); Tap("7"); Tap("8"); Tap("9"); Tap("0"); Tap("AltGr"); Tap("Space");
                        Tap("Equals");
                        // Later label lookups must also preserve the pending accent.
                        Legend("BracketRight", "^", shift: true, dead: true);
                        Tap("e"); Tap("Space");
                    }
                    foreach (var id in "1234567890") Tap(id.ToString());
                    Tap("Space"); Tap("Comma"); Tap("Period");
                    if (layout.Swedish) { Tap("Shift"); Tap("7"); } else Tap("Slash");
                    Tap("Space"); Tap("Shift"); Tap("2"); Tap("Shift"); Tap("7");
                    var expectedText = layout.Swedish ? "åäöÅÄÖ @€| {[]} é 1234567890 ,./ \"/" : "1234567890 ,./ @&";
                    if (caps) expectedText = new string(expectedText.Select(c => char.IsLetter(c) ? (char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)) : c).ToArray());
                    var actualText = new StringBuilder(512); GetWindowText(edit, actualText, actualText.Capacity);
                    if (actualText.ToString() != expectedText) throw new InvalidOperationException($"{layout.Name} input mismatch. Expected {expectedText}; got {actualText}.");
                    // Layout changes must cancel one-shot and locked modifiers.
                    Tap("Shift"); Tap("AltGr"); Tap("AltGr");
                    Console.WriteLine($"Layout check passed: foreground HKL {unchecked((uint)(long)hkl):X8}, {layout.Name}, Windows-derived legends and actual text: {actualText}");
                }
                state.Cancel(Now());
                if (output.HasOwnedKeys || new[] { 0x10, 0x11, 0x12, 0xa5 }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0)) throw new InvalidOperationException("Layout check left modifiers held.");
            }
            if (shell)
            {
                void ResetTarget()
                {
                    FocusTestWindow(frame, edit); Pump(100);
                    output.Target = WindowsKeyboard.Foreground();
                    if (output.Target.Window != frame) throw new InvalidOperationException("Shell check could not restore its test window; no further input sent.");
                    state.Cancel(Now()); state.Enter(1, 8, Now());
                    Pump(5); // A new press must be newer than the cancellation watermark.
                }
                void ExpectShell(string processName, string windowClass, string action)
                {
                    var timer = Stopwatch.StartNew(); InputTarget target = default; bool matched = false; string observed = "test window";
                    do
                    {
                        Pump(50); target = WindowsKeyboard.Foreground();
                        if (target.Window == frame || target.Window == 0) continue;
                        using var process = Process.GetProcessById((int)target.Process);
                        var name = new StringBuilder(256); GetClassName(target.Window, name, name.Capacity);
                        observed = $"{process.ProcessName} ({name})";
                        matched = process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase) && (windowClass == null || name.ToString() == windowClass);
                    } while (!matched && timer.ElapsedMilliseconds < 3000);
                    if (!matched)
                    {
                        throw new InvalidOperationException($"{action}: expected shell window did not take focus; observed {observed}; no dismissal input sent.");
                    }
                    output.Target = target;
                    output.Stroke(0x01, []);
                    Pump(200);
                    Console.WriteLine($"Shell check passed: {action} opened {processName}; Escape dismissed it.");
                }
                ResetTarget(); Tap("Win"); Tap("r"); ExpectShell("explorer", "#32770", "Win+R");
                ResetTarget(); Tap("Win"); Tap("Win"); ExpectShell("StartMenuExperienceHost", null, "Windows key double click");
                ResetTarget();
                if (output.HasOwnedKeys || (GetAsyncKeyState(0x5b) & 0x8000) != 0) throw new InvalidOperationException("Shell check left Windows key held.");
            }
        }
        finally
        {
            observe = null;
            try { output.ReleaseAll(); }
            finally
            {
                ActivateKeyboardLayout(previousLayout, 0);
                DestroyWindow(frame);
                if (previous != 0) SetForegroundWindow(previous);
            }
        }
    }

    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            while (PeekMessage(out var message, 0, 0, 0, 1)) { observe?.Invoke(message); TranslateMessage(ref message); DispatchMessage(ref message); }
            Thread.Sleep(1);
        } while (timer.ElapsedMilliseconds < milliseconds);
    }
    private static void FocusTestWindow(nint frame, nint edit)
    {
        // Test harness only: temporarily join the foreground input queue so a
        // shell-launched check can activate its own window. Never used by VR typing.
        var other = GetWindowThreadProcessId(WindowsKeyboard.Foreground().Window, out _);
        var current = GetCurrentThreadId();
        var attached = other != 0 && other != current && AttachThreadInput(current, other, true);
        try { SetForegroundWindow(frame); SetFocus(edit); }
        finally { if (attached) AttachThreadInput(current, other, false); }
    }
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint extended, string cls, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, nint[] list);
    [DllImport("user32.dll")] private static extern nint ActivateKeyboardLayout(nint layout, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WindowClass cls);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode)] private static extern nint LoadLibrary(string name);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(nint window, string text);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int size);
}
