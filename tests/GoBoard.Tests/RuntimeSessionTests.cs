using GoBoard.Platform.Windows;
using Xunit;

namespace GoBoard.Tests;

public sealed class RuntimeSessionTests
{
    [Fact]
    public void DuplicateLaunchCannotResetStopAndRestartClearsOldSignal()
    {
        var name = "Local\\GoBoard.Tests." + Guid.NewGuid().ToString("N");
        Assert.False(RuntimeSession.RequestStop(name)); // Stopping while idle leaves no stale signal.
        using (var first = RuntimeSession.TryStart(name))
        {
            Assert.NotNull(first);
            Assert.False(first.StopRequested);
            Assert.True(RuntimeSession.RequestStop(name));
            using var duplicate = RuntimeSession.TryStart(name);
            Assert.Null(duplicate);
            Assert.True(first.StopRequested);
        }
        using var restarted = RuntimeSession.TryStart(name);
        Assert.NotNull(restarted);
        Assert.False(restarted.StopRequested);
    }
}
