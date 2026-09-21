using System;
using System.Threading;
using System.Threading.Tasks;

namespace GoBoard.Setup;

internal static class RuntimeWorkflow
{
    // Keep the failure boundary explicit: acquisition includes verification;
    // no child installer runs until it has completed successfully.
    internal static async Task<int> InstallAsync(Func<CancellationToken, Task> acquireAndVerify,
        Func<Task<int>> install, Func<bool> compatible, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        await acquireAndVerify(cancellation);
        cancellation.ThrowIfCancellationRequested();
        var exitCode = await install();
        cancellation.ThrowIfCancellationRequested();
        if (exitCode == 3010 || exitCode == 1641)
            throw new InvalidOperationException("Microsoft .NET requires a Windows restart. Restart Windows, then run GoBoard setup again. GoBoard has not been changed.");
        if (exitCode == 1602 || exitCode == 1223) return 1602;
        if (exitCode != 0)
            throw new InvalidOperationException("Microsoft .NET installation failed (exit " + exitCode + "). GoBoard has not been changed. See the Microsoft installer log beside the runtime check log.");
        if (!compatible())
            throw new InvalidOperationException("Microsoft .NET setup finished, but the GoBoard runtime check still fails. Check DOTNET_ROOT / roll-forward environment overrides and the log, then retry setup. GoBoard has not been changed.");
        return 0;
    }
}
