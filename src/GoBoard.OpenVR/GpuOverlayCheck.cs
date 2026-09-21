using GoBoard.Core;
using GoBoard.Presentation.Skia;
using SkiaSharp;
using Valve.VR;

namespace GoBoard.Vr;

// Explicit integration check: one hidden, uniquely named overlay, no input or
// settings. Exercises real publication and a deliberately failed GPU draw.
internal static class GpuOverlayCheck
{
    private sealed class Sink : IKeySink { public void Down(ushort scan) { } public void Up(ushort scan) { } }

    public static int Run()
    {
        var error = EVRInitError.None;
        OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
        if (error != EVRInitError.None) throw new InvalidOperationException($"Initialize GPU overlay check: {error}");
        ulong handle = 0;
        OverlayGraphics graphics = null;
        AnimatedKeyboardRenderer renderer = null;
        try
        {
            var overlay = OpenVR.Overlay;
            Check(overlay.CreateOverlay("goboard.check.gpu." + Guid.NewGuid().ToString("N"), "GoBoard hidden GPU check", ref handle));
            Check(overlay.SetOverlayAlpha(handle, 0));
            graphics = new(useGpu: true);
            Require(graphics.GpuEnabled, "GPU initialization fell back unexpectedly");
            renderer = new();
            var keyboard = new KeyboardState(new Sink());
            var options = new EffectSettings();
            var info = KeyboardOverlay.MainTextureInfo;
            var bounds = KeyboardOverlay.MainContentBounds(keyboard);
            for (var i = 0; i < 4; i++)
            {
                var now = (double)i;
                var shift = i % 2 == 1;
                Require(graphics.TryDraw(overlay, handle, info, (canvas, context) => renderer.Draw(canvas, context,
                    keyboard, shift, null, false, false, false, BoardThemes.Default, options, now,
                    info.WithAlphaType(SKAlphaType.Premul), bounds)), "GPU publication failed");
                Require(graphics.GpuEnabled, "GPU drawing fell back unexpectedly");
                bool premul = false;
                Check(overlay.GetOverlayFlag(handle, VROverlayFlags.IsPremultiplied, ref premul));
                Require(premul, "GPU overlay is missing premultiplied alpha flag");
                using var pixels = Read();
                Require(pixels.GetPixel(0, 0).Alpha == 0 && pixels.GetPixel(info.Width / 2, info.Height / 2).Alpha > 0,
                    "GPU publication lost transparent padding or keyboard pixels");
            }
            Require(!graphics.TryDraw(overlay, handle, info, (canvas, _) =>
                {
                    canvas.Clear(SKColors.Magenta); // Leave queued work to be abandoned.
                    throw new InvalidOperationException("Injected GPU failure");
                }),
                "Injected failure did not request CPU fallback");
            Require(!graphics.GpuEnabled, "Failed GPU backend was left enabled");
            using var fallback = renderer.Render(keyboard, false, null, false, false, false, BoardThemes.Default, options, 5, info, bounds);
            Require(fallback != null, "Fallback did not invalidate GPU caches");
            graphics.Upload(overlay, handle, fallback);
            bool alpha = true;
            Check(overlay.GetOverlayFlag(handle, VROverlayFlags.IsPremultiplied, ref alpha));
            Require(!alpha, "CPU fallback retained the GPU alpha flag");
            using var actual = Read();
            Require(actual.Bytes.SequenceEqual(fallback.Bytes), "CPU fallback texture does not match submitted pixels");
            var uv = new VRTextureBounds_t();
            Check(overlay.GetOverlayTextureBounds(handle, ref uv));
            Require(uv.uMin == 0 && uv.uMax == 1 && uv.vMin == 1 && uv.vMax == 0, "Fallback changed texture orientation");
            Require(!graphics.TryDraw(overlay, handle, info, (_, _) => throw new Exception("Disabled GPU draw was invoked")),
                "Disabled GPU unexpectedly accepted another frame");
            Console.WriteLine("GPU overlay check passed: real texture publication, alpha flags, alternating textures, injected failure, CPU fallback readback and UV bounds. No input or settings changed.");
            return 0;

            SKBitmap Read()
            {
                var bitmap = new SKBitmap(info);
                uint width = 0, height = 0;
                try
                {
                    Check(overlay.GetOverlayImageData(handle, bitmap.GetPixels(), (uint)bitmap.ByteCount, ref width, ref height));
                    Require(width == info.Width && height == info.Height, "Published texture dimensions differ");
                    return bitmap;
                }
                catch { bitmap.Dispose(); throw; }
            }
        }
        finally
        {
            renderer?.Dispose();
            if (handle != 0) OpenVR.Overlay.DestroyOverlay(handle);
            OpenVR.Shutdown();
            graphics?.Dispose();
        }
    }
    private static void Check(EVROverlayError error)
    {
        if (error != EVROverlayError.None) throw new InvalidOperationException($"GPU overlay check: {error}");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
