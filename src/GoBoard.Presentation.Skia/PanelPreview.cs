using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

// Preview fixtures exercise the production renderer/state without Windows input.
internal static class PanelPreview
{
    // Explicit, headless timing check. No PNG encoding, Windows input, or VR upload.
    public static void Benchmark()
    {
        foreach (var theme in new[] { BoardThemes.SteamFlat, BoardThemes.SteamSoft })
        {
            for (var i = 0; i < 10; i++) using (Render("sv", "reference", theme)) { }
            var samples = new double[100];
            for (var i = 0; i < samples.Length; i++)
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                using (Render("sv", i % 2 == 0 ? "reference" : "pressed", theme)) { }
                samples[i] = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            Array.Sort(samples);
            Console.WriteLine($"{theme}: median {samples[50]:F2} ms, p95 {samples[95]:F2} ms (100 frames, {Panel.LayoutWidth * Panel.RasterScale}x{Panel.LayoutHeight * Panel.RasterScale})");
        }
    }

    public static readonly string[] States = ["idle", "hover", "pressed", "oneshot", "locked", "shift", "caps", "scrolllock", "altgr", "unsupported", "error", "reference"];
    private sealed class PreviewSink : IKeySink
    {
        public void Down(ushort scan) { }
        public void Up(ushort scan) { }
    }

    public static SKBitmap Render(string language, string visualState, string theme = BoardThemes.Default, bool cacheSurfaces = true, bool shortcuts = false)
    {
        if (language is not ("us" or "sv" or "ja")) throw new ArgumentException("Preview layout must be us, sv or ja.");
        return Render(new WindowsLayout((nint)(language == "ja" ? 0x04110411u : language == "sv" ? WindowsLayout.SwedishHandle : WindowsLayout.UsHandle)), visualState, theme, cacheSurfaces, shortcuts);
    }

    public static SKBitmap Render(WindowsLayout layout, string visualState, string theme = BoardThemes.Default, bool cacheSurfaces = true, bool shortcuts = false)
    {
        if (!States.Contains(visualState)) throw new ArgumentException($"Preview state must be {string.Join(", ", States)}.");
        var keyboard = new KeyboardState(new PreviewSink());
        keyboard.SetLayout(layout, 0);
        keyboard.SetShortcuts(new() { Enabled = shortcuts }, 0);
        keyboard.Enter(0, 7, 1);
        double time = 2;
        void Press(string id, bool release = true)
        {
            var b = keyboard.Keys.Single(k => k.Id == id).Bounds;
            keyboard.Press(0, 7, b.X + b.Width / 2, OverlayGeometry.PanelHeight - b.Y - b.Height / 2, time, time);
            if (release) keyboard.Up(0, 7, time + .01);
            time += .1;
        }
        switch (visualState)
        {
            case "reference":
                Press("Ctrl"); Press("Ctrl"); Press("Alt");
                goto case "hover";
            case "hover":
                var e = keyboard.Keys.Single(k => k.Id == "e").Bounds;
                keyboard.Move(0, 7, e.X + e.Width / 2, OverlayGeometry.PanelHeight - e.Y - e.Height / 2);
                break;
            case "pressed": Press("e", false); break;
            case "oneshot": Press("Ctrl"); Press("Shift"); break;
            case "locked": Press("Ctrl"); Press("Ctrl"); Press("Shift"); Press("Shift"); break;
            case "shift": Press("Shift"); break;
            case "altgr": Press("AltGr"); break;
            case "unsupported": keyboard.SetLayout(new WindowsLayout((nint)0x08090809), time); break;
        }
        var result = Panel.Render(keyboard, keyboard.Shift,
            visualState == "error" ? "Input could not be sent. Focus a text field and try again." : null,
            keyboard.AltGr, visualState == "caps", visualState == "scrolllock", theme, cacheSurfaces);
        if (!shortcuts) return result;
        using var main = result;
        var palette = new KeyboardState(new PreviewSink(), shortcutsOnly: true);
        palette.SetLayout(layout, 0);
        using var keys = Panel.Render(palette, theme: theme);
        using var button = ShortcutLauncherRenderer.Render(true, false, theme);
        const int gap = 14 * Panel.RasterScale;
        var bitmap = new SKBitmap(keys.Width + button.Width + main.Width + gap * 2, Math.Max(keys.Height, main.Height), SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(7, 16, 24));
        canvas.DrawBitmap(keys, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        canvas.DrawBitmap(button, keys.Width + gap, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        canvas.DrawBitmap(main, keys.Width + gap * 2 + button.Width, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return bitmap;
    }
}
