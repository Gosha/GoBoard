using System.Numerics;
using GoBoard.Core;
using GoBoard.Presentation.Skia;
using GoBoard.Vr;
using SkiaSharp;
using Xunit;

namespace GoBoard.Tests;

public sealed class NumpadTests
{
    private sealed class Sink : IKeySink
    {
        public readonly List<(ushort Scan, ushort[] Chord)> Strokes = new();
        public void Down(ushort scan) { }
        public void Up(ushort scan) { }
        public void Stroke(ushort scan, ushort[] chord) => Strokes.Add((scan, chord));
    }
    private static bool Press(KeyboardState state, string id, double time, uint cursor = 0)
    {
        var b = state.Keys.Single(k => k.Id == id).Bounds;
        state.Enter(cursor, cursor + 7, time);
        return state.Press(cursor, cursor + 7, b.X + b.Width / 2, state.Height - b.Y - b.Height / 2, time, time);
    }

    [Theory]
    [InlineData(WindowsLayout.UsHandle)]
    [InlineData(WindowsLayout.SwedishHandle)]
    [InlineData(0x04110411u)]
    public void NumpadPreservesMainKeysAndTargetsEveryKeyInDesktopAndVr(uint hkl)
    {
        var state = new KeyboardState(new Sink());
        state.SetLayout(new WindowsLayout((nint)hkl), 0);
        var original = state.Keys.ToArray();
        state.SetNumpad(true, 1);
        Assert.Equal(original.Length + 17, state.Keys.Count);
        Assert.Equal(original, state.Keys.Take(original.Length));
        Assert.Equal(1046, state.Width);
        Assert.Equal(state.Width - 4, state.Keys.Max(k => k.Bounds.X + k.Bounds.Width));
        Assert.Equal(state.Height - 4, state.Keys.Max(k => k.Bounds.Y + k.Bounds.Height));
        foreach (var enabled in new[] { true, false, true })
        {
            state.SetNumpad(enabled, 2);
            foreach (var dpi in new[] { 1f, 1.5f, 2f })
            {
                var desktop = DesktopGeometry.Create(1.5f, dpi, 900, 550, state.Width);
                Assert.True(desktop.Width <= 900.001f && desktop.Height <= 550.001f);
                foreach (var key in state.Keys)
                {
                    var b = key.Bounds;
                    foreach (var (x, y) in new[] { (b.X + 1, b.Y + 1), (b.X + b.Width - 1, b.Y + b.Height - 1) })
                    {
                        Assert.Single(state.Keys, k => k.Contains(x, y));
                        var vr = KeyboardOverlay.MainPointerPosition(KeyboardOverlay.MainContentBounds(state).Left + x * Panel.RasterScale,
                            (1 - y / state.Height) * KeyboardOverlay.MainTextureInfo.Height, state);
                        Assert.Same(key, state.Hit(vr.X, vr.Y));
                        var point = desktop.ToKeyboard(x / state.Width * desktop.Width,
                            desktop.HeaderHeight + y / state.Height * (desktop.Height - desktop.HeaderHeight));
                        Assert.Same(key, state.Hit(point.X, point.Y));
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(.5f)] [InlineData(1f)] [InlineData(1.5f)]
    public void ToggleKeepsMainKeysHandleAndResetAtTheSameWorldPositions(float scale)
    {
        var state = new KeyboardState(new Sink());
        var original = state.Keys.ToArray();
        var parent = Matrix4x4.CreateFromYawPitchRoll(.4f, -.3f, .2f) * Matrix4x4.CreateTranslation(1, 2, -3);
        var placement = new RelativePose();
        var initial = placement.Update(parent).World;
        var moved = Matrix4x4.CreateRotationX(.2f) * Matrix4x4.CreateTranslation(.3f, -.1f, .2f) * initial;
        placement.SetWorld(parent, moved);
        foreach (var resetPosition in new[] { false, true })
        {
            if (resetPosition) placement.Reset();
            var anchor = placement.Update(parent).World;
            Assert.True(RelativePose.Near(resetPosition ? initial : moved, anchor));
            var texture = OverlayGeometry.TextureFromPanel(scale) * anchor;
            var recovered = OverlayGeometry.PanelFromTexture(scale) * texture;
            Assert.True(RelativePose.Near(anchor, recovered));
            var handle = OverlayGeometry.GrabFromScaledPanel(scale) * anchor;
            var reset = MainKeyboardControls.Offset(KeyboardAction.ResetPosition, scale) * anchor;
            foreach (var enabled in new[] { false, true, false, true })
            {
                state.SetNumpad(enabled, 1);
                var bounds = KeyboardOverlay.MainContentBounds(state);
                Assert.Equal(0, bounds.Left);
                Assert.True(RelativePose.Near(handle, OverlayGeometry.GrabFromScaledPanel(scale) * recovered));
                Assert.True(RelativePose.Near(reset, MainKeyboardControls.Offset(KeyboardAction.ResetPosition, scale) * recovered));
                foreach (var key in original)
                {
                    var b = key.Bounds;
                    var x = b.X + b.Width / 2;
                    var y = b.Y + b.Height / 2;
                    var pixel = new Vector3(
                        ((bounds.Left + x * Panel.RasterScale) / KeyboardOverlay.MainTextureInfo.Width - .5f) *
                            OverlayGeometry.WidthInMeters(true) * scale,
                        (.5f - y / state.Height) * OverlayGeometry.PanelHeightInMeters * scale, 0);
                    var expected = new Vector3((x - OverlayGeometry.PanelWidth / 2f) * ProgrammableKeys.MetersPerUnit * scale,
                        (state.Height / 2f - y) * ProgrammableKeys.MetersPerUnit * scale, 0);
                    Assert.True(Vector3.Distance(Vector3.Transform(expected, anchor), Vector3.Transform(pixel, texture)) < .00001f);
                }
            }
            var expansion = KeyboardLayout.NumpadExtraWidth * ProgrammableKeys.MetersPerUnit * scale;
            Assert.Equal(expansion, OverlayGeometry.ResizeFromScaledPanel(scale, true).M41 -
                OverlayGeometry.ResizeFromScaledPanel(scale, false).M41, 5);
        }
    }
    [Fact]
    public void KeypadChordsRepeatAndToggleCancelsBothPointersAndQueuedPresses()
    {
        var sink = new Sink();
        var state = new KeyboardState(sink);
        state.SetNumpad(true, 0);
        Assert.True(Press(state, "Ctrl", 1)); state.Up(0, 7, 1.1);
        Assert.True(Press(state, "Num1", 2));
        Assert.Equal((ushort)0x4f, sink.Strokes[0].Scan);
        Assert.Equal(new ushort[] { 0x1d }, sink.Strokes[0].Chord);
        Assert.Equal(ModifierMode.Idle, state.Mode(0x1d));
        Assert.True(Press(state, "NumDivide", 2.1, 1));
        Assert.Equal(ProgrammableKeys.NumDivide, sink.Strokes[1].Scan);
        Assert.Empty(sink.Strokes[1].Chord);
        state.Tick(2.5);
        Assert.Equal(sink.Strokes[0], sink.Strokes[2]);
        state.SetNumpad(false, 3);
        var count = sink.Strokes.Count;
        state.Tick(5);
        Assert.Equal(count, sink.Strokes.Count);
        Assert.False(state.HasHeldKeys);
        Assert.All(state.VisualPointers, p => { Assert.False(p.Focused); Assert.Null(p.Held); });
        Assert.False(state.Up(0, 7, 3.1));
        Assert.False(Press(state, "a", 2.9));
        state.SetNumpad(true, 4);
        foreach (var (id, scan) in new[] { ("NumLock", ProgrammableKeys.NumLock), ("NumEnter", ProgrammableKeys.NumEnter) })
        {
            Assert.True(Press(state, id, 6));
            count = sink.Strokes.Count;
            state.Tick(7);
            Assert.Equal(count, sink.Strokes.Count);
            Assert.Equal(scan, sink.Strokes[^1].Scan);
            state.Cancel(5);
        }
    }

    [Theory]
    [InlineData(BoardThemes.SteamSoft, true)]
    [InlineData(BoardThemes.SteamSoft, false)]
    [InlineData(BoardThemes.SteamFlat, true)]
    [InlineData(BoardThemes.SteamFlat, false)]
    public void ToggleAndNumLockRepaintWithStableVrTextureAndNoIdleFrames(string theme, bool vr)
    {
        var state = new KeyboardState(new Sink());
        using var renderer = new AnimatedKeyboardRenderer();
        double time = 0;
        foreach (var enabled in new[] { false, true, false, true })
        foreach (var numLock in new[] { false, true })
        {
            state.SetNumpad(enabled, ++time);
            state.SetNumLock(numLock);
            var output = vr ? KeyboardOverlay.MainTextureInfo : new SKImageInfo(state.Width, state.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            SKRect? bounds = vr ? KeyboardOverlay.MainContentBounds(state) : null;
            using var actual = renderer.Render(state, false, null, false, false, false, theme, new(), time, output, bounds);
            using var fresh = new AnimatedKeyboardRenderer();
            using var expected = fresh.Render(state, false, null, false, false, false, theme, new(), time, output, bounds);
            Assert.NotNull(actual);
            Assert.Equal(expected.Bytes, actual.Bytes);
            if (Environment.GetEnvironmentVariable("GOBOARD_NUMPAD_ARTIFACTS") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var png = actual.Encode(SKEncodedImageFormat.Png, 100);
                using var file = File.Create(Path.Combine(directory, $"{(vr ? "vr" : "desktop")}-{theme}-numpad-{enabled}-numlock-{(numLock ? "on" : "off")}.png"));
                png.SaveTo(file);
            }
            Assert.Null(renderer.Render(state, false, null, false, false, false, theme, new(), time + .1, output, bounds));
        }
    }

    [Theory]
    [InlineData(BoardThemes.SteamSoft)]
    [InlineData(BoardThemes.SteamFlat)]
    public void VrToggleKeepsNativePixelsAndEffectsFixedWithoutStretching(string theme)
    {
        var state = new KeyboardState(new Sink());
        using var paddedRenderer = new AnimatedKeyboardRenderer();
        using var nativeRenderer = new AnimatedKeyboardRenderer();
        double time = 0;
        foreach (var enabled in new[] { false, true, false, true })
        {
            state.SetNumpad(enabled, ++time);
            var bounds = KeyboardOverlay.MainContentBounds(state);
            var key = state.Keys.Single(k => k.Id == "a").Bounds;
            time += .01; // Fresh input after the geometry-change watermark.
            state.Enter(0, 7, time);
            state.Move(0, 7, key.X + key.Width / 2, state.Height - key.Y - key.Height / 2);
            foreach (var elapsed in new[] { 0, .04, .08 })
            {
                using var padded = paddedRenderer.Render(state, false, null, false, false, false, theme, new(), time + elapsed,
                    KeyboardOverlay.MainTextureInfo, bounds);
                using var native = nativeRenderer.Render(state, false, null, false, false, false, theme, new(), time + elapsed);
                Assert.NotNull(padded);
                Assert.NotNull(native);
                Assert.Equal(native.Width, bounds.Width);
                Assert.Equal(native.Height, bounds.Height);
                var pixels = padded.Bytes;
                var reference = native.Bytes;
                var marginBytes = (int)bounds.Left * 4;
                for (var y = 0; y < padded.Height; y++)
                {
                    var row = pixels.AsSpan(y * padded.RowBytes, padded.RowBytes);
                    var content = row.Slice(marginBytes, native.RowBytes);
                    var expected = reference.AsSpan(y * native.RowBytes, native.RowBytes);
                    // Translated gradient shaders and alpha conversion can round
                    // channels slightly differently; a stretched key cannot pass.
                    var largestDifference = 0;
                    for (var x = 0; x < content.Length; x++)
                        largestDifference = Math.Max(largestDifference, Math.Abs(content[x] - expected[x]));
                    Assert.True(largestDifference <= 2,
                        $"Pixel difference {largestDifference} at row {y}, numpad {enabled}, animation {elapsed}");
                    Assert.True(row[..marginBytes].IndexOfAnyExcept((byte)0) < 0);
                    Assert.True(row[(marginBytes + native.RowBytes)..].IndexOfAnyExcept((byte)0) < 0);
                }
            }
            if (!enabled)
                foreach (var x in new[] { bounds.Left - 1, bounds.Right + 1 })
                {
                    var pointer = KeyboardOverlay.MainPointerPosition(x, 100, state);
                    Assert.Null(state.Hit(pointer.X, pointer.Y));
                }
        }
    }

    [Fact]
    public void SettingMergesAndResizeUsesWiderCornerThenCancelsOnToggle()
    {
        var path = Path.Combine(Path.GetTempPath(), "GoBoard.Numpad." + Guid.NewGuid() + ".json");
        try
        {
            var store = new SettingsStore(path);
            var other = new SettingsStore(path);
            Assert.False(store.Current.NumpadEnabled);
            Assert.True(other.Update(s => s with { Theme = BoardThemes.SteamFlat }));
            Assert.True(store.Update(s => SettingsControls.Apply(SettingsAction.ToggleNumpad, s)));
            Assert.True(new SettingsStore(path).Current.NumpadEnabled);
            Assert.Equal(BoardThemes.SteamFlat, store.Current.Theme);
            var resize = new ResizeSession(store);
            var point = OverlayGeometry.ResizePoint(1, 35, 35, true);
            var hand = Matrix4x4.CreateTranslation(point + Vector3.UnitZ);
            Assert.True(resize.Begin(Matrix4x4.Identity, hand, 35, 35));
            var corner = new Vector3(OverlayGeometry.RightEdgeInMeters(true), -OverlayGeometry.PanelHeightInMeters / 2, 0);
            Assert.Equal(.006f, point.X - corner.X, 5);
            resize.Update(Matrix4x4.Identity, hand * Matrix4x4.CreateTranslation(corner * .2f));
            Assert.Equal(1.2f, resize.Scale, 5);
            Assert.True(store.Update(s => SettingsControls.Apply(SettingsAction.ToggleNumpad, s)));
            Assert.True(resize.Synchronize());
            Assert.False(resize.Active);
            Assert.Equal(100, store.Current.SizePercent);
            Assert.False(SettingsControls.Apply(SettingsAction.Defaults, store.Current with { NumpadEnabled = true }).NumpadEnabled);
        }
        finally { File.Delete(path); }
    }
}
