using System.Diagnostics;
using GoBoard.Core;
using GoBoard.Presentation.Skia;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SkiaSharp;

namespace GoBoard.Vr;

// Explicit native graphics check. No OpenVR initialization, windows shown, input
// injection, or saved settings. Readbacks are verification only, never live rendering.
internal static class GpuRenderCheck
{
    private sealed class Sink : IKeySink { public void Down(ushort scan) { } public void Up(ushort scan) { } }

    public static int Run()
    {
        using var window = new NativeWindow(new NativeWindowSettings
        {
            ClientSize = new Vector2i(1, 1), StartVisible = false, StartFocused = false,
            API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core,
            Title = "GoBoard GPU renderer check"
        });
        window.Context.MakeCurrent();
        Console.WriteLine($"GPU renderer check: {GL.GetString(StringName.Renderer)}");
        using var context = GRContext.CreateGl() ?? throw new InvalidOperationException("Skia GPU context unavailable.");
        var directory = Path.GetFullPath(Path.Combine(".runtime", "gpu-check"));
        Directory.CreateDirectory(directory);
        Console.WriteLine("CPU draw vs GPU draw+flush+completion; no SteamVR, upload or display latency. Default effects, 20 warmup + 60 measured frames.");
        foreach (var theme in new[] { BoardThemes.SteamSoft, BoardThemes.SteamFlat })
        foreach (var scenario in new[] { "us", "sv", "numpad", "shortcuts" })
            CheckScenario(theme, scenario);
        Console.WriteLine($"GPU rendering check passed; previews: {directory}");
        return 0;

        void CheckScenario(string theme, string scenario)
        {
            var shortcuts = scenario == "shortcuts";
            var keyboard = new KeyboardState(new Sink(), shortcuts);
            if (scenario == "sv") keyboard.SetLayout(new WindowsLayout((nint)WindowsLayout.SwedishHandle), 0);
            if (scenario == "numpad") keyboard.SetNumpad(true, 0);
            var info = (shortcuts ? KeyboardOverlay.ShortcutTextureInfo : KeyboardOverlay.MainTextureInfo).WithAlphaType(SKAlphaType.Premul);
            SKRect? bounds = shortcuts ? null : KeyboardOverlay.MainContentBounds(keyboard);
            var textures = new int[2];
            var targets = new GlRenderTarget[2];
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    textures[i] = GL.GenTexture();
                    GL.BindTexture(TextureTarget.Texture2D, textures[i]);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, info.Width, info.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
                    targets[i] = new GlRenderTarget(context, textures[i], info.Width, info.Height);
                }
                context.ResetContext();
                targets[0].Canvas.Clear(SKColors.Transparent);
                using (var paint = new SKPaint { Color = SKColors.Red }) targets[0].Canvas.DrawRect(2, 3, 10, 10, paint);
                context.Flush(submit: true, synchronous: true);
                using (var origin = ReadTexture(textures[0], info))
                    if (origin.GetPixel(5, 6) != SKColors.Red || origin.GetPixel(5, info.Height - 7).Alpha != 0)
                        throw new InvalidOperationException("GPU texture origin does not match CPU upload orientation.");

                using var cpu = new AnimatedKeyboardRenderer();
                using var gpu = new AnimatedKeyboardRenderer();
                var options = new EffectSettings();
                var cpuTimes = new List<double>(); var gpuTimes = new List<double>();
                var worstMean = 0.0; var worstLarge = 0.0;
                for (var i = 0; i < 80; i++)
                {
                    var now = 1 + i / 90.0;
                    var key = keyboard.Keys[i / 8 % keyboard.Keys.Count];
                    var b = key.Bounds;
                    keyboard.Enter(0, 7, now);
                    keyboard.Move(0, 7, b.X + b.Width / 2, keyboard.Height - b.Y - b.Height / 2);
                    var other = keyboard.Keys[(i / 8 + 5) % keyboard.Keys.Count].Bounds;
                    keyboard.Enter(1, 8, now);
                    keyboard.Move(1, 8, other.X + other.Width / 2, keyboard.Height - other.Y - other.Height / 2);
                    if (i % 12 == 0) keyboard.Press(0, 7, b.X + b.Width / 2, keyboard.Height - b.Y - b.Height / 2, now, now);
                    if (i % 12 == 2) keyboard.Up(0, 7, now);
                    var shift = i / 10 % 2 != 0;
                    var started = Stopwatch.GetTimestamp();
                    using var expected = cpu.Render(keyboard, shift, null, false, false, false, theme, options, now, info, bounds);
                    var cpuMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var next = i % 2;
                    context.ResetContext();
                    started = Stopwatch.GetTimestamp();
                    var changed = gpu.Draw(targets[next].Canvas, context, keyboard, shift, null, false, false, false, theme, options, now, info, bounds);
                    context.Flush(submit: true, synchronous: false);
                    GL.Finish();
                    var gpuMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    if ((expected != null) != changed) throw new InvalidOperationException("CPU/GPU invalidation differs.");
                    if (i >= 20 && changed) { cpuTimes.Add(cpuMs); gpuTimes.Add(gpuMs); }
                    if (changed && i is 0 or 15 or 35 or 79)
                    {
                        using var actual = ReadTexture(textures[next], info);
                        var a = actual.Bytes; var e = expected.Bytes;
                        long total = 0, large = 0;
                        for (var p = 0; p < a.Length; p++) { var delta = Math.Abs(a[p] - e[p]); total += delta; if (delta > 32) large++; }
                        var mean = total / (double)a.Length; var fraction = large / (double)a.Length;
                        worstMean = Math.Max(worstMean, mean); worstLarge = Math.Max(worstLarge, fraction);
                        if (i == 35) { Save(actual, $"{theme}-{scenario}-gpu"); Save(expected, $"{theme}-{scenario}-cpu"); }
                        // GPU/raster antialiasing differs; detect substantial geometry,
                        // orientation, alpha or stale-frame errors, then inspect PNGs.
                        if (mean > 3 || fraction > .035)
                            throw new InvalidOperationException($"GPU image mismatch {theme}/{scenario}/{i}: mean={mean:F3}, large={fraction:P3}");
                    }
                }
                keyboard.Cancel(3);
                context.ResetContext();
                if (!gpu.Draw(targets[0].Canvas, context, keyboard, false, null, false, false, false, theme, options, 3, info, bounds))
                    throw new InvalidOperationException("Cancellation did not invalidate the GPU frame.");
                if (gpu.Draw(targets[1].Canvas, context, keyboard, false, null, false, false, false, theme, options, 4, info, bounds))
                    throw new InvalidOperationException("Settled GPU renderer failed to become idle.");
                context.Flush(submit: true, synchronous: true);
                using (var settled = ReadTexture(textures[0], info)) Save(settled, $"{theme}-{scenario}-settled-gpu");
                // Switching backend must discard context-bound caches and yield a
                // complete CPU frame, even with otherwise unchanged state.
                using var fallback = gpu.Render(keyboard, false, null, false, false, false, theme, options, 4, info, bounds);
                using var fresh = new AnimatedKeyboardRenderer();
                using var reference = fresh.Render(keyboard, false, null, false, false, false, theme, options, 4, info, bounds);
                if (fallback == null || !fallback.Bytes.SequenceEqual(reference.Bytes))
                    throw new InvalidOperationException("GPU-to-CPU fallback left stale cache state.");
                Save(reference, $"{theme}-{scenario}-settled-cpu");
                cpuTimes.Sort(); gpuTimes.Sort();
                Console.WriteLine(FormattableString.Invariant($"{theme}/{scenario} {info.Width}x{info.Height}: CPU p50={cpuTimes[cpuTimes.Count / 2]:F3} p95={cpuTimes[(int)(cpuTimes.Count * .95)]:F3} ms; GPU p50={gpuTimes[gpuTimes.Count / 2]:F3} p95={gpuTimes[(int)(gpuTimes.Count * .95)]:F3} ms; image mean<={worstMean:F3}, large<={worstLarge:P3}"));
                context.Flush(submit: true, synchronous: true);
            }
            finally
            {
                foreach (var target in targets) target?.Dispose();
                foreach (var texture in textures) if (texture != 0) GL.DeleteTexture(texture);
                context.ResetContext();
            }
        }
        void Save(SKBitmap bitmap, string name)
        {
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(directory, name + ".png"));
            data.SaveTo(file);
        }
    }

    private static SKBitmap ReadTexture(int texture, SKImageInfo info)
    {
        var bitmap = new SKBitmap(info);
        GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);
        GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 4);
        GL.PixelStore(PixelStoreParameter.PackRowLength, 0);
        GL.PixelStore(PixelStoreParameter.PackSkipPixels, 0);
        GL.PixelStore(PixelStoreParameter.PackSkipRows, 0);
        GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, bitmap.GetPixels());
        return bitmap;
    }
}
