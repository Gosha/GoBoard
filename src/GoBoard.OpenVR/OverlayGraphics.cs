using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SkiaSharp;
using Valve.VR;

namespace GoBoard.Vr;

// Skia still draws RGBA on the CPU. Persistent GL textures replace the async raw
// image loader, so the currently displayed frame is never cleared during redraw.
internal sealed class OverlayGraphics : IDisposable
{
    private sealed class Surface(int width, int height)
    {
        public readonly int Width = width, Height = height;
        public readonly int[] Textures = new int[2];
        public int Front = -1;
    }
    private readonly NativeWindow context;
    private readonly Dictionary<ulong, Surface> surfaces = new();

    public OverlayGraphics()
    {
        context = new NativeWindow(new NativeWindowSettings
        {
            ClientSize = new Vector2i(1, 1), StartVisible = false, StartFocused = false,
            API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core,
            Title = "GoBoard texture context"
        });
        context.Context.MakeCurrent();
        Console.WriteLine($"Persistent OpenGL overlay textures: {GL.GetString(StringName.Renderer)}.");
    }

    public void Upload(CVROverlay overlay, ulong handle, SKBitmap bitmap)
    {
        context.Context.MakeCurrent();
        if (!surfaces.TryGetValue(handle, out var surface))
        {
            // Skia's first row is the top of the image; OpenGL texture rows
            // start at the bottom. Reverse V when presenting CPU-drawn pixels.
            // U stays left-to-right; overlay pose and mouse coordinates do not change.
            var bounds = new VRTextureBounds_t { uMin = 0, uMax = 1, vMin = 1, vMax = 0 };
            var boundsResult = overlay.SetOverlayTextureBounds(handle, ref bounds);
            if (boundsResult != EVROverlayError.None)
                throw new InvalidOperationException($"Set overlay texture orientation: {boundsResult}");
            surface = new Surface(bitmap.Width, bitmap.Height);
            surfaces.Add(handle, surface);
            for (var i = 0; i < 2; i++)
            {
                surface.Textures[i] = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, surface.Textures[i]);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, bitmap.Width, bitmap.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            }
        }
        if (surface.Width != bitmap.Width || surface.Height != bitmap.Height)
            throw new InvalidOperationException("Overlay texture dimensions changed without reallocating the surface.");
        var next = surface.Front == 0 ? 1 : 0;
        GL.BindTexture(TextureTarget.Texture2D, surface.Textures[next]);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, bitmap.Width, bitmap.Height, PixelFormat.Rgba, PixelType.UnsignedByte, bitmap.GetPixels());
        // Complete the new frame before publishing it to the other process.
        GL.Finish();
        var error = GL.GetError();
        if (error != ErrorCode.NoError) throw new InvalidOperationException($"Overlay texture upload: {error}");
        var texture = new Texture_t { handle = (IntPtr)surface.Textures[next], eType = ETextureType.OpenGL, eColorSpace = EColorSpace.Gamma };
        var result = overlay.SetOverlayTexture(handle, ref texture);
        if (result != EVROverlayError.None) throw new InvalidOperationException($"Publish overlay texture: {result}");
        surface.Front = next;
    }

    public void Dispose()
    {
        // The host shuts OpenVR down first, while all submitted textures live.
        context.Context.MakeCurrent();
        foreach (var surface in surfaces.Values)
            foreach (var texture in surface.Textures) if (texture != 0) GL.DeleteTexture(texture);
        surfaces.Clear();
        context.Dispose();
    }
}
