using System;

namespace GoBoard.Installer.Actions;

internal static class LaunchMode
{
    // A committed upgrade launches the installed VR app. Keep the original mode
    // in the captured instance so rollback can still restore an older desktop host.
    internal static string InstalledArguments(string capturedMode) => capturedMode == "--desktop" ? "" : capturedMode;

    // Diagnostic processes must never be stopped or relaunched as keyboards.
    internal static string RestartArguments(string[] args)
    {
        var first = 0;
        var mode = "";
        if (args.Length > 0 && args[0] == "--settings")
            return args.Length == 1 || (args.Length == 3 && args[1] == "--executable") ? "--settings" : null;
        if (args.Length > 0 && args[0] == "--desktop") { mode = "--desktop"; first = 1; }
        for (var i = first; i < args.Length; i++)
        {
            if ((args[i] != "--stop-file" && args[i] != "--seconds") || ++i >= args.Length) return null;
        }
        // Launcher stop files/time limits belong to the old launch, not the installed copy.
        return mode;
    }
}
