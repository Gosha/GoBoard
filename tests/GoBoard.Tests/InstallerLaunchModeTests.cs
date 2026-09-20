using GoBoard.Installer.Actions;
using Xunit;

namespace GoBoard.Tests;

public sealed class InstallerLaunchModeTests
{
    [Fact]
    public void PreservesLiveModeWithoutOldLauncherLifetimeArguments()
    {
        Assert.Equal("", LaunchMode.RestartArguments([]));
        Assert.Equal("", LaunchMode.RestartArguments(["--stop-file", "V:\\folder with spaces\\stop", "--seconds", "60"]));
        Assert.Equal("--desktop", LaunchMode.RestartArguments(["--desktop", "--stop-file", "stop"]));
        Assert.Equal("--desktop", LaunchMode.RestartArguments(["--desktop"]));
        Assert.Equal("--settings", LaunchMode.RestartArguments(["--settings"]));
        Assert.Equal("--settings", LaunchMode.RestartArguments(["--settings", "--executable", "old.exe"]));
    }

    [Theory]
    [InlineData("--render")]
    [InlineData("--render-desktop")]
    [InlineData("--desktop-input-check")]
    [InlineData("--steamvr")]
    [InlineData("--stop")]
    [InlineData("--unknown")]
    public void DoesNotTurnDiagnosticsIntoLiveKeyboards(string command)
    {
        Assert.Null(LaunchMode.RestartArguments([command]));
        Assert.Null(LaunchMode.RestartArguments([command, "output.png"]));
        Assert.Null(LaunchMode.RestartArguments(["--desktop", command]));
    }

    [Fact]
    public void RejectsIncompleteAndMixedModeCommands()
    {
        Assert.Null(LaunchMode.RestartArguments(["--stop-file"]));
        Assert.Null(LaunchMode.RestartArguments(["--desktop", "--seconds"]));
        Assert.Null(LaunchMode.RestartArguments(["--settings", "--desktop"]));
        Assert.Null(LaunchMode.RestartArguments(["--settings", "--executable"]));
    }
}
