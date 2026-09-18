using GoBoard.Vr;
using GoBoard.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--desktop") return DesktopRuntime.Run(args);
        if (args is ["--render-desktop", var desktopPath]) return DesktopRuntime.Render(desktopPath);
        if (args is ["--desktop-input-check"]) return DesktopInputCheck.Run();
        if (args is ["--desktop-shell-check"]) return DesktopInputCheck.Run(shell: true);
        if (args is ["--desktop-launch-check"]) return DesktopInputCheck.RunLaunch();
        if (args is ["--settings-input-check"]) return SettingsInputCheck.Run();
        if (args is ["--settings"])
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new SettingsForm());
            return 0;
        }
        if (args is ["--render-desktop-settings", var path])
        {
            ApplicationConfiguration.Initialize();
            using var form = new SettingsForm(previewOnly: true);
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
