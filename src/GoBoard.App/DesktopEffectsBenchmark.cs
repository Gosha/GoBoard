using System.Diagnostics;
using System.Drawing.Imaging;
using GoBoard.Presentation.Skia;
using SkiaSharp;

namespace GoBoard.App;

internal static class DesktopEffectsBenchmark
{
    public static void Run()
    {
        using var pixels = PanelPreview.Render("sv", "reference");
        using var destination = new Bitmap(1275, 423, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(destination);
        var convert = new List<double>(); var clone = new List<double>(); var paint = new List<double>();
        for (var i = 0; i < 65; i++)
        {
            var watch = Stopwatch.StartNew();
            using var bgra = pixels.Copy(SKColorType.Bgra8888);
            var conversion = watch.Elapsed.TotalMilliseconds;
            using var borrowed = new Bitmap(bgra.Width, bgra.Height, bgra.RowBytes, PixelFormat.Format32bppPArgb, bgra.GetPixels());
            watch.Restart();
            using var frame = new Bitmap(borrowed);
            var cloning = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            graphics.DrawImage(frame, new Rectangle(0, 0, destination.Width, destination.Height));
            if (i >= 5) { convert.Add(conversion); clone.Add(cloning); paint.Add(watch.Elapsed.TotalMilliseconds); }
        }
        foreach (var (name, values) in new[] { ("RGBA to BGRA", convert), ("Bitmap clone", clone), ("GDI scaled paint", paint) })
        {
            values.Sort();
            Console.WriteLine(FormattableString.Invariant($"{name}: median {values[30]:F3} ms, p95 {values[57]:F3} ms"));
        }
        using var native = new SKBitmap(1275, 423, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(native))
        {
            using var image = SKImage.FromBitmap(pixels);
            canvas.DrawImage(image, new SKRect(0, 0, native.Width, native.Height), new SKSamplingOptions(SKFilterMode.Linear));
        }
        var direct = new List<double>();
        for (var i = 0; i < 65; i++)
        {
            var watch = Stopwatch.StartNew();
            using var borrowed = DesktopFrame.BorrowBitmap(native);
            graphics.DrawImageUnscaled(borrowed, Point.Empty);
            if (i >= 5) direct.Add(watch.Elapsed.TotalMilliseconds);
        }
        direct.Sort();
        Console.WriteLine(FormattableString.Invariant($"Direct BGRA wrap + unscaled paint: median {direct[30]:F3} ms, p95 {direct[57]:F3} ms"));
        EffectsBenchmark.Run(new SKImageInfo(1275, 423, SKColorType.Bgra8888, SKAlphaType.Opaque), frame =>
        {
            using var borrowed = DesktopFrame.BorrowBitmap(frame);
            graphics.DrawImageUnscaled(borrowed, Point.Empty);
        });
    }
}
