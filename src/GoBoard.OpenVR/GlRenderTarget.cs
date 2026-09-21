using OpenTK.Graphics.OpenGL4;
using SkiaSharp;

namespace GoBoard.Vr;

// Wraps a host-owned persistent texture. The host also owns the current GL context.
internal sealed class GlRenderTarget : IDisposable
{
    private int framebuffer, stencil;
    private GRBackendRenderTarget backend;
    private SKSurface surface;
    public SKCanvas Canvas => surface.Canvas;

    public GlRenderTarget(GRContext context, int texture, int width, int height)
    {
        try
        {
            framebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
            stencil = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, stencil);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, width, height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, stencil);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException("Incomplete overlay GPU framebuffer.");
            backend = new GRBackendRenderTarget(width, height, 0, 8,
                new GRGlFramebufferInfo((uint)framebuffer, (uint)SizedInternalFormat.Rgba8));
            context.ResetContext();
            // Logical top row occupies GL texture row zero, just like CPU uploads.
            // OpenVR's existing V 1 -> 0 bounds remain the single presentation flip.
            surface = SKSurface.Create(context, backend, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888)
                ?? throw new InvalidOperationException("Could not wrap an overlay texture with Skia.");
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        surface?.Dispose(); surface = null;
        backend?.Dispose(); backend = null;
        if (stencil != 0) { GL.DeleteRenderbuffer(stencil); stencil = 0; }
        if (framebuffer != 0) { GL.DeleteFramebuffer(framebuffer); framebuffer = 0; }
    }
}
