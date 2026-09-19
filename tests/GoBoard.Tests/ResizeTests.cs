using System.Numerics;
using GoBoard.Core;
using GoBoard.Presentation.Skia;
using SkiaSharp;
using Xunit;

namespace GoBoard.Tests;

public sealed class ResizeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GoBoard.Resize.Tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(directory, "settings.json");
    private static readonly Vector3 Corner = new(OverlayGeometry.PanelWidthInMeters / 2, -OverlayGeometry.PanelHeightInMeters / 2, 0);
    private static Matrix4x4 Hand(float scale = 1) => Matrix4x4.CreateTranslation(OverlayGeometry.ResizePoint(scale, 30, 30) + Vector3.UnitZ);
    private static Matrix4x4 Drag(Matrix4x4 hand, float delta) => hand * Matrix4x4.CreateTranslation(Corner * delta);

    [Theory]
    [InlineData(.5f, 2, 58)]
    [InlineData(1f, 30, 30)]
    [InlineData(1.5f, 58, 2)]
    public void CapturePreservesSizeForEveryClickOffsetAndPanelPose(float scale, float x, float y)
    {
        var panel = Matrix4x4.CreateFromYawPitchRoll(.7f, -.3f, .2f) * Matrix4x4.CreateTranslation(2, 1, -3);
        var hand = Hand(scale) * panel;
        var pose = ResizePose.Capture(panel, hand, OverlayGeometry.ResizePoint(scale, x, y), scale);
        Assert.NotNull(pose);
        Assert.Equal(scale, pose.Update(panel, hand), 5);
        // Moving the entire dashboard/controller frame together must not scale the keyboard.
        var delta = Matrix4x4.CreateRotationY(-.4f) * Matrix4x4.CreateTranslation(-1, 2, .5f);
        Assert.Equal(scale, pose.Update(panel * delta, hand * delta), 5);
    }

    [Fact]
    public void DiagonalMotionScalesAboutCenterInPanelCoordinatesAndDoesNotStickAtLimits()
    {
        var panel = Matrix4x4.CreateFromYawPitchRoll(.4f, -.6f, .3f) * Matrix4x4.CreateTranslation(1, 2, -1);
        var hand = Hand();
        var pose = ResizePose.Capture(panel, hand * panel, OverlayGeometry.ResizePoint(1, 30, 30), 1);
        Assert.Equal(1.25f, pose.Update(panel, Drag(hand, .25f) * panel), 5);
        Assert.Equal(.75f, pose.Update(panel, Drag(hand, -.25f) * panel), 5);
        Assert.Equal(1.5f, pose.Update(panel, Drag(hand, 2) * panel));
        Assert.Equal(1.2f, pose.Update(panel, Drag(hand, .2f) * panel), 5);
        Assert.Equal(.5f, pose.Update(panel, Drag(hand, -2) * panel));
        Assert.Equal(.8f, pose.Update(panel, Drag(hand, -.2f) * panel), 5);
        // Perpendicular motion must not resize; diagonal motion controls one proportional scale.
        var sideways = new Vector3(-Corner.Y, Corner.X, 0) * .4f;
        Assert.Equal(1f, pose.Update(panel, hand * Matrix4x4.CreateTranslation(sideways) * panel), 5);
    }

    [Fact]
    public void InvalidParallelAndBackwardRaysRetainTheLastValidScale()
    {
        var hand = Hand();
        var pose = ResizePose.Capture(Matrix4x4.Identity, hand, OverlayGeometry.ResizePoint(1, 30, 30), 1);
        Assert.Equal(1.2f, pose.Update(Matrix4x4.Identity, Drag(hand, .2f)), 5);
        var parallel = Matrix4x4.CreateRotationY(MathF.PI / 2) * hand;
        Assert.Equal(1.2f, pose.Update(Matrix4x4.Identity, parallel), 5);
        Assert.Equal(1.2f, pose.Update(Matrix4x4.Identity, Matrix4x4.CreateRotationY(MathF.PI) * hand), 5);
        Assert.Equal(1.2f, pose.Update(default, hand), 5);
        var invalid = hand; invalid.M41 = float.NaN;
        Assert.Equal(1.2f, pose.Update(Matrix4x4.Identity, invalid), 5);
        Assert.Null(ResizePose.Capture(default, hand, Vector3.Zero, 1));
        Assert.Null(ResizePose.Capture(Matrix4x4.Identity, default, Vector3.Zero, 1));
        Assert.Null(ResizePose.Capture(Matrix4x4.Identity, hand, Vector3.Zero, float.NaN));
    }

    [Theory]
    [InlineData(.5f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void HandleKeepsSmallCornerSpacingAndTargetExcludesKeys(float scale)
    {
        var offset = OverlayGeometry.ResizeFromScaledPanel(scale);
        var half = OverlayGeometry.ResizeSizeInMeters / 2;
        var right = OverlayGeometry.PanelWidthInMeters * scale / 2;
        var bottom = -OverlayGeometry.PanelHeightInMeters * scale / 2;
        Assert.Equal(.006f, offset.M41 - right, 5);
        Assert.Equal(.006f, bottom - offset.M42, 5);
        Assert.True(offset.M41 - half > OverlayGeometry.GrabWidthInMeters / 2);
        Assert.Equal(offset.Translation, OverlayGeometry.ResizePoint(scale, OverlayGeometry.ResizeSize / 2, OverlayGeometry.ResizeSize / 2));
        foreach (var target in OverlayGeometry.ResizeTargets)
        {
            var left = OverlayGeometry.ResizePoint(scale, target.X, target.Y).X;
            var top = OverlayGeometry.ResizePoint(scale, target.X, target.Y + target.Height).Y;
            Assert.True(left >= right - 1e-6f || top <= bottom + 1e-6f);
        }
        // The opposite corners remain equidistant from the same center.
        var panel = Matrix4x4.CreateRotationX(.3f) * Matrix4x4.CreateTranslation(1, 2, -3);
        var a = Vector3.Transform(Corner * scale, panel);
        var b = Vector3.Transform(-Corner * scale, panel);
        Assert.True(Vector3.Distance((a + b) / 2, panel.Translation) < 1e-5f);
    }

    private static GrabInput Input() => new(OverlayGeometry.ResizeSize, OverlayGeometry.ResizeSize,
        retainCaptureOnLeave: true, hitTest: OverlayGeometry.ResizeHit);

    [Fact]
    public void ResizeCaptureSurvivesLeavingAndOtherHandsCannotStealOrReleaseIt()
    {
        var input = Input();
        input.Enter(7, 7, 1);
        Assert.Null(input.Down(7, 7, 30, 30, 1.1, 1.1));
        var press = input.Active;
        input.Leave(7, 7, 1.2);
        input.ObserveMotion(7, 7, 300, -100, 1.3, 1.3, true);
        Assert.Same(press, input.Active);
        input.Enter(8, 8, 1.3);
        Assert.NotNull(input.Down(8, 8, 30, 30, 1.4, 1.4));
        input.Up(8, 8, 1.5);
        input.Up(7, 8, 1.5);
        input.Leave(8, 8, 1.5);
        input.Up(7, 7, 1.05); // Out-of-order release predates capture.
        Assert.Same(press, input.Active);
        input.Up(7, 7, 1.6);
        Assert.Null(input.Active);
        Assert.NotNull(input.Down(7, 7, 30, 30, 1.15, 1.65));
    }

    [Fact]
    public void QueuedReleaseStalePressUnknownHandAndOutsidePressCannotStartResize()
    {
        var input = Input();
        Assert.NotNull(input.Down(7, 7, 30, 30, 1, 1));
        input.Enter(7, null, 1);
        Assert.NotNull(input.Down(7, null, 30, 30, 1.1, 1.1));
        input.Enter(7, 7, 1.2);
        Assert.NotNull(input.Down(7, 7, 30, 30, 1.3, 1.6));
        Assert.NotNull(input.Down(7, 7, OverlayGeometry.ResizeSize + 1, 30, 1.7, 1.7));
        Assert.NotNull(input.Down(7, 7, float.NaN, 30, 1.7, 1.7));
        Assert.NotNull(input.Down(7, 7, 15, 45, 1.7, 1.7)); // Transparent quadrant over the keys.
        Assert.Null(input.Down(7, 7, 30, 30, 1.8, 1.8));
        input.Up(7, 7, 1.9);
        Assert.Null(input.Active);
        input.Reset(); // Disabled during moving or hidden: no capture/focus is retained.
        Assert.False(input.HasFocus);
        Assert.NotNull(input.Down(7, 7, 30, 30, 2, 2));
        input.ObserveMotion(7, 7, 30, 30, 2.1, 2.1, true);
        Assert.Null(input.Active);
        Assert.Null(input.Down(7, 7, 30, 30, 2.2, 2.2));
    }

    [Theory]
    [InlineData(.124f, 112)]
    [InlineData(.126f, 113)]
    public void PreviewDoesNotWriteAndReleaseSavesRoundedSizeMergingOtherFields(float delta, int expected)
    {
        var store = new SettingsStore(SettingsPath);
        var session = new ResizeSession(store);
        Assert.True(session.Begin(Matrix4x4.Identity, Hand(), 30, 30));
        Assert.False(session.Begin(Matrix4x4.Identity, Hand(), 30, 30));
        session.Update(Matrix4x4.Identity, Drag(Hand(), delta));
        Assert.False(File.Exists(SettingsPath));
        var other = new SettingsStore(SettingsPath);
        Assert.True(other.Update(s => s with { VolumePercent = 30, Theme = BoardThemes.SteamFlat }));
        Assert.True(session.Complete());
        Assert.False(session.Active);
        var saved = new SettingsStore(SettingsPath).Current;
        Assert.Equal(expected, saved.SizePercent);
        Assert.Equal(30, saved.VolumePercent);
        Assert.Equal(BoardThemes.SteamFlat, saved.Theme);
        Assert.Equal(expected / 100f, session.Scale);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExternalSizeWinsWhetherObservedDuringDragOrOnlyAtCommit(bool reload)
    {
        var store = new SettingsStore(SettingsPath);
        var session = new ResizeSession(store);
        session.Begin(Matrix4x4.Identity, Hand(), 30, 30);
        session.Update(Matrix4x4.Identity, Drag(Hand(), .3f));
        var other = new SettingsStore(SettingsPath);
        Assert.True(other.Update(s => s with { SizePercent = 80 }));
        if (reload)
        {
            store.Reload();
            Assert.True(session.Synchronize());
        }
        else Assert.True(session.Complete());
        Assert.False(session.Active);
        Assert.Equal(.8f, session.Scale);
        Assert.Equal(80, new SettingsStore(SettingsPath).Current.SizePercent);
    }

    [Fact]
    public void CancelAndFailedSaveRestoreSavedScaleWithoutWritingPreview()
    {
        var store = new SettingsStore(SettingsPath);
        var session = new ResizeSession(store);
        session.Begin(Matrix4x4.Identity, Hand(), 30, 30);
        session.Update(Matrix4x4.Identity, Drag(Hand(), .3f));
        session.Cancel(); // Dashboard hidden or process shutdown.
        Assert.False(session.Active);
        Assert.Equal(1f, session.Scale);
        Assert.False(File.Exists(SettingsPath));
        session.Begin(Matrix4x4.Identity, Hand(), 30, 30);
        session.Update(Matrix4x4.Identity, Drag(Hand(), .3f));
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, "{broken");
        Assert.False(session.Complete());
        Assert.False(session.Active);
        Assert.Equal(1f, session.Scale);
        Assert.NotNull(store.Error);
        Assert.Equal("{broken", File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData(false, true, true, true, true, (int)ResizeResult.Cancelled)]
    [InlineData(true, false, false, true, true, (int)ResizeResult.Completed)]
    [InlineData(true, false, false, false, true, (int)ResizeResult.Active)]
    [InlineData(true, true, true, false, false, (int)ResizeResult.Cancelled)]
    [InlineData(true, true, false, false, true, (int)ResizeResult.Completed)]
    [InlineData(true, true, true, true, false, (int)ResizeResult.Completed)]
    [InlineData(true, true, true, false, true, (int)ResizeResult.Active)]
    public void TrackingAndTriggerPollingDistinguishCancellationFromRelease(bool tracked, bool triggerAvailable,
        bool held, bool released, bool ownsCapture, int expectedValue)
    {
        var expected = (ResizeResult)expectedValue;
        var store = new SettingsStore(SettingsPath);
        var session = new ResizeSession(store);
        session.Begin(Matrix4x4.Identity, Hand(), 30, 30);
        session.Update(Matrix4x4.Identity, Drag(Hand(), .2f));
        var result = session.Track(Matrix4x4.Identity, tracked ? Drag(Hand(), .2f) : null,
            triggerAvailable, held, released, ownsCapture);
        Assert.Equal(expected, result);
        Assert.Equal(expected == ResizeResult.Active, session.Active);
        Assert.Equal(expected == ResizeResult.Completed, File.Exists(SettingsPath));
        Assert.Equal(expected == ResizeResult.Cancelled ? 1f : 1.2f, session.Scale, 5);
    }

    private sealed class Sink : IKeySink
    {
        public int Strokes;
        public void Down(ushort scan) { }
        public void Up(ushort scan) { }
        public void Stroke(ushort scan, ushort[] chord) => Strokes++;
    }

    [Fact]
    public void DashboardPointerReleaseWorksWithoutLegacyPollingButLosingEstablishedPollingCancels()
    {
        var store = new SettingsStore(SettingsPath);
        var session = new ResizeSession(store);
        Assert.False(session.Begin(Matrix4x4.Identity, Hand(), 30, 30, triggerAvailable: true, triggerHeld: false));
        Assert.True(session.Begin(Matrix4x4.Identity, Hand(), 30, 30, triggerAvailable: false, triggerHeld: false));
        Assert.Equal(ResizeResult.Active, session.Track(Matrix4x4.Identity, Drag(Hand(), .2f), false, false, false, true));
        Assert.Equal(ResizeResult.Completed, session.Track(Matrix4x4.Identity, Drag(Hand(), .2f), false, false, true, false));
        Assert.Equal(120, store.Current.SizePercent);
        Assert.True(session.Begin(Matrix4x4.Identity, Hand(1.2f), 30, 30, triggerAvailable: true, triggerHeld: true));
        Assert.Equal(ResizeResult.Cancelled, session.Track(Matrix4x4.Identity, Drag(Hand(1.2f), .2f), false, false, false, true));
        Assert.Equal(1.2f, session.Scale);
    }

    [Fact]
    public void ResizingCancelsBothHandsAndRepeatAndRejectsQueuedTypingOnResume()
    {
        var sink = new Sink();
        var keyboard = new KeyboardState(sink);
        var a = keyboard.Layout.Keys.Single(k => k.Id == "a").Bounds;
        var x = a.X + a.Width / 2;
        var y = OverlayGeometry.PanelHeight - a.Y - a.Height / 2;
        foreach (uint hand in new uint[] { 7, 8 })
        {
            keyboard.Enter(hand, hand, 1);
            Assert.True(keyboard.Press(hand, hand, x, y, 1.1, 1.1));
        }
        Assert.True(keyboard.HasHeldKeys);
        keyboard.SetResizing(true, 1.2);
        Assert.False(keyboard.HasHeldKeys);
        var strokes = sink.Strokes;
        keyboard.Tick(3);
        Assert.Equal(strokes, sink.Strokes);
        foreach (uint hand in new uint[] { 7, 8, 9 })
        {
            keyboard.ObserveMotion(hand, hand, x, y, 3.1, 3.1, true);
            Assert.False(keyboard.Press(hand, hand, x, y, 3.2, 3.2));
        }
        keyboard.SetResizing(false, 3.3);
        foreach (uint hand in new uint[] { 7, 8, 9 })
        {
            keyboard.ObserveMotion(hand, hand, x, y, 3.25, 3.35, true);
            Assert.False(keyboard.Press(hand, hand, x, y, 3.26, 3.35));
        }
        keyboard.ObserveMotion(7, 7, x, y, 3.4, 3.4, true);
        Assert.True(keyboard.Press(7, 7, x, y, 3.5, 3.5));
        Assert.Equal(strokes + 1, sink.Strokes);
    }

    [Fact]
    public void MoveHandleStillUsesItsOriginalLeaveToCancelRules()
    {
        GrabInput.Verify();
        GrabPose.Verify();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BothVisibleGripArmsAndTheirPaddingAreInsideNativeAndMouseTargets(int state)
    {
        using var bitmap = ResizeHandleRenderer.Render(state);
        var size = OverlayGeometry.ResizeSize;
        bool NativeHit(float x, float y) => OverlayGeometry.ResizeMaskTargets.Any(r => r.Contains(x, y));
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, y).Alpha == 0) continue;
            var tx = (x + .5f) / OverlayGeometry.RasterScale;
            var ty = (y + .5f) / OverlayGeometry.RasterScale;
            Assert.True(NativeHit(tx, ty), $"Visible grip pixel {tx},{ty} excluded from native mask.");
            Assert.True(OverlayGeometry.ResizeHit(tx, size - ty));
        }
        // Transparent room above/right of the vertical arm, below/left of the
        // horizontal arm, and diagonally outside the corner must all grab.
        foreach (var (x, y) in new[] { (43f, 5f), (65f, 20f), (5f, 43f), (20f, 65f), (65f, 65f) })
        {
            Assert.True(NativeHit(x, y));
            var input = Input();
            input.Enter(7, 7, 1);
            Assert.Null(input.Down(7, 7, x, size - y, 1.1, 1.1));
        }
        Assert.False(NativeHit(15, 15)); // Transparent quadrant over the corner keys.
        Assert.False(OverlayGeometry.ResizeHit(15, size - 15));
    }

    [Fact]
    public void RenderGripStatesForVisualReview()
    {
        var path = Environment.GetEnvironmentVariable("GOBOARD_RESIZE_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        using var preview = new SKBitmap(720, 240);
        using var canvas = new SKCanvas(preview);
        canvas.Clear(new SKColor(31, 40, 49));
        for (var state = 0; state < 3; state++)
        {
            using var bitmap = ResizeHandleRenderer.Render(state);
            using var image = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(image, 30 + state * 240, 30, new SKSamplingOptions(SKFilterMode.Nearest));
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(path, $"resize-{state}.png"));
            data.SaveTo(file);
        }
        using var combined = SKImage.FromBitmap(preview);
        using var png = combined.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(Path.Combine(path, "resize-states.png"));
        png.SaveTo(output);

        using var grabStates = new SKBitmap(720, 100);
        using var grabCanvas = new SKCanvas(grabStates);
        grabCanvas.Clear(new SKColor(31, 40, 49));
        for (var state = 0; state < 3; state++)
        {
            using var bitmap = GrabHandleRenderer.Render(state);
            using var image = SKImage.FromBitmap(bitmap);
            grabCanvas.DrawImage(image, new SKRect(state * 240 + 30, 20, state * 240 + 210, 80),
                new SKSamplingOptions(SKFilterMode.Linear));
        }
        using var grabStateImage = SKImage.FromBitmap(grabStates);
        using var grabStatePng = grabStateImage.Encode(SKEncodedImageFormat.Png, 100);
        using var grabStateFile = File.Create(Path.Combine(path, "grab-states.png"));
        grabStatePng.SaveTo(grabStateFile);

        // Compose the actual three overlays at their physical proportions for layout review.
        using var panelBitmap = Panel.Render();
        using var panelImage = SKImage.FromBitmap(panelBitmap);
        using var grabBitmap = GrabHandleRenderer.Render(0);
        using var grabImage = SKImage.FromBitmap(grabBitmap);
        using var gripBitmap = ResizeHandleRenderer.Render(0);
        using var gripImage = SKImage.FromBitmap(gripBitmap);
        const float pixelsPerMeter = 1000;
        foreach (var scale in new[] { .5f, 1f, 1.5f })
        {
            var width = OverlayGeometry.PanelWidthInMeters * scale * pixelsPerMeter;
            var height = OverlayGeometry.PanelHeightInMeters * scale * pixelsPerMeter;
            using var layout = new SKBitmap((int)Math.Ceiling(width) + 70, (int)Math.Ceiling(height) + 115);
            using var target = new SKCanvas(layout);
            target.Clear(new SKColor(31, 40, 49));
            target.DrawImage(panelImage, new SKRect(20, 20, 20 + width, 20 + height), new SKSamplingOptions(SKFilterMode.Linear));
            void DrawHandle(SKImage image, Matrix4x4 offset, float handleWidth, float handleHeight)
            {
                var cx = 20 + width / 2 + offset.M41 * pixelsPerMeter;
                var cy = 20 + height / 2 - offset.M42 * pixelsPerMeter;
                target.DrawImage(image, new SKRect(cx - handleWidth / 2, cy - handleHeight / 2,
                    cx + handleWidth / 2, cy + handleHeight / 2), new SKSamplingOptions(SKFilterMode.Linear));
            }
            DrawHandle(grabImage, OverlayGeometry.GrabFromScaledPanel(scale), 180, 60);
            DrawHandle(gripImage, OverlayGeometry.ResizeFromScaledPanel(scale),
                OverlayGeometry.ResizeSizeInMeters * pixelsPerMeter, OverlayGeometry.ResizeSizeInMeters * pixelsPerMeter);
            using var result = SKImage.FromBitmap(layout);
            using var encoded = result.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(path, $"keyboard-resize-{scale * 100:0}.png"));
            encoded.SaveTo(file);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
