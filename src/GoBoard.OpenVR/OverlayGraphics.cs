using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SkiaSharp;
using Valve.VR;

namespace GoBoard.Vr;

// Persistent textures support direct Skia GPU rendering and CPU upload fallback.
internal sealed class OverlayGraphics : IDisposable
{
    private sealed class Surface(int width, int height)
    {
        public readonly int Width = width, Height = height;
        public readonly int[] Textures = new int[2];
        public readonly GlRenderTarget[] Targets = new GlRenderTarget[2];
        public int Front = -1;
        public bool? Premultiplied;
    }
    private readonly NativeWindow context;
    private GRContext skia;
    private bool gpuEnabled;
    public bool GpuEnabled => gpuEnabled;
    public RenderTimings Timings { get; } = new();
    // Submitted texture pairs stay alive until OpenVR shutdown. Hosts must keep
    // each overlay's raster size fixed; SteamVR can retain its initial GL size.
    private readonly Dictionary<(ulong Handle, int Width, int Height), Surface> surfaces = new();

    public OverlayGraphics(bool? useGpu = null)
    {
        context = new NativeWindow(new NativeWindowSettings
        {
            ClientSize = new Vector2i(1, 1), StartVisible = false, StartFocused = false,
            API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core,
            Title = "GoBoard texture context"
        });
        context.Context.MakeCurrent();
        Console.WriteLine($"Persistent OpenGL overlay textures: {GL.GetString(StringName.Renderer)}.");
        var mode = Environment.GetEnvironmentVariable("GOBOARD_RENDERER");
        if (useGpu ?? !string.Equals(mode, "cpu", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                skia = GRContext.CreateGl() ?? throw new InvalidOperationException("Skia GL context unavailable.");
                gpuEnabled = true;
            }
            catch (Exception ex) { DisableGpu(ex); }
        }
        Console.WriteLine($"Keyboard renderer: {(gpuEnabled ? "Skia GPU/OpenGL" : "Skia CPU/upload")}. GOBOARD_RENDERER=cpu forces CPU rendering.");
    }

    private Surface GetSurface(CVROverlay overlay, ulong handle, int width, int height)
    {
        context.Context.MakeCurrent();
        var identity = (handle, width, height);
        if (!surfaces.TryGetValue(identity, out var surface))
        {
            // Skia's first row is the top of the image; OpenGL texture rows
            // start at the bottom. Reverse V when presenting CPU-drawn pixels.
            // U stays left-to-right; overlay pose and mouse coordinates do not change.
            var bounds = new VRTextureBounds_t { uMin = 0, uMax = 1, vMin = 1, vMax = 0 };
            var boundsResult = overlay.SetOverlayTextureBounds(handle, ref bounds);
            if (boundsResult != EVROverlayError.None)
                throw new InvalidOperationException($"Set overlay texture orientation: {boundsResult}");
            surface = new Surface(width, height);
            surfaces.Add(identity, surface);
            for (var i = 0; i < 2; i++)
            {
                surface.Textures[i] = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, surface.Textures[i]);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            }
        }
        return surface;
    }

    // True includes an unchanged frame: the caller must not rasterize it again.
    // Failures in publication are deliberately not hidden as renderer fallback.
    public bool TryDraw(CVROverlay overlay, ulong handle, SKImageInfo info, Func<SKCanvas, GRContext, bool> draw)
    {
        if (!gpuEnabled) return false;
        var surface = GetSurface(overlay, handle, info.Width, info.Height);
        var next = surface.Front == 0 ? 1 : 0;
        try
        {
            surface.Targets[next] ??= new GlRenderTarget(skia, surface.Textures[next], info.Width, info.Height);
            // Raw GL uploads and OpenVR publication can change bindings behind Skia.
            skia.ResetContext();
            var started = Timings.Start();
            var changed = draw(surface.Targets[next].Canvas, skia);
            if (!changed) return true;
            skia.Flush(submit: true, synchronous: false);
            Timings.End("gpu.draw-and-flush", started);
            Complete();
        }
        catch (Exception ex)
        {
            DisableGpu(ex);
            return false;
        }
        Publish(overlay, handle, surface, next, premultiplied: true);
        return true;
    }

    private void DisableGpu(Exception ex)
    {
        gpuEnabled = false;
        // Discard queued Skia work so it cannot later overwrite a CPU fallback
        // upload into the same back texture. Host-owned GL objects remain alive.
        skia?.AbandonContext(releaseResources: false);
        // Keep context/surface resources alive through OpenVR shutdown even after
        // fallback. The renderer resets its GPU caches when switched back to CPU.
        Console.Error.WriteLine($"Skia GPU rendering unavailable ({ex.GetType().Name}); falling back to CPU/upload.");
    }

    public void Upload(CVROverlay overlay, ulong handle, SKBitmap bitmap)
    {
        var surface = GetSurface(overlay, handle, bitmap.Width, bitmap.Height);
        var next = surface.Front == 0 ? 1 : 0;
        var started = Timings.Start();
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
        GL.BindTexture(TextureTarget.Texture2D, surface.Textures[next]);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, bitmap.RowBytes / 4);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, bitmap.Width, bitmap.Height, PixelFormat.Rgba, PixelType.UnsignedByte, bitmap.GetPixels());
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        Timings.End("cpu.texture-upload", started);
        Complete();
        Publish(overlay, handle, surface, next, bitmap.AlphaType == SKAlphaType.Premul);
    }

    private void Complete()
    {
        // Complete the new frame before publishing it to the other process.
        // Flush alone does not establish cross-process readiness.
        var started = Timings.Start();
        GL.Finish();
        Timings.End("texture.gpu-wait", started);
        var error = GL.GetError();
        if (error != ErrorCode.NoError) throw new InvalidOperationException($"Overlay texture upload: {error}");
    }

    private void Publish(CVROverlay overlay, ulong handle, Surface surface, int next, bool premultiplied)
    {
        if (surface.Premultiplied != premultiplied)
        {
            var flag = overlay.SetOverlayFlag(handle, VROverlayFlags.IsPremultiplied, premultiplied);
            if (flag != EVROverlayError.None) throw new InvalidOperationException($"Overlay alpha mode: {flag}");
            surface.Premultiplied = premultiplied;
        }
        var texture = new Texture_t { handle = (IntPtr)surface.Textures[next], eType = ETextureType.OpenGL, eColorSpace = EColorSpace.Gamma };
        var started = Timings.Start();
        var result = overlay.SetOverlayTexture(handle, ref texture);
        Timings.End("texture.publish", started);
        if (result != EVROverlayError.None) throw new InvalidOperationException($"Publish overlay texture: {result}");
        surface.Front = next;
    }

    public void Dispose()
    {
        // The host shuts OpenVR down first, while all submitted textures live.
        context.Context.MakeCurrent();
        Timings.Report(force: true);
        foreach (var surface in surfaces.Values)
            foreach (var target in surface.Targets) target?.Dispose();
        skia?.Dispose();
        foreach (var surface in surfaces.Values)
            foreach (var texture in surface.Textures) if (texture != 0) GL.DeleteTexture(texture);
        surfaces.Clear();
        context.Dispose();
    }
}
