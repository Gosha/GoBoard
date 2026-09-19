using System.Drawing.Imaging;
using SkiaSharp;

namespace GoBoard.App;

// GDI borrows the Skia pixels for their entire displayed lifetime. No format
// conversion or Bitmap clone is needed for each animated frame.
internal sealed class DesktopFrame : IDisposable
{
    private readonly SKBitmap pixels;
    private readonly Bitmap bitmap;
    public DesktopFrame(SKBitmap pixels)
    {
        this.pixels = pixels;
        try { bitmap = BorrowBitmap(pixels); }
        catch { pixels.Dispose(); throw; }
    }
    internal static Bitmap BorrowBitmap(SKBitmap pixels) => new(pixels.Width, pixels.Height, pixels.RowBytes,
        PixelFormat.Format32bppPArgb, pixels.GetPixels());
    public void Draw(Graphics graphics, Rectangle destination)
    {
        if (bitmap.Width == destination.Width && bitmap.Height == destination.Height)
            graphics.DrawImageUnscaled(bitmap, destination.Location);
        else graphics.DrawImage(bitmap, destination); // Briefly used while a resize awaits its next frame.
    }
    public void Dispose() { bitmap.Dispose(); pixels.Dispose(); }
}
