using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace GoBoard.Setup;

internal static class Program
{
    internal const string Requirement = "GoBoard requires Microsoft .NET 10 Desktop Runtime. Setup can download and install it from Microsoft. An administrator prompt may appear.";
    private static string log;

    [STAThread]
    private static int Main(string[] args)
    {
        log = Path.Combine(Path.GetTempPath(), "GoBoard-runtime-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            Log("Runtime prerequisite check. Log: " + log);
            if (CheckRuntime()) return 0;
            // /check never downloads, launches UI, or changes the machine.
            if (args.Contains("/check")) return 1;
            var interactive = args.Contains("/ui:4");
            if (!interactive && !args.Contains("/accept:1"))
                throw new InvalidOperationException("Runtime missing. Run setup interactively or explicitly pass AcceptRuntimeDownload=1 for unattended installation.");
            Application.EnableVisualStyles();
            if (interactive)
            {
                using var consent = new Form { Text = "GoBoard — runtime required", ClientSize = new Size(510, 190), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
                consent.Controls.Add(new Label { Text = Requirement, Location = new Point(20, 20), Size = new Size(470, 85) });
                var download = new Button { Text = "Download and continue", DialogResult = DialogResult.OK, Location = new Point(225, 135), Size = new Size(175, 32) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(410, 135), Size = new Size(80, 32) };
                consent.Controls.AddRange(new Control[] { download, cancel });
                consent.AcceptButton = download;
                consent.CancelButton = cancel;
                if (consent.ShowDialog() != DialogResult.OK) return 1602;
            }
            if (!interactive) return InstallRuntime(CancellationToken.None, _ => { }).GetAwaiter().GetResult();
            using var progress = new ProgressDialog();
            progress.Shown += async (_, __) =>
            {
                try { progress.Result = await InstallRuntime(progress.Cancellation.Token, s => progress.Status.Text = s); }
                catch (OperationCanceledException) { progress.Result = 1602; }
                catch (Exception ex) { Fail(ex, true); progress.Result = 1603; }
                progress.Completed = true;
                progress.Close();
            };
            progress.ShowDialog();
            return progress.Result;
        }
        catch (OperationCanceledException) { return 1602; }
        catch (Exception ex) { Fail(ex, args.Contains("/ui:4")); return 1603; }
    }

    private static bool CheckRuntime()
    {
        var probe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe", "RuntimeProbe.exe");
        var start = new ProcessStartInfo(probe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.EnvironmentVariables["DOTNET_DISABLE_GUI_ERRORS"] = "1";
        using var process = Process.Start(start);
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Log("Apphost compatibility result: " + process.ExitCode + Environment.NewLine + error);
        return process.ExitCode == 0;
    }

    private static async Task<int> InstallRuntime(CancellationToken cancellation, Action<string> status)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("runtime.xml");
        var runtime = XDocument.Load(resource).Root;
        var uri = new Uri((string)runtime.Attribute("Url"));
        if (uri.Scheme != "https" || uri.Host != "builds.dotnet.microsoft.com") throw new InvalidDataException("Runtime source must be Microsoft's HTTPS download service.");
        var directory = Path.Combine(Path.GetTempPath(), "GoBoard-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "windowsdesktop-runtime.exe");
        try
        {
            return await RuntimeWorkflow.InstallAsync(async token =>
            {
                status("Downloading Microsoft .NET Desktop Runtime…");
                Log("Downloading " + uri);
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(15));
                using (var client = new HttpClient())
                using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                {
                    response.EnsureSuccessStatusCode();
                    using var input = await response.Content.ReadAsStreamAsync();
                    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                    var buffer = new byte[81920];
                    int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) != 0)
                        await output.WriteAsync(buffer, 0, count, timeout.Token);
                }
                status("Verifying Microsoft’s download…");
                using (var sha = SHA512.Create())
                using (var file = File.OpenRead(path))
                {
                    var hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                    if (!hash.Equals((string)runtime.Attribute("Sha512"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Microsoft runtime download failed SHA-512 verification. No installer was started. Retry setup.");
                }
            }, async () =>
            {
                status("Installing Microsoft .NET. Finish or cancel any administrator prompt. Cancel waits for the runtime installer to finish.");
                // Microsoft's installer requests elevation for its own shared runtime.
                // This helper and the following MSI remain in the original user's process context.
                var runtimeLog = log + ".microsoft.log";
                using var installer = Process.Start(new ProcessStartInfo(path, "/install /quiet /norestart /log \"" + runtimeLog + "\"") { UseShellExecute = false, CreateNoWindow = true });
                await Task.Run(() => installer.WaitForExit());
                Log("Microsoft installer exit: " + installer.ExitCode + "; log: " + runtimeLog);
                return installer.ExitCode;
            }, CheckRuntime, cancellation);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("The Microsoft runtime download timed out. Check your connection and retry GoBoard setup. GoBoard has not been changed.");
        }
        finally
        {
            // Only this invocation's exact payload and empty temporary directory.
            try { File.Delete(path); Directory.Delete(directory); }
            catch (IOException ex) { Log("Temporary download cleanup: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log("Temporary download cleanup: " + ex.Message); }
        }
    }

    private static void Log(string message) => File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
    private static void Fail(Exception exception, bool interactive)
    {
        Log(exception.ToString());
        if (interactive) MessageBox.Show(exception.Message + "\n\nDiagnostic log: " + log, "GoBoard setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private sealed class ProgressDialog : Form
    {
        internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        internal readonly Label Status = new Label { Location = new Point(20, 20), Size = new Size(470, 100) };
        internal int Result = 1603;
        internal bool Completed;
        internal ProgressDialog()
        {
            Text = "GoBoard — Microsoft .NET";
            ClientSize = new Size(510, 180);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            var cancel = new Button { Text = "Cancel", Location = new Point(410, 130), Size = new Size(80, 32) };
            cancel.Click += (_, __) => { Cancellation.Cancel(); cancel.Enabled = false; };
            Controls.AddRange(new Control[] { Status, cancel });
            FormClosing += (_, e) => { if (!Completed) { Cancellation.Cancel(); e.Cancel = true; } };
        }
        protected override void Dispose(bool disposing) { if (disposing) Cancellation.Dispose(); base.Dispose(disposing); }
    }
}
