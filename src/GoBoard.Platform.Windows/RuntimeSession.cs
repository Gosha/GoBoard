namespace GoBoard.Platform.Windows;

// Shared by manual, script and SteamVR launches. Named kernel objects are local
// to the Windows session and disappear after the last process closes them.
internal sealed class RuntimeSession : IDisposable
{
    internal const string Name = "Local\\GoBoard.Runtime";
    private readonly Mutex instance;
    private readonly EventWaitHandle stop;
    private readonly TextWriter originalOutput = Console.Out;
    private readonly TextWriter originalError = Console.Error;
    private StreamWriter log, errorLog;
    internal bool StopRequested => stop.WaitOne(0);
    internal static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoBoard", "runtime");

    private RuntimeSession(Mutex instance, EventWaitHandle stop)
    {
        this.instance = instance;
        this.stop = stop;
    }

    internal static RuntimeSession TryStart(string name = Name)
    {
        var instance = new Mutex(true, name, out var first);
        if (!first) { instance.Dispose(); return null; }
        try
        {
            var stop = new EventWaitHandle(false, EventResetMode.ManualReset, name + ".Stop");
            // Only the winning process resets a signal from an earlier session.
            stop.Reset();
            return new RuntimeSession(instance, stop);
        }
        catch { instance.ReleaseMutex(); instance.Dispose(); throw; }
    }

    internal void StartLogging()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            log = OpenLog("goboard.log");
            errorLog = OpenLog("goboard.error.log");
            Console.SetOut(new TeeWriter(originalOutput, log));
            Console.SetError(new TeeWriter(originalError, errorLog));
            Console.WriteLine($"GoBoard started {DateTimeOffset.Now:O}, PID {Environment.ProcessId}, executable {Environment.ProcessPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot open runtime logs: {ex.Message}");
        }
    }

    private static StreamWriter OpenLog(string name) => new(new FileStream(
        Path.Combine(LogDirectory, name), FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };

    internal static bool RequestStop(string name = Name)
    {
        if (!EventWaitHandle.TryOpenExisting(name + ".Stop", out var stop)) return false;
        using (stop) return stop.Set();
    }

    public void Dispose()
    {
        if (log != null || errorLog != null)
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
            log?.Dispose();
            errorLog?.Dispose();
        }
        stop.Dispose();
        instance.ReleaseMutex();
        instance.Dispose();
    }

    private sealed class TeeWriter(TextWriter console, TextWriter file) : TextWriter
    {
        public override System.Text.Encoding Encoding => console.Encoding;
        public override void Write(char value) { console.Write(value); WriteFile(() => file.Write(value)); }
        public override void WriteLine(string value) { console.WriteLine(value); WriteFile(() => file.WriteLine(value)); }
        public override void Flush() { console.Flush(); WriteFile(file.Flush); }
        // Losing a diagnostic file (e.g. disk full) must not interrupt key cleanup.
        private static void WriteFile(Action write)
        {
            try { write(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
