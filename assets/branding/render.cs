#:property TargetFramework=net10.0-windows
#:project ../../src/GoBoard.Presentation.Skia/GoBoard.Presentation.Skia.csproj

using System.Globalization;
using System.Xml.Linq;
using SkiaSharp;

// Run from the repository root: dotnet run --file assets/branding/render.cs
// This logo deliberately uses only flat, filled SVG rectangles. Read their
// geometry from the SVG so it remains the single source for every export.
var directory = Path.GetFullPath("assets/branding");
var svg = XDocument.Load(Path.Combine(directory, "goboard-logo.svg")).Root!;
XNamespace ns = "http://www.w3.org/2000/svg";
if (svg.Name != ns + "svg" || (string?)svg.Attribute("viewBox") != "0 0 1254 1254")
    throw new InvalidDataException("Expected the logo's 1254-square SVG viewBox.");
var shapes = svg.Elements().Where(e => e.Name != ns + "title" && e.Name != ns + "desc").ToArray();
string[] attributes = ["x", "y", "width", "height", "rx", "fill"];
foreach (var shape in shapes)
    if (shape.Name != ns + "rect" || shape.Attributes().Any(a => !attributes.Contains(a.Name.ToString())))
        throw new InvalidDataException("The branding exporter supports only filled rectangles with x/y/width/height/rx/fill.");

byte[] Render(int size)
{
    using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(size / 1254f);
    using var paint = new SKPaint { IsAntialias = true };
    foreach (var shape in shapes)
    {
        float Number(string name) => float.Parse((string?)shape.Attribute(name) ?? "0", CultureInfo.InvariantCulture);
        paint.Color = SKColor.Parse((string?)shape.Attribute("fill") ?? (string?)svg.Attribute("fill") ?? "black");
        var bounds = SKRect.Create(Number("x"), Number("y"), Number("width"), Number("height"));
        canvas.DrawRoundRect(bounds, Number("rx"), Number("rx"), paint);
    }
    canvas.Flush();
    using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    return png.ToArray();
}

File.WriteAllBytes(Path.Combine(directory, "goboard-logo.png"), Render(1254));
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var frames = sizes.Select(Render).ToArray();
File.WriteAllBytes(Path.Combine(directory, "goboard-dashboard.png"), frames[^1]);
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
Console.WriteLine("Exported logo PNG, dashboard PNG, and nine Windows ICO frames from goboard-logo.svg.");
