# Branding assets

- `goboard-logo.png` is the 1254 x 1254 master. Keep it for source artwork; it is not embedded in the app.
- `goboard-dashboard.png` is the 256 x 256 SteamVR dashboard thumbnail embedded as `GoBoard.Logo.png`. It is the lossless PNG encoding of the previous production `SettingsPanel.Icon()` output, preserving its pixels. The renderer decodes it directly without a second resampling step.
- `goboard.ico` supplies the Windows executable, desktop window, and MSI shortcut/Installed apps icons. The MSI embeds this ICO directly rather than storing an extra copy of the executable as its icon stream.

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

Inspect the regenerated icon at its actual 256-pixel size. The initial conversion from the master reduced the encoded resource from 1,398,238 to 64,646 bytes. Both the old renderer output and the new decoded resource have identical RGBA pixels.
