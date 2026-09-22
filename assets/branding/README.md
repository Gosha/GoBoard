# Branding assets

- `goboard-logo.png` is the 1254 x 1254 master. Its keyboard mark uses SteamVR Dashboard blue (`#1A9FFF`), matching `BrandColors.Accent` in the shared renderer. Keep it for source artwork; it is not embedded in the app.
- `goboard-dashboard.png` is the 256 x 256 SteamVR dashboard thumbnail embedded as `GoBoard.Logo.png`. The renderer decodes it directly without a second resampling step.
- `goboard.ico` supplies the Windows executable, desktop window, and MSI shortcut/Installed apps icons. It contains PNG frames at 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixels, each sampled directly from the master. The MSI embeds this ICO directly rather than storing an extra copy of the executable as its icon stream.

To regenerate the dashboard asset after changing the master, use the version of SkiaSharp locked by `GoBoard.Presentation.Skia` and the same operations used by the original renderer:

```csharp
using SkiaSharp;

using var logo = SKImage.FromEncodedData("assets/branding/goboard-logo.png");
using var bitmap = new SKBitmap(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);
using (var canvas = new SKCanvas(bitmap))
    canvas.DrawImage(logo, new SKRect(0, 0, 256, 256),
        new SKSamplingOptions(SKCubicResampler.Mitchell));
using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes("assets/branding/goboard-dashboard.png", png.ToArray());
```

Regenerate each Windows ICO frame with the same sampling at its target size; the 256-pixel frame matches the dashboard PNG. Inspect the regenerated dashboard at its actual 256-pixel size and the ICO at small Windows icon sizes.
