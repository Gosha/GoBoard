using System.Runtime.InteropServices;
using System.Text;

namespace GoBoard.App;

// WinExe prevents Windows from creating a console before Main. Preserve launcher
// redirection; otherwise use an existing terminal or a per-process diagnostic log.
internal sealed class StartupDiagnostics : IDisposable
{
    private readonly TextWriter originalOut = Console.Out, originalError = Console.Error;
    private StreamWriter log;

    public static StartupDiagnostics Open()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoBoard", "logs");
        try { return Open(directory); }
        finally { StartupLogRetention.Cleanup(directory, DateTime.UtcNow); }
    }

    private static StartupDiagnostics Open(string directory)
    {
        var diagnostics = new StartupDiagnostics();
        var output = HasStream(-11);
        var error = HasStream(-12);
        // Never allocate a console, and never replace explicitly redirected handles.
        if (!output && !error && AttachConsole(uint.MaxValue))
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            return diagnostics;
        }
        if (output && error) return diagnostics;

        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"goboard-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Environment.ProcessId}.log");
            diagnostics.log = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
            diagnostics.log.WriteLine($"GoBoard started at {DateTime.UtcNow:O} (PID {Environment.ProcessId}).");
            var writer = TextWriter.Synchronized(diagnostics.log);
            if (!output) Console.SetOut(writer);
            if (!error) Console.SetError(writer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unavailable log directory must not prevent the keyboard starting.
            diagnostics.log?.Dispose();
            diagnostics.log = null;
        }
        return diagnostics;
    }

    public void Dispose()
    {
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        log?.Dispose();
    }

    private static bool HasStream(int id)
    {
        var handle = GetStdHandle(id);
        return handle != 0 && handle != -1 && GetFileType(handle) != 0;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int id);
    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(nint handle);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}
