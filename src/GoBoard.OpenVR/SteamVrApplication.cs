using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoBoard.Core;
using Valve.VR;

namespace GoBoard.Vr;

public static class SteamVrApplication
{
    public const string Key = "goboard.app";
    internal static string ManifestPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoBoard", "steamvr", "goboard.vrmanifest");

    public static int Run(string[] args, bool json = false)
    {
        var initialized = false;
        try
        {
            if (args.Length != 2 && !(args.Length == 4 && args[2] == "--executable"))
                throw new ArgumentException("Usage: GoBoard --steamvr register|enable|disable|unregister|status [--executable PATH]");
            var action = args[1];
            if (action is not ("register" or "enable" or "disable" or "unregister" or "status"))
                throw new ArgumentException("Unknown SteamVR action: " + action);
            if (args.Length == 4 && action is not ("register" or "enable"))
                throw new ArgumentException("--executable is only valid for register or enable.");
            var executable = args.Length == 4 ? args[3] : Path.Combine(AppContext.BaseDirectory, "GoBoard.exe");
            // Validate before connecting to SteamVR or changing any registration.
            var manifest = action is "register" or "enable" ? CreateManifest(executable) : null;
            if (!OpenVR.IsRuntimeInstalled()) throw new InvalidOperationException("SteamVR is not installed or its runtime path is not registered.");
            var error = EVRInitError.None;
            OpenVR.Init(ref error, EVRApplicationType.VRApplication_Utility);
            if (error != EVRInitError.None) throw new InvalidOperationException($"OpenVR initialization failed: {error}. Start SteamVR and try again.");
            initialized = true;
            var applications = OpenVR.Applications ?? throw new InvalidOperationException("SteamVR applications interface is unavailable.");
            var api = new ApplicationsApi(applications);
            Configure(api, action, ManifestPath, manifest);
            var installed = api.IsInstalled();
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new AutostartState(installed && api.GetAutoLaunch())));
                return 0;
            }
            Console.WriteLine($"GoBoard: registered={installed}, autostart={installed && api.GetAutoLaunch()}");
            if (installed)
            {
                var path = new StringBuilder(32768);
                var propertyError = EVRApplicationError.None;
                applications.GetApplicationPropertyString(Key, EVRApplicationProperty.BinaryPath_String, path, (uint)path.Capacity, ref propertyError);
                Check(propertyError, "Read registered executable");
                Console.WriteLine($"Executable: {path}");
            }
            Console.WriteLine($"Manifest: {ManifestPath}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"GoBoard SteamVR: {ex.Message}"); return 1; }
        finally { if (initialized) OpenVR.Shutdown(); }
    }

    internal static string CreateManifest(string executable)
    {
        var fullPath = Path.GetFullPath(executable);
        if (!string.Equals(Path.GetFileName(fullPath), "GoBoard.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            throw new ArgumentException("Select an existing production GoBoard.exe (build or publish it first).");
        using var resource = typeof(SteamVrApplication).Assembly.GetManifestResourceStream("GoBoard.vrmanifest");
        var manifest = JsonNode.Parse(resource)!;
        var app = manifest["applications"]![0]!;
        app["binary_path_windows"] = fullPath;
        app["working_directory"] = Path.GetDirectoryName(fullPath);
        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    internal static void Configure(IApplicationsApi api, string action, string manifestPath, string manifest = null)
    {
        // The stable per-user manifest path lets another build disable/unregister an
        // old installation, and lets registration update a moved executable in place.
        using var gate = new Mutex(false, "Local\\GoBoard.SteamVrRegistration");
        var held = false;
        try
        {
            try { held = gate.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("Another SteamVR registration command is busy. Try again.");
            switch (action)
            {
                case "register":
                case "enable":
                    ArgumentNullException.ThrowIfNull(manifest);
                    var autoLaunch = action == "enable" || (api.IsInstalled() && api.GetAutoLaunch());
                    Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
                    var previous = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null;
                    WriteManifest(manifestPath, manifest);
                    try { Check(api.AddManifest(manifestPath), "Register manifest"); }
                    catch
                    {
                        if (previous == null) File.Delete(manifestPath);
                        else WriteManifest(manifestPath, previous);
                        throw;
                    }
                    Check(api.SetAutoLaunch(autoLaunch), "Manifest registered, but setting autostart failed; retry enable/disable");
                    break;
                case "disable":
                    if (api.IsInstalled()) Check(api.SetAutoLaunch(false), "Disable autostart");
                    break;
                case "unregister":
                    if (api.IsInstalled()) Check(api.SetAutoLaunch(false), "Disable autostart before unregistering");
                    var result = api.RemoveManifest(manifestPath);
                    if (result != EVRApplicationError.NoManifest) Check(result, "Remove manifest");
                    File.Delete(manifestPath);
                    break;
                case "status": break;
                default: throw new ArgumentException("Unknown SteamVR action: " + action);
            }
        }
        finally { if (held) gate.ReleaseMutex(); }
    }

    private static void WriteManifest(string path, string contents)
    {
        var temporary = path + ".tmp";
        try { File.WriteAllText(temporary, contents); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void IdentifyCurrentProcess()
    {
        var applications = OpenVR.Applications;
        if (applications == null || !applications.IsApplicationInstalled(Key)) return;
        var error = applications.IdentifyApplication((uint)Environment.ProcessId, Key);
        if (error != EVRApplicationError.None) Console.Error.WriteLine($"SteamVR application identification: {error}");
    }

    private static void Check(EVRApplicationError error, string operation)
    {
        if (error != EVRApplicationError.None) throw new InvalidOperationException($"{operation}: {error}");
    }

    internal interface IApplicationsApi
    {
        bool IsInstalled();
        bool GetAutoLaunch();
        EVRApplicationError AddManifest(string path);
        EVRApplicationError RemoveManifest(string path);
        EVRApplicationError SetAutoLaunch(bool enabled);
    }

    private sealed class ApplicationsApi(CVRApplications applications) : IApplicationsApi
    {
        public bool IsInstalled() => applications.IsApplicationInstalled(Key);
        public bool GetAutoLaunch() => applications.GetApplicationAutoLaunch(Key);
        public EVRApplicationError AddManifest(string path) => applications.AddApplicationManifest(path, false);
        public EVRApplicationError RemoveManifest(string path) => applications.RemoveApplicationManifest(path);
        public EVRApplicationError SetAutoLaunch(bool enabled) => applications.SetApplicationAutoLaunch(Key, enabled);
    }
}
