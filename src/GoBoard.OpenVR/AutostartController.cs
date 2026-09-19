using System.Diagnostics;
using System.Text.Json;
using GoBoard.Core;

namespace GoBoard.Vr;

// Both settings hosts poll completed work on their own UI/render thread. OpenVR
// utility calls run in a child process, never on the live overlay's connection.
internal sealed class AutostartController
{
    private readonly Func<string, Task<AutostartState>> execute;
    private Task<AutostartState> pending;
    private double nextRefresh;
    public AutostartState State { get; private set; } = new();

    public AutostartController(string executable = null, Func<string, Task<AutostartState>> execute = null)
    {
        this.execute = execute ?? (action => Execute(action, executable ?? Path.Combine(AppContext.BaseDirectory, "GoBoard.exe")));
    }

    public void Update(double now)
    {
        if (pending != null)
        {
            if (!pending.IsCompleted) return;
            try { State = pending.GetAwaiter().GetResult(); }
            catch (Exception ex) { State = new(Error: ex.Message); }
            pending = null;
            nextRefresh = now + 10;
        }
        if (now >= nextRefresh) Begin("status");
    }

    public void Toggle()
    {
        if (!State.CanClick) return;
        Begin(State.Error != null ? "status" : State.Enabled == true ? "unregister" : "enable");
    }

    private void Begin(string action)
    {
        State = State with { Busy = true, Error = null };
        try { pending = execute(action); }
        catch (Exception ex) { pending = Task.FromException<AutostartState>(ex); }
    }

    private static async Task<AutostartState> Execute(string action, string executable)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "GoBoard.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--steamvr-ui");
        start.ArgumentList.Add(action);
        if (action == "enable")
        {
            start.ArgumentList.Add("--executable");
            start.ArgumentList.Add(executable);
        }
        using var process = Process.Start(start) ?? throw new IOException("Could not start SteamVR configuration.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            process.Kill();
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw new IOException("SteamVR did not respond. Check SteamVR and retry.");
        }
        var message = await error.ConfigureAwait(false);
        var json = await output.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(message) ? "SteamVR configuration failed." : message.Trim());
        return JsonSerializer.Deserialize<AutostartState>(json) ?? throw new IOException("SteamVR returned no status.");
    }
}
