using GoBoard.Setup;
using Xunit;

namespace GoBoard.Tests;

public sealed class RuntimeWorkflowTests
{
    [Theory]
    [InlineData(1603)]
    [InlineData(5)]
    [InlineData(3010)]
    [InlineData(1641)]
    public async Task FailedOrRebootingRuntimeNeverPermitsAppInstallation(int exitCode)
    {
        var verified = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => RuntimeWorkflow.InstallAsync(
            _ => Task.CompletedTask, () => Task.FromResult(exitCode), () => verified = true, CancellationToken.None));
        Assert.False(verified);
    }

    [Theory]
    [InlineData(1602)]
    [InlineData(1223)]
    public async Task CancellationFromMicrosoftInstallerPropagates(int exitCode)
    {
        Assert.Equal(1602, await RuntimeWorkflow.InstallAsync(_ => Task.CompletedTask,
            () => Task.FromResult(exitCode), () => throw new Exception("Must not verify after cancellation"), CancellationToken.None));
    }

    [Fact]
    public async Task DownloadOrHashFailureNeverRunsInstaller()
    {
        var installed = false;
        await Assert.ThrowsAsync<InvalidDataException>(() => RuntimeWorkflow.InstallAsync(
            _ => throw new InvalidDataException("Incorrect SHA-512"), () => { installed = true; return Task.FromResult(0); }, () => true, CancellationToken.None));
        Assert.False(installed);
    }

    [Fact]
    public async Task SuccessfulExitStillRequiresCompatibleRuntime()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RuntimeWorkflow.InstallAsync(
            _ => Task.CompletedTask, () => Task.FromResult(0), () => false, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationAfterDownloadDoesNotInstall()
    {
        using var cancellation = new CancellationTokenSource();
        var installed = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RuntimeWorkflow.InstallAsync(
            _ => { cancellation.Cancel(); return Task.CompletedTask; },
            () => { installed = true; return Task.FromResult(0); }, () => true, cancellation.Token));
        Assert.False(installed);
    }

    [Fact]
    public async Task CancellationWhileInstallingWaitsThenStopsBeforeApp()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RuntimeWorkflow.InstallAsync(
            _ => Task.CompletedTask, () => { cancellation.Cancel(); return Task.FromResult(0); },
            () => throw new Exception("Must not continue after cancellation"), cancellation.Token));
    }

    [Fact]
    public async Task SuccessRequiresDownloadInstallAndFreshVerificationInOrder()
    {
        var operations = new List<string>();
        Assert.Equal(0, await RuntimeWorkflow.InstallAsync(
            _ => { operations.Add("verified download"); return Task.CompletedTask; },
            () => { operations.Add("install"); return Task.FromResult(0); },
            () => { operations.Add("runtime resolves"); return true; }, CancellationToken.None));
        Assert.Equal(new[] { "verified download", "install", "runtime resolves" }, operations);
    }
}
