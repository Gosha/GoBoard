using System.Text.Json;
using GoBoard.Vr;
using Valve.VR;
using Xunit;

namespace GoBoard.Tests;

public sealed class SteamVrApplicationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GoBoard SteamVR tests å", Guid.NewGuid().ToString("N"));
    private string ManifestPath => Path.Combine(directory, "registration", "goboard.vrmanifest");

    private string Executable(string folder = "production")
    {
        var path = Path.Combine(directory, folder, "GoBoard.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test placeholder");
        return path;
    }

    [Fact]
    public void ManifestTargetsProductionExeWithAbsoluteUnicodePathsAndStableIdentity()
    {
        var executable = Executable();
        using var json = JsonDocument.Parse(SteamVrApplication.CreateManifest(executable));
        var apps = json.RootElement.GetProperty("applications");
        Assert.Equal(1, apps.GetArrayLength());
        var app = apps[0];
        Assert.Equal(SteamVrApplication.Key, app.GetProperty("app_key").GetString());
        Assert.Equal("binary", app.GetProperty("launch_type").GetString());
        Assert.Equal(executable, app.GetProperty("binary_path_windows").GetString());
        Assert.Equal(Path.GetDirectoryName(executable), app.GetProperty("working_directory").GetString());
        Assert.Equal("", app.GetProperty("arguments").GetString());
        Assert.True(app.GetProperty("is_dashboard_overlay").GetBoolean());
        Assert.Equal("GoBoard", app.GetProperty("strings").GetProperty("en_us").GetProperty("name").GetString());
    }

    [Fact]
    public void RegistrationRejectsMissingExecutableAndDotnetHost()
    {
        Assert.Throws<ArgumentException>(() => SteamVrApplication.CreateManifest(Path.Combine(directory, "GoBoard.exe")));
        var host = Path.Combine(Path.GetDirectoryName(Executable())!, "dotnet.exe");
        File.WriteAllText(host, "test placeholder");
        Assert.Throws<ArgumentException>(() => SteamVrApplication.CreateManifest(host));
    }

    [Fact]
    public void RegisterEnableRelocateDisableAndUnregisterUseOneManifest()
    {
        var api = new FakeApplications();
        var first = SteamVrApplication.CreateManifest(Executable());
        SteamVrApplication.Configure(api, "register", ManifestPath, first);
        Assert.True(api.Installed);
        Assert.False(api.AutoLaunch);
        SteamVrApplication.Configure(api, "enable", ManifestPath, first);
        Assert.True(api.AutoLaunch);
        var relocated = SteamVrApplication.CreateManifest(Executable("moved folder"));
        SteamVrApplication.Configure(api, "register", ManifestPath, relocated);
        Assert.True(api.AutoLaunch); // Register preserves the SteamVR-owned setting.
        Assert.Equal(relocated, File.ReadAllText(ManifestPath));
        Assert.All(api.Paths, path => Assert.Equal(ManifestPath, path));
        SteamVrApplication.Configure(api, "disable", ManifestPath);
        Assert.True(api.Installed);
        Assert.False(api.AutoLaunch);
        Assert.True(File.Exists(ManifestPath));
        SteamVrApplication.Configure(api, "unregister", ManifestPath);
        Assert.False(api.Installed);
        Assert.False(File.Exists(ManifestPath));
        SteamVrApplication.Configure(api, "unregister", ManifestPath);
        SteamVrApplication.Configure(api, "disable", ManifestPath);
    }

    [Fact]
    public void FailedRegistrationRestoresOldManifestAndDoesNotEnableAutostart()
    {
        var api = new FakeApplications();
        var original = SteamVrApplication.CreateManifest(Executable());
        SteamVrApplication.Configure(api, "register", ManifestPath, original);
        api.AddError = EVRApplicationError.InvalidManifest;
        var ex = Assert.Throws<InvalidOperationException>(() => SteamVrApplication.Configure(api, "enable", ManifestPath, "invalid"));
        Assert.Contains("InvalidManifest", ex.Message);
        Assert.Equal(original, File.ReadAllText(ManifestPath));
        Assert.False(api.AutoLaunch);
    }

    [Fact]
    public void FailedFirstRegistrationLeavesNoManifest()
    {
        var api = new FakeApplications { AddError = EVRApplicationError.IPCFailed };
        Assert.Throws<InvalidOperationException>(() => SteamVrApplication.Configure(api, "enable", ManifestPath,
            SteamVrApplication.CreateManifest(Executable())));
        Assert.False(File.Exists(ManifestPath));
        Assert.False(api.AutoLaunch);
    }

    [Fact]
    public void FailedAutostartCanBeRetriedAndFailedRemovalKeepsManifest()
    {
        var api = new FakeApplications { SetError = EVRApplicationError.IPCFailed };
        var manifest = SteamVrApplication.CreateManifest(Executable());
        var ex = Assert.Throws<InvalidOperationException>(() => SteamVrApplication.Configure(api, "enable", ManifestPath, manifest));
        Assert.Contains("Manifest registered", ex.Message);
        Assert.True(api.Installed);
        Assert.True(File.Exists(ManifestPath));
        api.SetError = EVRApplicationError.None;
        SteamVrApplication.Configure(api, "enable", ManifestPath, manifest);
        api.RemoveError = EVRApplicationError.IPCFailed;
        Assert.Throws<InvalidOperationException>(() => SteamVrApplication.Configure(api, "unregister", ManifestPath));
        Assert.True(File.Exists(ManifestPath));
        Assert.False(api.AutoLaunch);
    }

    [Fact]
    public void StatusDoesNotCreateFilesOrChangeRegistration()
    {
        var api = new FakeApplications { Installed = true, AutoLaunch = true };
        SteamVrApplication.Configure(api, "status", ManifestPath);
        Assert.True(api.Installed);
        Assert.True(api.AutoLaunch);
        Assert.Empty(api.Paths);
        Assert.False(Directory.Exists(directory));
    }

    private sealed class FakeApplications : SteamVrApplication.IApplicationsApi
    {
        public bool Installed, AutoLaunch;
        public EVRApplicationError AddError, SetError, RemoveError;
        public List<string> Paths { get; } = [];
        public bool IsInstalled() => Installed;
        public bool GetAutoLaunch() => AutoLaunch;
        public EVRApplicationError AddManifest(string path)
        {
            Paths.Add(path);
            Assert.True(File.Exists(path));
            if (AddError == EVRApplicationError.None) Installed = true;
            return AddError;
        }
        public EVRApplicationError RemoveManifest(string path)
        {
            Paths.Add(path);
            Assert.False(AutoLaunch);
            if (RemoveError != EVRApplicationError.None) return RemoveError;
            if (!Installed) return EVRApplicationError.NoManifest;
            Installed = false;
            return EVRApplicationError.None;
        }
        public EVRApplicationError SetAutoLaunch(bool enabled)
        {
            Assert.True(Installed);
            if (SetError == EVRApplicationError.None) AutoLaunch = enabled;
            return SetError;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
