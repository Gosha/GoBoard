#:property TargetFramework=net10.0-windows
#:property AssemblyName=GoBoard.Branding
#:project ../../src/GoBoard.Presentation.Skia/GoBoard.Presentation.Skia.csproj

using System.Globalization;
using System.Xml.Linq;
using SkiaSharp;
using GoBoard.Presentation.Skia;

// Run from the repository root: dotnet run --file assets/branding/render.cs --no-cache
// This logo deliberately uses only flat, filled or stroked SVG rectangles. Read their
// geometry from the SVG so it remains the single source for every export.
var directory = Path.GetFullPath("assets/branding");
var svgPath = Path.Combine(directory, "goboard-logo.svg");
var document = XDocument.Load(svgPath, LoadOptions.PreserveWhitespace);
var svg = document.Root!;
XNamespace ns = "http://www.w3.org/2000/svg";
if (svg.Name != ns + "svg" || (string?)svg.Attribute("viewBox") != "0 0 1254 1254")
    throw new InvalidDataException("Expected the logo's 1254-square SVG viewBox.");
var shapes = svg.Elements().Where(e => e.Name != ns + "title" && e.Name != ns + "desc").ToArray();
string[] attributes = ["id", "x", "y", "width", "height", "rx", "fill", "stroke", "stroke-width"];
foreach (var shape in shapes)
    if (shape.Name != ns + "rect" || shape.Attributes().Any(a => !attributes.Contains(a.Name.ToString())))
        throw new InvalidDataException("The branding exporter supports only rectangles with id/x/y/width/height/rx/fill/stroke/stroke-width.");

// Geometry is edited in the SVG; colors come from the same theme mapping as
// the app. Keep the checked-in SVG usable directly by README/browser consumers.
var colors = UiColors.Current;
static string Hex(SKColor color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
svg.SetAttributeValue("fill", Hex(colors.Logo));
foreach (var shape in shapes)
{
    if ((string?)shape.Attribute("id") == "background")
        shape.SetAttributeValue("fill", Hex(colors.LogoBackground));
    if (shape.Attribute("stroke") != null)
        shape.SetAttributeValue("stroke", Hex(colors.Logo));
}
File.WriteAllText(svgPath, document.ToString(SaveOptions.DisableFormatting).Replace("\r\n", "\n"));

byte[] Render(int size, bool transparent = false)
{
    using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(size / 1254f);
    using var paint = new SKPaint { IsAntialias = true };
    foreach (var shape in shapes)
    {
        if (transparent && (string?)shape.Attribute("id") == "background")
            continue;
        float Number(string name) => float.Parse((string?)shape.Attribute(name) ?? "0", CultureInfo.InvariantCulture);
        var bounds = SKRect.Create(Number("x"), Number("y"), Number("width"), Number("height"));
        var fill = (string?)shape.Attribute("fill") ?? (string?)svg.Attribute("fill") ?? "black";
        if (fill != "none")
        {
            paint.Style = SKPaintStyle.Fill;
            paint.Color = SKColor.Parse(fill);
            canvas.DrawRoundRect(bounds, Number("rx"), Number("rx"), paint);
        }
        var stroke = (string?)shape.Attribute("stroke");
        if (stroke is not null && stroke != "none")
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = Number("stroke-width");
            paint.Color = SKColor.Parse(stroke);
            canvas.DrawRoundRect(bounds, Number("rx"), Number("rx"), paint);
        }
    }
    canvas.Flush();
    using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    return png.ToArray();
}

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var frames = sizes.Select(size => Render(size)).ToArray();
File.WriteAllBytes(Path.Combine(directory, "goboard-dashboard.png"), Render(256, transparent: true));
using var file = File.Create(Path.Combine(directory, "goboard.ico"));
using var writer = new BinaryWriter(file);
writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
int offset = 6 + 16 * sizes.Length;
for (int i = 0; i < sizes.Length; i++)
{
    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
    writer.Write((byte)0); writer.Write((byte)0);
    writer.Write((ushort)1); writer.Write((ushort)32);
    writer.Write(frames[i].Length); writer.Write(offset);
    offset += frames[i].Length;
}
foreach (var frame in frames) writer.Write(frame);
Console.WriteLine("Exported dashboard PNG and nine Windows ICO frames from goboard-logo.svg.");
