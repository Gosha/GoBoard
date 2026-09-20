using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using WixToolset.Dtf.WindowsInstaller;

namespace GoBoard.Installer.Actions;

public static class Actions
{
    // Private properties stay in this execute session, including its error/cancel
    // exit action. They must not propagate to a nested RemoveExistingProducts MSI.
    private const string Resume = "GoBoardResume";
    private const string StopEvent = @"Local\GoBoard.Runtime.Stop";

    [CustomAction]
    public static ActionResult Stop(Session session)
    {
        try
        {
            var removing = session["REMOVE"] == "ALL";
            var processes = FindInstances(removing ? session["INSTALLFOLDER"] : null);
            // Record before sending anything so cancellation/partial shutdown can
            // restore just the instances that actually exited.
            session[Resume] = Serialize(processes);
            if (processes.Count == 0) return ActionResult.Success;

            if (processes.Any(p => p.Arguments != "--settings"))
            {
                if (!EventWaitHandle.TryOpenExisting(StopEvent, out var stop))
                    throw new InvalidOperationException("The running keyboard has no graceful stop signal. Close GoBoard and retry.");
                using (stop)
                    if (!stop.Set()) throw new InvalidOperationException("Could not signal GoBoard to stop.");
                // Do not keep the stop event open during relaunch: old releases
                // reset it at startup and use its lifetime to identify a session.
            }
            foreach (var instance in processes)
            {
                using (var process = instance.Open())
                {
                    if (process == null) continue;
                    session.Log("GoBoard: waiting for PID {0} ({1}) to exit.", instance.Pid, instance.Arguments);
                    if (instance.Arguments == "--settings") process.CloseMainWindow();
                    if (!process.WaitForExit(15000))
                        throw new InvalidOperationException("GoBoard did not close within 15 seconds. Close it and retry; no process was forcefully terminated.");
                }
            }
            if (FindInstances(removing ? session["INSTALLFOLDER"] : null).Count != 0)
                throw new InvalidOperationException("GoBoard started again during shutdown. Close it and retry.");
            session.Log("GoBoard: all captured instances exited before file replacement.");
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            Report(session, InstallMessage.Error, "Could not stop GoBoard. " + ex.Message);
            return ActionResult.Failure;
        }
    }

    [CustomAction]
    public static ActionResult Restart(Session session)
    {
        var previous = Deserialize(session[Resume]);
        // Files are committed now; an error exit must not launch the old path.
        session[Resume] = "";
        if (session["REMOVE"] == "ALL") return ActionResult.Success;
        var executable = Path.Combine(session["INSTALLFOLDER"], "GoBoard.exe");
        foreach (var mode in previous.Select(p => p.Arguments).Distinct())
        {
            try { Launch(session, executable, mode); }
            catch (Exception ex)
            {
                // The install has committed. Report the distinct launch failure
                // without misrepresenting it as a rolled-back installation.
                Report(session, InstallMessage.Warning,
                    "GoBoard was installed, but could not restart. Open it from the Start menu. " + ex.Message);
            }
        }
        return ActionResult.Success;
    }

    [CustomAction]
    public static ActionResult Recover(Session session)
    {
        foreach (var instance in Deserialize(session[Resume]))
        {
            try
            {
                using (var alive = instance.Open())
                    if (alive != null) continue;
                Launch(session, instance.Executable, instance.Arguments);
            }
            catch (Exception ex) { Report(session, InstallMessage.Warning, "Could not restore GoBoard after the cancelled/failed install. " + ex.Message); }
        }
        session[Resume] = "";
        return ActionResult.Success;
    }

    private static void Launch(Session session, string executable, string arguments)
    {
        using (var process = Process.Start(new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable), CreateNoWindow = true
        }))
        {
            if (process == null) throw new InvalidOperationException("Windows did not create a process.");
            session.Log("GoBoard: launched PID {0}, executable {1}, mode {2}.", process.Id, executable, arguments);
            if (process.WaitForExit(5000))
                throw new InvalidOperationException("The new process exited during startup (exit code " + process.ExitCode + "). See %LOCALAPPDATA%\\GoBoard\\logs.");
            session.Log("GoBoard: PID {0} remained running after startup.", process.Id);
        }
    }

    private static List<Instance> FindInstances(string installFolder)
    {
        var result = new List<Instance>();
        using var identity = WindowsIdentity.GetCurrent();
        using var current = Process.GetCurrentProcess();
        using var search = new ManagementObjectSearcher("SELECT * FROM Win32_Process WHERE Name = 'GoBoard.exe' AND SessionId = " + current.SessionId);
        using var found = search.Get();
        foreach (ManagementObject item in found)
        using (item)
        {
            using var owner = item.InvokeMethod("GetOwnerSid", null, null);
            if (owner == null || Convert.ToUInt32(owner["ReturnValue"]) != 0)
                throw new InvalidOperationException("Could not verify the owner of a GoBoard process.");
            if (!string.Equals((string)owner["Sid"], identity.User.Value, StringComparison.OrdinalIgnoreCase)) continue;
            var path = (string)item["ExecutablePath"];
            var command = (string)item["CommandLine"];
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(command))
                throw new InvalidOperationException("Could not inspect a running GoBoard process. Run the installer as the same user as GoBoard.");
            if (installFolder != null && !string.Equals(Path.GetFullPath(path),
                Path.GetFullPath(Path.Combine(installFolder, "GoBoard.exe")), StringComparison.OrdinalIgnoreCase)) continue;
            var mode = LaunchMode.RestartArguments(SplitCommandLine(command).Skip(1).ToArray());
            if (mode == null) continue;
            try
            {
                using var process = Process.GetProcessById(Convert.ToInt32(item["ProcessId"]));
                result.Add(new Instance(process.Id, process.StartTime.ToUniversalTime().Ticks, path, mode));
            }
            catch (ArgumentException) { } // Exited between enumeration and opening.
        }
        return result;
    }

    private static void Report(Session session, InstallMessage kind, string message)
    {
        session.Log("GoBoard: " + message);
        using var record = new Record(0);
        record.FormatString = message;
        session.Message(kind, record);
    }

    private sealed class Instance
    {
        internal readonly int Pid;
        internal readonly long Started;
        internal readonly string Executable, Arguments;
        internal Instance(int pid, long started, string executable, string arguments)
        { Pid = pid; Started = started; Executable = executable; Arguments = arguments; }
        internal Process Open()
        {
            Process process = null;
            try
            {
                process = Process.GetProcessById(Pid);
                if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == Started) return process;
            }
            catch (ArgumentException) { }
            process?.Dispose();
            return null;
        }
    }

    private static string Serialize(IEnumerable<Instance> instances) => string.Join("\n", instances.Select(p =>
        p.Pid + "|" + p.Started + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(p.Executable)) + "|" + p.Arguments));
    private static IEnumerable<Instance> Deserialize(string value)
    {
        foreach (var line in value.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            yield return new Instance(int.Parse(parts[0]), long.Parse(parts[1]), Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])), parts[3]);
        }
    }

    private static string[] SplitCommandLine(string command)
    {
        var pointer = CommandLineToArgvW(command, out var count);
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Could not parse GoBoard's launch mode.");
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size))).ToArray(); }
        finally { LocalFree(pointer); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
