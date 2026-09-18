using System.ComponentModel;
using System.Runtime.InteropServices;
using GoBoard.Core;

namespace GoBoard.Platform.Windows;

// Read the layout DLL's immutable WDK kbd.h tables. Never call ToUnicodeEx:
// even a nonmutating query can inherit an accent pending in another app.
// ABI reference: Windows SDK um/kbd.h, KBDTABLES / MODIFIERS / VK_TO_WCHARS.
internal sealed class KeyboardTables : IDisposable
{
    private nint module;
    private readonly uint imageSize;
    private readonly nint modifiers, charTables, scans, ligatures;
    private readonly byte scanCount, ligatureLength, ligatureStride;
    private readonly Dictionary<int, byte> modifierBits = new();
    public bool HasAltGr { get; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint Descriptor();
    [StructLayout(LayoutKind.Sequential)]
    private struct ModuleInfo { public nint Base; public uint Size; public nint Entry; }

    public KeyboardTables(string fileName)
    {
        if (IntPtr.Size != 8) throw new NotSupportedException("Keyboard tables require x64.");
        if (Path.GetFileName(fileName) != fileName || !fileName.StartsWith("kbd", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Not a standard Windows keyboard DLL.");
        // Resolve dependencies only in System32 as well. No current-directory search.
        module = LoadLibraryEx(Path.Combine(Environment.SystemDirectory, fileName), 0, 0x800);
        if (module == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!GetModuleInformation(GetCurrentProcess(), module, out var info, Marshal.SizeOf<ModuleInfo>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            imageSize = info.Size;
            var export = GetProcAddress(module, "KbdLayerDescriptor");
            Check(export, 1);
            var table = Marshal.GetDelegateForFunctionPointer<Descriptor>(export)();
            Check(table, 104);
            modifiers = Pointer(table); charTables = Pointer(table + 8); scans = Pointer(table + 48);
            scanCount = Byte(table + 56);
            var flags = UInt(table + 80);
            if ((flags >> 16) > 1 || (flags & 0xfffe) != 0)
                throw new NotSupportedException("Unsupported keyboard table version or locale behavior.");
            HasAltGr = (flags & 1) != 0;
            ligatureLength = Byte(table + 84); ligatureStride = Byte(table + 85); ligatures = Pointer(table + 88);
            var bits = Pointer(modifiers);
            for (var i = 0; ; i++)
            {
                if (i == 32) throw new InvalidDataException("Unterminated modifier table.");
                var vk = Byte(bits + i * 2);
                if (vk == 0) break;
                modifierBits.Add(vk, Byte(bits + i * 2 + 1));
            }
        }
        catch { Dispose(); throw; }
    }

    public Dictionary<ushort, WindowsLayout.KeyLegend[]> ReadLegends()
        => KeyboardLayout.IsoKeys.Where(k => k.Printable).ToDictionary(k => k.Scan,
            k => Enumerable.Range(0, 8).Select(state => Read(k.Scan, state)).ToArray());

    private WindowsLayout.KeyLegend Read(ushort scan, int state)
    {
        if (scan >= scanCount) return new("");
        var vk = (byte)Word(scans + scan * 2);
        if (vk is 0 or 0xff) return new("");
        var shift = (state & 1) != 0;
        var altGr = (state & 2) != 0;
        var caps = (state & 4) != 0;
        // Query Ctrl+Alt states even on a layout without AltGr, as the static
        // fixtures do. The renderer only selects these states when HasAltGr.
        var bits = (shift ? modifierBits.GetValueOrDefault(0x10) : 0) |
            (altGr ? modifierBits.GetValueOrDefault(0x11) | modifierBits.GetValueOrDefault(0x12) : 0);
        for (var t = 0; t < 32; t++)
        {
            var descriptor = charTables + t * 16;
            var rows = Pointer(descriptor);
            if (rows == 0) return new("");
            var columns = Byte(descriptor + 8); var stride = Byte(descriptor + 9);
            if (columns is 0 or > 15 || stride < 2 + columns * 2)
                throw new InvalidDataException("Invalid character table dimensions.");
            for (var r = 0; ; r++)
            {
                if (r == 512) throw new InvalidDataException("Unterminated character table.");
                var row = rows + r * stride;
                var key = Byte(row);
                if (key == 0) break;
                if (key != vk) continue;
                var attributes = Byte(row + 1);
                // SGCAPS, kana/group locks need additional mode handling. Keep
                // the whole layout on the explicit fallback instead of guessing.
                if ((attributes & ~5) != 0) throw new NotSupportedException("Layout uses additional lock modes.");
                var effectiveBits = bits;
                if (caps && (altGr ? (attributes & 4) != 0 : (attributes & 1) != 0))
                    effectiveBits ^= modifierBits.GetValueOrDefault(0x10);
                if (effectiveBits > Word(modifiers + 8)) return new("");
                var column = Byte(modifiers + 10 + effectiveBits);
                if (column == 15 || column >= columns) return new("");
                var character = Word(row + 2 + column * 2);
                if (character == 0xf001)
                {
                    if (Byte(row + stride) != 0xff) throw new InvalidDataException("Missing dead-key row.");
                    return new(Text(Word(row + stride + 2 + column * 2)), true);
                }
                if (character == 0xf002) return new(Ligature(vk, column));
                return new(Text(character));
            }
        }
        throw new InvalidDataException("Unterminated character table list.");
    }

    private static string Text(ushort character)
        => character is 0 or 0xf000 || char.IsControl((char)character) ? "" : ((char)character).ToString();

    private string Ligature(byte vk, byte column)
    {
        if (ligatures == 0 || ligatureLength is 0 or > 16 || ligatureStride < 4 + ligatureLength * 2)
            throw new InvalidDataException("Invalid ligature table.");
        for (var i = 0; i < 1024; i++)
        {
            var entry = ligatures + i * ligatureStride;
            var key = Byte(entry);
            if (key == 0) break;
            if (key != vk || Word(entry + 2) != column) continue;
            var text = "";
            for (var c = 0; c < ligatureLength; c++) text += Text(Word(entry + 4 + c * 2));
            return text;
        }
        throw new InvalidDataException("Missing ligature.");
    }

    private void Check(nint address, int size)
    {
        var offset = (long)address - (long)module;
        if (offset < 0 || size < 0 || offset > imageSize - (long)size)
            throw new InvalidDataException("Keyboard table pointer is outside its DLL image.");
    }
    private byte Byte(nint p) { Check(p, 1); return Marshal.ReadByte(p); }
    private ushort Word(nint p) { Check(p, 2); return unchecked((ushort)Marshal.ReadInt16(p)); }
    private uint UInt(nint p) { Check(p, 4); return unchecked((uint)Marshal.ReadInt32(p)); }
    private nint Pointer(nint p) { Check(p, 8); return Marshal.ReadIntPtr(p); }
    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref module, 0);
        if (handle != 0) FreeLibrary(handle);
    }

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryEx(string file, nint reserved, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint module);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("psapi.dll", SetLastError = true)] private static extern bool GetModuleInformation(nint process, nint module, out ModuleInfo info, int size);
}
