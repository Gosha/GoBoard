using System.Globalization;
using GoBoard.Platform.Windows;

namespace GoBoard.App;

internal static class DesktopRuntime
{
    public static int Run(string[] args)
    {
        RuntimeSession session = null;
        try
        {
            string stopFile = null;
            double seconds = double.PositiveInfinity;
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--stop-file" when i + 1 < args.Length: stopFile = args[++i]; break;
                    case "--seconds" when i + 1 < args.Length:
                        seconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
                        if (!double.IsFinite(seconds) || seconds <= 0) throw new ArgumentException("Seconds must be positive and finite.");
                        break;
                    default: throw new ArgumentException("Usage: GoBoard --desktop [--stop-file PATH] [--seconds N]");
                }
            }
            session = RuntimeSession.TryStart();
            if (session == null) { Console.WriteLine("GoBoard is already running in this Windows session."); return 0; }
            session.StartLogging();
            ApplicationConfiguration.Initialize();
            Console.WriteLine("GoBoard desktop mode. SteamVR is not initialized. Select a window, then click the keyboard. No text field is needed for shortcuts.");
            Application.Run(new DesktopKeyboardForm(stopFile, seconds, stopRequested: () => session.StopRequested));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"GoBoard desktop: {ex.Message}"); return 1; }
        finally { session?.Dispose(); }
    }

    public static int Render(string path, bool shortcuts = false, bool numpad = false)
    {
        ApplicationConfiguration.Initialize();
        using var form = new DesktopKeyboardForm(previewOnly: true, previewShortcuts: shortcuts, previewNumpad: numpad);
        form.Show();
        Application.DoEvents();
        if (shortcuts) form.Shortcuts.ExpandPreview();
        Application.DoEvents();
        Form[] windows = shortcuts ? [form, form.Shortcuts.Launcher, form.Shortcuts.PanelWindow] : [form];
        var bounds = windows.Concat(form.FloatingControls.Windows.Where(w => w.Visible)).Select(w => w.Bounds).Aggregate(Rectangle.Union);
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.FromArgb(7, 16, 24));
        foreach (var window in windows)
            window.DrawToBitmap(bitmap, new Rectangle(window.Left - bounds.Left, window.Top - bounds.Top, window.Width, window.Height));
        using (var graphics = Graphics.FromImage(bitmap)) form.FloatingControls.DrawPreview(graphics, bounds.Location);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        form.Hide();
        Console.WriteLine($"Rendered desktop keyboard to {fullPath}");
        return 0;
    }
}
