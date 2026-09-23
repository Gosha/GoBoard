using GoBoard.Vr;
using GoBoard.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var diagnostics = StartupDiagnostics.Open();
        try { return Run(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GoBoard: {ex.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--steamvr") return SteamVrApplication.Run(args);
        if (args.Length > 0 && args[0] == "--steamvr-ui") return SteamVrApplication.Run(args, json: true);
        if (args is ["--stop"])
        {
            Console.WriteLine(GoBoard.Platform.Windows.RuntimeSession.RequestStop()
                ? "Requested graceful GoBoard shutdown." : "GoBoard is not running in this Windows session.");
            return 0;
        }
#if GOBOARD_DESKTOP_DEBUG
        if (args is ["--desktop-effects-benchmark"]) { DesktopEffectsBenchmark.Run(); return 0; }
        if (args.Length > 0 && args[0] == "--desktop") return DesktopRuntime.Run(args);
        if (args is ["--render-desktop", var desktopPath]) return DesktopRuntime.Render(desktopPath);
        if (args is ["--render-desktop", var numpadDesktopPath, "--numpad"]) return DesktopRuntime.Render(numpadDesktopPath, numpad: true);
        if (args is ["--render-desktop", var shortcutDesktopPath, "--shortcuts"]) return DesktopRuntime.Render(shortcutDesktopPath, shortcuts: true);
        if (args is ["--desktop-input-check"]) return DesktopInputCheck.Run();
        if (args is ["--desktop-shell-check"]) return DesktopInputCheck.Run(shell: true);
        if (args is ["--desktop-launch-check"]) return DesktopInputCheck.RunLaunch();
        if (args is ["--desktop-inactivity-check"]) return DesktopInactivityCheck.Run();
        if (args is ["--settings-input-check"]) return SettingsInputCheck.Run();
#else
        if (args.Length > 0 && args[0] is ("--desktop" or "--render-desktop" or
            "--desktop-effects-benchmark" or "--desktop-input-check" or "--desktop-shell-check" or
            "--desktop-launch-check" or "--desktop-inactivity-check" or "--settings-input-check"))
            throw new ArgumentException("Desktop keyboard debugging is available only in source builds. Installed GoBoard is a VR app.");
#endif
        if (args is ["--settings"] || args is ["--settings", "--executable", _])
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new SettingsForm(autostartExecutable: args.Length == 3 ? args[2] : null));
            return 0;
        }
        if (args.Length is 2 or 4 && args[0] == "--render-desktop-settings")
        {
            if (args.Length == 4 && args[2] != "--settings-page") throw new ArgumentException("Expected --settings-page.");
            var page = args.Length == 4 ? Enum.Parse<GoBoard.Core.SettingsPage>(args[3], ignoreCase: true) : GoBoard.Core.SettingsPage.General;
            var path = args[1];
            ApplicationConfiguration.Initialize();
            using var form = new SettingsForm(previewOnly: true, previewPage: page);
            form.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
            form.Hide();
            return 0;
        }
        return GoBoardRuntime.Run(args);
    }
}
