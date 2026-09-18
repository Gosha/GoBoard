using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using GoBoard.Core;
using Microsoft.Win32;

namespace GoBoard.Platform.Windows;

internal static class WindowsLayoutProvider
{
    private const string RegistryPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";
    private static readonly ConcurrentDictionary<(nint, KeyboardGeometry), Lazy<WindowsLayout>> Cache = new();

    public static WindowsLayout Get(nint handle, KeyboardGeometry geometry = KeyboardGeometry.Auto)
        => Cache.GetOrAdd((handle, geometry), key => new(() => Create(key.Item1, key.Item2))).Value;

    private static WindowsLayout Create(nint handle, KeyboardGeometry geometry)
    {
        try
        {
            if (handle == 0) return new(handle);
            var loaded = new nint[GetKeyboardLayoutList(0, null)];
            var count = GetKeyboardLayoutList(loaded.Length, loaded);
            if (!loaded.Take(count).Contains(handle)) return new(handle);
            var klid = ResolveName(handle);
            return FromKlid(handle, klid, geometry);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidDataException or NotSupportedException or
            IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            Console.Error.WriteLine($"Layout {unchecked((uint)(long)handle):X8}: using fallback ({ex.Message}).");
            return new(handle);
        }
    }

    // Also used for deterministic previews/tests: reading a registered layout
    // does not load or activate it as an input language.
    internal static WindowsLayout FromKlid(nint handle, string klid, KeyboardGeometry geometry = KeyboardGeometry.Auto)
    {
        if (klid.Length != 8 || !uint.TryParse(klid, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            throw new ArgumentException("Expected an eight-digit Windows layout ID.", nameof(klid));
        // CJK NLS/IME modes need additional geometry and mode-aware labels.
        if ((id & 0x3ff) is 0x04 or 0x11 or 0x12) throw new NotSupportedException("IME-dependent layout.");
        using var registry = Registry.LocalMachine.OpenSubKey(RegistryPath + "\\" + klid);
        if (registry?.GetValue("IME File") is string) throw new NotSupportedException("IME-dependent layout.");
        var file = registry?.GetValue("Layout File") as string ?? throw new NotSupportedException("Layout DLL is unavailable.");
        using var tables = new KeyboardTables(file);
        var legends = tables.ReadLegends();
        // Physical geometry is metadata, never inferred from output at scan 0x56.
        // Unknown arrangements use all ISO positions and a visible notice.
        bool? knownIso = id switch
        {
            0x00000409 or 0x00010409 or 0x00020409 => false,
            0x0000041d or 0x00000809 or 0x00000407 or 0x0000040c => true,
            _ => null
        };
        var iso = geometry switch { KeyboardGeometry.Ansi => false, KeyboardGeometry.Iso => true, _ => knownIso ?? true };
        var notice = geometry == KeyboardGeometry.Auto && knownIso == null ? "Windows labels; generic ISO arrangement." : null;
        return new(handle, registry.GetValue("Layout Text") as string ?? klid, iso, tables.HasAltGr, legends, notice);
    }

    private static string ResolveName(nint handle)
    {
        string name = null;
        Exception failure = null;
        // Windows resolves the full HKL, including variants/substitutions. A
        // fresh windowless thread prevents activation on the UI/target thread.
        var thread = new Thread(() =>
        {
            var previous = GetKeyboardLayout(0);
            try
            {
                if (ActivateKeyboardLayout(handle, 0) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (GetKeyboardLayout(0) != handle) throw new NotSupportedException("Requested layout is no longer loaded.");
                var buffer = new StringBuilder(9);
                if (!GetKeyboardLayoutName(buffer)) throw new Win32Exception(Marshal.GetLastWin32Error());
                name = buffer.ToString();
            }
            catch (Exception ex) { failure = ex; }
            finally { ActivateKeyboardLayout(previous, 0); }
        }) { IsBackground = true, Name = "GoBoard layout identity" };
        thread.Start(); thread.Join();
        if (failure != null) throw failure;
        return name;
    }

    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, [Out] nint[] layouts);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint ActivateKeyboardLayout(nint layout, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetKeyboardLayoutNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetKeyboardLayoutName(StringBuilder name);
}
