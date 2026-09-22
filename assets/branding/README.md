# Branding assets

- `goboard-logo.svg` is the editable geometry master, using rounded rectangles on a flat dark background. The exporter applies `UiColors.Logo` and `UiColors.LogoBackground` from the Steam Blue color theme, then updates the SVG and raster assets together. The README uses this SVG directly.
- `goboard-dashboard.png` is the transparent 256 x 256 SteamVR dashboard thumbnail embedded as `GoBoard.Logo.png`. The exporter omits the SVG's `background` rectangle, leaving the space around and between the blue shapes transparent. The renderer decodes it directly without a second resampling step.
- `goboard.ico` supplies the Windows executable, desktop window, and MSI shortcut/Installed apps icons. It contains PNG frames at 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixels. The MSI embeds this ICO directly rather than storing an extra copy of the executable as its icon stream.

Regenerate all raster assets from the SVG with the repository's .NET SDK and locked SkiaSharp dependency, running from the repository root:

```powershell
dotnet run --file assets/branding/render.cs --no-cache
```

The exporter reads the SVG's rectangle geometry, fills, and strokes, then rasterizes each size directly. It intentionally supports this logo's simple SVG subset and rejects unsupported shapes or rectangle attributes. If the artwork starts using paths, transforms, or gradients, extend the exporter before regenerating.

Inspect the dashboard at its actual 256-pixel size against light and dark backgrounds, and the ICO at small Windows icon sizes. The Windows ICO keeps the dark background at every size. Rebuild the app after exporting to embed the updated assets.
