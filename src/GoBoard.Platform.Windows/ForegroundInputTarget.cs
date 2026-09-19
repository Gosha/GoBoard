using System.Runtime.InteropServices;

namespace GoBoard.Platform.Windows;

internal readonly record struct InputTarget(nint Window, nint Layout, uint Process, nint Focus = 0, uint Thread = 0);

internal static class ForegroundInputTarget
{
    internal interface IWindows
    {
        nint Foreground();
        (uint Thread, uint Process) Owner(nint window);
        nint? Focus(uint thread);
        bool IsChild(nint parent, nint child);
        nint Layout(uint thread);
    }

    private static readonly IWindows Native = new NativeWindows();
    public static InputTarget Read() => Read(Native);

    internal static InputTarget Read(IWindows windows)
    {
        var frame = windows.Foreground();
        if (frame == 0) return default;
        var owner = windows.Owner(frame);
        if (owner.Thread == 0) return default;

        // Modern Notepad hosts Rich Edit on a different thread from its frame.
        // The frame HKL can stay at its initial language while the editor changes.
        // GetGUIThreadInfo observes focus without attaching input queues or
        // activating any layout. Keep the frame for foreground guards/status.
        var focus = windows.Focus(owner.Thread);
        var input = focus.GetValueOrDefault();
        if (input == 0) input = frame; // Shortcuts also work without a focused control.
        if (input != frame && !windows.IsChild(frame, input)) return default;
        var thread = input == frame ? owner.Thread : windows.Owner(input).Thread;
        if (thread == 0) return default; // Never query our own HKL via thread zero.
        var layout = windows.Layout(thread);
        if (layout == 0) return default;

        // Do not combine a previous frame/control with a new foreground target
        // during activation. Callers cancel on an invalid/changed snapshot and
        // revalidate this same identity immediately before injecting input.
        if (windows.Focus(owner.Thread) != focus || windows.Foreground() != frame) return default;
        return new(frame, layout, owner.Process, input, thread);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }

    private sealed class NativeWindows : IWindows
    {
        public nint Foreground() => GetForegroundWindow();
        public (uint Thread, uint Process) Owner(nint window)
        {
            var thread = GetWindowThreadProcessId(window, out var process);
            return (thread, process);
        }
        public nint? Focus(uint thread)
        {
            var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            return GetGUIThreadInfo(thread, ref info) ? info.Focus : null;
        }
        public bool IsChild(nint parent, nint child) => NativeIsChild(parent, child);
        public nint Layout(uint thread) => GetKeyboardLayout(thread);
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", EntryPoint = "IsChild")] private static extern bool NativeIsChild(nint parent, nint child);
}
