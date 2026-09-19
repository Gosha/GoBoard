using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Presentation.Skia;
using SkiaSharp;
using Xunit;

namespace GoBoard.Tests;

public sealed class EffectsTests
{
    private sealed class Sink : IKeySink
    {
        public int Strokes;
        public void Down(ushort scan) { }
        public void Up(ushort scan) { }
        public void Stroke(ushort scan, ushort[] chord) => Strokes++;
    }
    // Keep isolated renderer scenarios independent of the application's enabled defaults.
    private static EffectSettings Baseline => new()
    {
        Afterglow = false, Spotlight = false, Ripples = false, Transition = CharacterTransition.None,
        EnterMs = 0, LeaveMs = 220, Radius = 80, Strength = 45, RippleMs = 380, TransitionMs = 300, Travel = 8
    };
    private static KeyboardState Keyboard() => new(new Sink());
    private static SKBitmap Render(AnimatedKeyboardRenderer r, KeyboardState k, EffectSettings e, double time,
        bool shift = false, bool altGr = false, bool caps = false, string theme = BoardThemes.SteamSoft)
        => r.Render(k, shift, null, altGr, caps, false, theme, e, time);
    private static void Move(KeyboardState k, string id, uint cursor = 0, double time = 1)
    {
        var b = k.Layout.Keys.Single(k => k.Id == id).Bounds;
        k.Enter(cursor, cursor + 7, time);
        k.Move(cursor, cursor + 7, b.X + b.Width / 2, OverlayGeometry.PanelHeight - b.Y - b.Height / 2);
    }
    private static void EqualArea(SKBitmap a, SKBitmap b, SKRect area)
    {
        const int s = Panel.RasterScale;
        for (var y = (int)Math.Ceiling(area.Top * s); y < (int)(area.Bottom * s); y++)
        for (var x = (int)Math.Ceiling(area.Left * s); x < (int)(area.Right * s); x++)
            Assert.True(a.GetPixel(x, y) == b.GetPixel(x, y), $"Pixel {x},{y}: {a.GetPixel(x, y)} != {b.GetPixel(x, y)}");
    }
    private static void Save(SKBitmap image, string name)
    {
        var directory = Environment.GetEnvironmentVariable("GOBOARD_EFFECTS_ARTIFACTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        data.SaveTo(file);
    }

    [Fact]
    public void PointerOnlyMotionReusesTheCachedKeyboardButStateChangesStillRebuildIt()
    {
        using var r = new AnimatedKeyboardRenderer(); var k = Keyboard();
        var e = Baseline with { Spotlight = true, Afterglow = true };
        using var first = Render(r, k, e, 0);
        var builds = r.BaselineBuildCount;
        foreach (var id in new[] { "a", "s", "d", "f", "j", "k" })
        {
            Move(k, id);
            using var frame = Render(r, k, e, 1);
            Assert.NotNull(frame);
            Assert.Equal(builds, r.BaselineBuildCount);
        }
        var p = k.VisualPointers.Single();
        Assert.True(k.Press(0, 7, p.X, OverlayGeometry.PanelHeight - p.Y, 2, 2));
        using var down = Render(r, k, e, 2);
        Assert.Equal(builds + 1, r.BaselineBuildCount);
        k.Up(0, 7, 2.1);
        using var up = Render(r, k, e, 2.1);
        Assert.Equal(builds + 2, r.BaselineBuildCount);
    }

    [Theory]
    [InlineData(850, 282)]
    [InlineData(1275, 423)]
    public void DesktopOutputUsesRequestedSizeAndColorWithoutChangingLogicalKeyGeometry(int width, int height)
    {
        using var r = new AnimatedKeyboardRenderer(); var k = Keyboard(); var e = Baseline;
        var output = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var actual = r.Render(k, false, null, false, false, false, BoardThemes.SteamSoft, e, 0, output);
        using var source = Panel.Render(k);
        using var expected = new SKBitmap(output);
        using (var canvas = new SKCanvas(expected))
        using (var image = SKImage.FromBitmap(source))
        {
            canvas.Clear(KeyboardTheme.Soft.Background);
            canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
        }
        Assert.Equal(output, actual.Info);
        Assert.Equal(expected.Bytes, actual.Bytes);
        Assert.Null(r.Render(k, false, null, false, false, false, BoardThemes.SteamSoft, e, 1, output));
        var builds = r.BaselineBuildCount;
        using var resized = r.Render(k, false, null, false, false, false, BoardThemes.SteamSoft, e, 2,
            new SKImageInfo(1700, 564, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Assert.Equal(1700, resized.Width);
        Assert.Equal(builds, r.BaselineBuildCount);
        // A caller retaining a frame owns its pixels across subsequent renders.
        Assert.Equal(expected.Bytes, actual.Bytes);
        Save(actual, $"desktop-{width}");
    }

    [Fact]
    public void EffectsPersistAcrossEditorsAndResetWithoutChangingGeneralPreferences()
    {
        var path = Path.Combine(Path.GetTempPath(), "GoBoard.Effects." + Guid.NewGuid() + ".json");
        try
        {
            var desktop = new SettingsStore(path); var vr = new SettingsStore(path);
            Assert.True(desktop.Update(s => s with { SizePercent = 120, Theme = BoardThemes.SteamFlat }));
            Assert.True(vr.Update(s => SettingsControls.Apply(SettingsAction.Lift, s)));
            Assert.True(desktop.Update(s => SettingsControls.Apply(SettingsAction.Spotlight, s)));
            Assert.True(vr.Update(s => SettingsControls.Apply(SettingsAction.TransitionSlower, s)));
            var saved = new SettingsStore(path);
            Assert.Null(saved.Error);
            Assert.Equal(CharacterTransition.Lift, saved.Current.Effects.Transition);
            Assert.Equal(180, saved.Current.Effects.TransitionMs);
            Assert.False(saved.Current.Effects.Spotlight);
            Assert.False(saved.Reload()); // Nested values compare structurally.
            Assert.True(desktop.Update(s => SettingsControls.Apply(SettingsAction.ResetEffects, s)));
            saved.Reload();
            Assert.Equal(new EffectSettings(), saved.Current.Effects);
            Assert.Equal(120, saved.Current.SizePercent);
            Assert.Equal(BoardThemes.SteamFlat, saved.Current.Theme);
            File.WriteAllText(path, "{\"SizePercent\":110,\"Effects\":null}");
            saved.Reload();
            Assert.Equal(110, saved.Current.SizePercent);
            Assert.Equal(new EffectSettings(), saved.Current.Effects);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidEffectValuesAreSafeAndControlsStopAtTheirLimits()
    {
        var settings = new BoardSettings { Effects = new() { Transition = (CharacterTransition)99,
            TransitionMs = -1, EnterMs = int.MaxValue, LeaveMs = -1, RippleMs = -1, Radius = 0, Strength = 999, Travel = -1 } };
        settings = settings.Normalize();
        Assert.Equal(CharacterTransition.None, settings.Effects.Transition);
        Assert.Equal(0, settings.Effects.TransitionMs);
        Assert.Equal(500, settings.Effects.EnterMs);
        Assert.Equal(20, settings.Effects.Radius);
        Assert.False(SettingsControls.Enabled(SettingsAction.StrengthMore, settings));
        Assert.False(SettingsControls.Enabled(SettingsAction.TravelLess, settings));
        Assert.False(SettingsControls.Enabled(SettingsAction.RippleFaster, settings));
        Assert.False(SettingsControls.Enabled(SettingsAction.TransitionFaster, settings));
    }

    [Fact]
    public void EffectsTabHasIndependentHitTargetsAndSwitchingCancelsBothHandsCaptures()
    {
        var p = new SettingsPointerState();
        p.Process(8, 750, 185, 1, 1, down: true); // General: size +.
        var tab = SettingsControls.Tabs.Single(c => c.Action == SettingsAction.EffectsTab).Bounds;
        p.Process(7, tab.X + 10, tab.Y + 10, 1, 1, down: true);
        Assert.Null(p.Process(7, tab.X + 10, tab.Y + 10, 1.1, 1.1, up: true));
        Assert.Equal(SettingsPage.Effects, p.Page);
        Assert.Null(p.Process(8, 750, 185, 1.2, 1.2, up: true));
        foreach (var c in SettingsControls.ForPage(p.Page))
        {
            var b = c.Bounds;
            Assert.Equal(c.Action, SettingsControls.Hit(b.X + b.Width / 2, b.Y + b.Height / 2, p.Page));
        }
        var before = p.Revision;
        p.Process(8, 620, 790, 2, 2, down: true); // Action > 31 still changes redraw signature.
        Assert.True(p.Revision > before);
        Assert.Equal(SettingsAction.ResetEffects, p.Process(8, 620, 790, 2.1, 2.1, up: true));
        using var page = SettingsPanel.Render(new BoardSettings { Effects = new() { Afterglow = true, Spotlight = true, Transition = CharacterTransition.Lift } }, p);
        Save(page, "effects-settings");
    }

    [Theory]
    [InlineData(BoardThemes.SteamSoft)]
    [InlineData(BoardThemes.SteamFlat)]
    public void DisabledEffectsMatchExistingRenderingAndIdleFramesAreSkipped(string theme)
    {
        using var r = new AnimatedKeyboardRenderer(); var k = Keyboard(); var e = Baseline;
        Move(k, "a");
        using var actual = Render(r, k, e, 1, theme: theme);
        using var expected = Panel.Render(k, theme: theme);
        Assert.Equal(expected.Bytes, actual.Bytes);
        Assert.Null(Render(r, k, e, 2, theme: theme));
        var p = k.VisualPointers.Single();
        k.Move(0, 7, p.X + 2, OverlayGeometry.PanelHeight - p.Y);
        Assert.Null(Render(r, k, e, 3, theme: theme));
    }

    [Theory]
    [InlineData(BoardThemes.SteamSoft, 1)]
    [InlineData(BoardThemes.SteamSoft, 2)]
    [InlineData(BoardThemes.SteamFlat, 1)]
    [InlineData(BoardThemes.SteamFlat, 2)]
    public void CharacterTransitionsKeepUnchangedLegendsAndThemeSurfacesStill(string theme, int transition)
    {
        using var r = new AnimatedKeyboardRenderer(); var k = Keyboard();
        k.SetLayout(WindowsLayoutProvider.FromKlid((nint)WindowsLayout.SwedishHandle, "0000041d"), 0);
        var e = Baseline with { Transition = (CharacterTransition)transition, TransitionMs = 300 };
        using var first = Render(r, k, e, 0, theme: theme);
        using var start = Render(r, k, e, 1, shift: true, theme: theme);
        using var middle = Render(r, k, e, 1.15, shift: true, theme: theme);
        using var target = Panel.Render(k, shift: true, theme: theme);
        using var layers = CharacterLayers.Capture(k.Layout, true, false, false, KeyboardTheme.Resolve(theme));
        foreach (var id in new[] { "2", "e" })
        {
            var key = layers.Keys[id]; var area = key.Layers[2].Bounds;
            Assert.NotNull(key.Layers[2].Label);
            area.Offset(key.Key.Bounds.X, key.Key.Bounds.Y);
            EqualArea(first, middle, area);
            EqualArea(target, middle, area);
        }
        var b = k.Layout.Keys.Single(k => k.Id == "a").Bounds;
        EqualArea(target, middle, new(b.X + 3, b.Y + 3, b.X + 7, b.Y + b.Height - 3));
        Assert.NotEqual(first.Bytes, middle.Bytes);
        Save(middle, $"{theme}-{(CharacterTransition)transition}-mid");
        using var settled = Render(r, k, e, 1.31, shift: true, theme: theme);
        Assert.Equal(target.Bytes, settled.Bytes);
        Assert.Null(Render(r, k, e, 2, shift: true, theme: theme));
    }

    [Theory]
    [InlineData(BoardThemes.SteamSoft)]
    [InlineData(BoardThemes.SteamFlat)]
    public void RapidRetargetStartsAtVisibleFrameAndZeroDurationIsImmediate(string theme)
    {
        using var r = new AnimatedKeyboardRenderer(); var k = Keyboard();
        var e = Baseline with { Transition = CharacterTransition.Lift };
        using var first = Render(r, k, e, 0, theme: theme);
        using var start = Render(r, k, e, 1, shift: true, theme: theme);
        using var mid = Render(r, k, e, 1.15, shift: true, theme: theme);
        using var retarget = Render(r, k, e, 1.15, theme: theme);
        Assert.Equal(mid.Bytes, retarget.Bytes);
        using var instant = Render(r, k, e with { TransitionMs = 0 }, 1.2, shift: true, theme: theme);
        using var expected = Panel.Render(k, shift: true, theme: theme);
        Assert.Equal(expected.Bytes, instant.Bytes);
        Assert.Null(Render(r, k, e with { TransitionMs = 0 }, 2, shift: true, theme: theme));
    }

    [Fact]
    public void PointerLeaveFinishesFadingIncludingFromGapsAndHandsRemainIndependent()
    {
        using var r = new AnimatedKeyboardRenderer(); var k = Keyboard();
        var e = Baseline with { Afterglow = true, Spotlight = true, Edges = true };
        using var empty = Render(r, k, e, 0);
        Move(k, "a", 0); Move(k, "l", 1);
        using var both = Render(r, k, e, 1);
        k.Leave(0, 7, 1.1);
        using var leaving = Render(r, k, e, 1.1);
        using var right = Render(r, k, e, 1.4);
        Assert.NotEqual(empty.Bytes, right.Bytes);
        var b = k.Layout.Keys.Single(k => k.Id == "l").Bounds;
        EqualArea(both, right, new(b.X, b.Y, b.X + b.Width, b.Y + b.Height));
        k.Move(1, 8, 1, 1); // No key here, but pointer lighting is still present.
        using var gap = Render(r, k, e, 1.5);
        k.Leave(1, 8, 1.6);
        using var gapLeave = Render(r, k, e, 1.6);
        using var finish = Render(r, k, e, 2);
        Assert.Equal(empty.Bytes, finish.Bytes);
        Assert.Null(Render(r, k, e, 3));
    }

    [Fact]
    public void AltGrCapsAndLayoutSwitchesSettleToTheirActualLegends()
    {
        using var r = new AnimatedKeyboardRenderer(); var k = Keyboard();
        k.SetLayout(WindowsLayoutProvider.FromKlid((nint)WindowsLayout.SwedishHandle, "0000041d"), 0);
        var e = Baseline with { Transition = CharacterTransition.Lift };
        using var first = Render(r, k, e, 0);
        using var alt = Render(r, k, e, 1, altGr: true);
        using var caps = Render(r, k, e, 1.1, caps: true);
        using var settled = Render(r, k, e, 1.5, caps: true);
        using var expected = Panel.Render(k, caps: true);
        Assert.Equal(expected.Bytes, settled.Bytes);
        using var moving = Render(r, k, e, 2, shift: true);
        k.SetLayout(new WindowsLayout((nint)WindowsLayout.UsHandle), 2.1);
        using var changed = Render(r, k, e, 2.1);
        using var us = Panel.Render(k);
        Assert.Equal(us.Bytes, changed.Bytes);
        Assert.Null(Render(r, k, e, 2.2));
    }

    [Fact]
    public void PressFlashUsesTintInsteadOfObscuringTheEffectWithSolidPressedFill()
    {
        using var r = new AnimatedKeyboardRenderer(); var k = Keyboard();
        var e = Baseline with { PressFlash = true };
        using var initial = Render(r, k, e, 0);
        Move(k, "a"); var p = k.VisualPointers.Single();
        Assert.True(k.Press(0, 7, p.X, OverlayGeometry.PanelHeight - p.Y, 1, 1));
        using var down = Render(r, k, e, 1);
        using var held = Render(r, k, e, 2);
        var b = k.Layout.Keys.Single(k => k.Id == "a").Bounds;
        var x = (int)(b.X + 5) * 3; var y = (int)(b.Y + 5) * 3;
        Assert.NotEqual(initial.GetPixel(x, y), held.GetPixel(x, y));
        Assert.NotEqual(KeyboardTheme.Soft.Accent, held.GetPixel(x, y));
        Assert.NotEqual(down.GetPixel(x, y), held.GetPixel(x, y));
        // Editing an effect while a key is held keeps the held feedback.
        using var edited = Render(r, k, e with { RippleMs = 500 }, 2.1);
        Assert.Equal(held.GetPixel(x, y), edited.GetPixel(x, y));
    }

    [Fact]
    public void EffectsDoNotDelayStrokesAndCancellationClearsPulses()
    {
        var sink = new Sink(); var k = new KeyboardState(sink); using var r = new AnimatedKeyboardRenderer();
        var e = Baseline with { Ripples = true, PressFlash = true, RippleMs = 1000 };
        using var first = Render(r, k, e, 0);
        Move(k, "a");
        var p = k.VisualPointers.Single();
        Assert.True(k.Press(0, 7, p.X, OverlayGeometry.PanelHeight - p.Y, 1, 1));
        Assert.Equal(1, sink.Strokes); // Already sent, before the first animated frame.
        k.Up(0, 7, 1.01);
        using var pulse = Render(r, k, e, 1.01);
        using var later = Render(r, k, e, 1.1);
        Assert.NotEqual(pulse.Bytes, later.Bytes);
        k.Cancel(1.2);
        using var cancelled = Render(r, k, e, 1.2);
        Assert.Equal(first.Bytes, cancelled.Bytes);
        Assert.Null(Render(r, k, e, 1.3));
        Assert.Equal(1, sink.Strokes);
    }
}
