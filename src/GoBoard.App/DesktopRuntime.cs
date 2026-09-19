using System.Globalization;

namespace GoBoard.App;

internal static class DesktopRuntime
{
    public static int Run(string[] args)
    {
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
            using var instance = new Mutex(false, "Local\\GoBoard.Desktop", out var first);
            if (!first) throw new InvalidOperationException("GoBoard desktop mode is already running.");
            ApplicationConfiguration.Initialize();
            Console.WriteLine("GoBoard desktop mode. SteamVR is not initialized. Select a window, then click the keyboard. No text field is needed for shortcuts.");
            Application.Run(new DesktopKeyboardForm(stopFile, seconds));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"GoBoard desktop: {ex.Message}"); return 1; }
    }

    public static int Render(string path)
    {
        ApplicationConfiguration.Initialize();
        using var form = new DesktopKeyboardForm(previewOnly: true);
        form.Show();
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        form.Hide();
        Console.WriteLine($"Rendered desktop keyboard to {fullPath}");
        return 0;
    }
}
