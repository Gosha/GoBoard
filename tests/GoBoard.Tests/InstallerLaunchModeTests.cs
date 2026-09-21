using GoBoard.Installer.Actions;
using Xunit;

namespace GoBoard.Tests;

public sealed class InstallerLaunchModeTests
{
    [Theory]
    [InlineData(new string[] { }, "", "")]
    [InlineData(new[] { "--desktop" }, "--desktop", "")]
    [InlineData(new[] { "--desktop", "--seconds", "60", "--stop-file", "stop" }, "--desktop", "")]
    [InlineData(new[] { "--settings", "--executable", "old.exe" }, "--settings", "--settings")]
    public void CommittedUpgradeUsesVrButRecoveryRetainsOriginalMode(string[] args, string recovery, string installed)
    {
        var captured = LaunchMode.RestartArguments(args);
        Assert.Equal(installed, LaunchMode.InstalledArguments(captured));
        Assert.Equal(recovery, captured);
    }

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
