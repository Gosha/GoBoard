using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GoBoard.Poc;

internal readonly record struct InputTarget(nint Window, nint Layout, uint Process);

internal sealed class WindowsKeyboard : IKeySink, IDisposable
{
    // Explicit x64 INPUT ABI: 8-byte union alignment, 32-byte union, 40 total.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort VirtualKey;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
        [FieldOffset(16)] public uint Time;
        [FieldOffset(24)] public nuint ExtraInfo;
    }
    private readonly HashSet<ushort> owned = new();
    private readonly HashSet<ushort> borrowed = new();
    public bool HasOwnedKeys => owned.Count > 0;
    public InputTarget Target { get; set; }
    public static bool CapsLock => (GetKeyState(0x14) & 1) != 0;
    public static bool PhysicalShift => (GetAsyncKeyState(0xa0) & 0x8000) != 0 || (GetAsyncKeyState(0xa1) & 0x8000) != 0;
    public static bool PhysicalAltGr => (GetAsyncKeyState(0xa5) & 0x8000) != 0;

    public static InputTarget Foreground()
    {
        var window = GetForegroundWindow();
        var thread = GetWindowThreadProcessId(window, out var process);
        return new(window, thread == 0 ? 0 : GetKeyboardLayout(thread), process);
    }

    public static string TargetName(InputTarget target)
    {
        if (target.Window == 0 || target.Process == Environment.ProcessId) return "Focus a text field on your desktop";
        try { using var process = Process.GetProcessById((int)target.Process); return $"Typing into {process.ProcessName}"; }
        catch (ArgumentException) { return "Focus a text field on your desktop"; }
    }

    public void Down(ushort scan)
    {
        if (Target.Window == 0 || Foreground() != Target)
            throw new InvalidOperationException("Focus changed. Press the key again.");
        if (!owned.Contains(scan) && !borrowed.Contains(scan))
        {
            var vk = MapVirtualKeyEx(scan, 3, Target.Layout);
            if (vk != 0 && (GetAsyncKeyState((int)vk) & 0x8000) != 0)
            {
                // Respect physical modifiers already held; never release them ourselves.
                if (scan is 0x2a or 0x1d or 0x38 or 0xe038 or 0xe05b) { borrowed.Add(scan); return; }
                throw new InvalidOperationException("Release the same key on your physical keyboard first.");
            }
        }
        if (borrowed.Contains(scan)) return;
        Send(scan, false);
        owned.Add(scan);
    }

    public void Up(ushort scan)
    {
        if (borrowed.Remove(scan) || !owned.Contains(scan)) return;
        Send(scan, true);
        owned.Remove(scan);
    }

    public void ReleaseAll()
    {
        borrowed.Clear();
        Exception failure = null;
        foreach (var scan in owned.ToArray())
            try { Up(scan); } catch (Exception ex) { failure ??= ex; }
        if (failure != null) throw failure;
    }

    public void Stroke(ushort scan, ushort[] chord)
    {
        if (Target.Window == 0 || Foreground() != Target)
            throw new InvalidOperationException("Focus changed. Press the key again.");
        if (owned.Count != 0) throw new InvalidOperationException("Previous keyboard input still needs cleanup.");
        var keys = new List<ushort>();
        foreach (var key in chord.Append(scan).Distinct())
        {
            var vk = MapVirtualKeyEx(key, 3, Target.Layout);
            if (vk != 0 && (GetAsyncKeyState((int)vk) & 0x8000) != 0)
            {
                if (key is 0x2a or 0x1d or 0x38 or 0xe038 or 0xe05b) continue;
                throw new InvalidOperationException("Release the same key on your physical keyboard first.");
            }
            keys.Add(key);
        }
        var events = keys.Select(key => (Scan: key, Up: false))
            .Concat(keys.AsEnumerable().Reverse().Select(key => (Scan: key, Up: true))).ToArray();
        var inputs = events.Select(e => MakeInput(e.Scan, e.Up)).ToArray();
        if (inputs.Length == 0) return;
        // Submit the complete chord in one ordered batch. Shell shortcuts can
        // change foreground focus; their key-ups must still be delivered.
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        var error = Marshal.GetLastWin32Error();
        for (var i = 0; i < sent; i++)
            if (events[i].Up) owned.Remove(events[i].Scan); else owned.Add(events[i].Scan);
        if (sent != inputs.Length)
            throw new InvalidOperationException($"Windows accepted only {sent}/{inputs.Length} shortcut events ({error}).");
    }

    private static void Send(ushort scan, bool up)
    {
        var input = MakeInput(scan, up);
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Windows blocked keyboard input ({error}). Try a non-admin text app.", new Win32Exception(error));
        }
    }

    // Keep the E0 prefix in key identity/mapping; SendInput receives the low
    // scan byte plus EXTENDEDKEY on both down and up (arrows, navigation, Win).
    internal static uint ScanFlags(ushort scan, bool up)
        => 0x0008u | ((scan & 0xff00) == 0xe000 ? 0x0001u : 0) | (up ? 0x0002u : 0);
    private static Input MakeInput(ushort scan, bool up)
        => new() { Type = 1, Scan = (ushort)(scan & 0xff), Flags = ScanFlags(scan, up), ExtraInfo = 0x474f4244 };

    public void Dispose()
    {
        try { ReleaseAll(); } catch (Exception ex) { Console.Error.WriteLine($"Keyboard cleanup: {ex.Message}"); }
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern short GetKeyState(int virtualKey);
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")] private static extern uint MapVirtualKeyEx(uint code, uint type, nint layout);
}
