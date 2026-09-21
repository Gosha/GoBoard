using System.Diagnostics;
using System.Reflection;
using SkiaSharp;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

if (args.Length != 1 || args[0] is not ("gpu" or "straight" or "premul" or "copies"))
{
    Console.Error.WriteLine("Usage: GoBoard.RenderingProbe gpu|straight|premul|copies");
    Environment.ExitCode = 1;
    return;
}

if (args is ["gpu"])
{
    using var window = new NativeWindow(new NativeWindowSettings
    {
        ClientSize = new Vector2i(1, 1), StartVisible = false, StartFocused = false,
        API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core,
        Title = "GoBoard disposable GPU capability probe"
    });
    window.Context.MakeCurrent();
    Console.WriteLine($"GPU: {GL.GetString(StringName.Renderer)}; GL: {GL.GetString(StringName.Version)}");
    using var context = GRContext.CreateGl() ?? throw new Exception("Skia GL context unavailable");
    using var surface = SKSurface.Create(context, true, new SKImageInfo(3138, 846, SKColorType.Rgba8888, SKAlphaType.Premul))
        ?? throw new Exception("Skia GPU surface unavailable");
    surface.Canvas.Clear(SKColors.CornflowerBlue);
    using var paint = new SKPaint { Color = SKColors.Red };
    surface.Canvas.DrawRect(10, 10, 50, 50, paint);
    context.Flush(submit: true, synchronous: true);
    using var snapshot = surface.Snapshot();
    using var pixels = new SKBitmap(3138, 846, SKColorType.Rgba8888, SKAlphaType.Premul);
    if (!snapshot.IsTextureBacked || !snapshot.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0) ||
        pixels.GetPixel(20, 20) != SKColors.Red || pixels.GetPixel(100, 100) != SKColors.CornflowerBlue)
        throw new Exception("GPU drawing/readback check failed");
    Console.WriteLine("SkiaSharp 4.152.1 GPU surface and drawing/readback check passed. No SteamVR submission or GPU performance measurement.");
    return;
}

if (args[0] is "straight" or "premul")
{
    var alpha = args[0] == "premul" ? SKAlphaType.Premul : SKAlphaType.Unpremul;
    var benchmark = Assembly.Load("GoBoard.Presentation.Skia").GetType("GoBoard.Presentation.Skia.EffectsBenchmark")!;
    Console.WriteLine($"Output alpha: {alpha}; fixed live main texture dimensions, base keyboard content scaled to fill (not padded). Diagnostic only.");
    benchmark.GetMethod("Run")!.Invoke(null, [new SKImageInfo(3138, 846, SKColorType.Rgba8888, alpha), null]);
    return;
}
foreach (var immutable in new[] { false, true })
{
    using var bitmap = new SKBitmap(3138, 846, SKColorType.Rgba8888, SKAlphaType.Unpremul);
    using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.CornflowerBlue);
    if (immutable) bitmap.SetImmutable();
    var samples = new List<double>();
    for (var i = 0; i < 220; i++)
    {
        var watch = Stopwatch.StartNew();
        using var image = SKImage.FromBitmap(bitmap);
        watch.Stop();
        if (i >= 20) samples.Add(watch.Elapsed.TotalMilliseconds);
    }
    samples.Sort();
    Console.WriteLine(FormattableString.Invariant($"FromBitmap immutable={immutable}: median={samples[100]:F4} ms p95={samples[190]:F4} ms"));
}
