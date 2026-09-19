using System.Security.Cryptography;
using System.Diagnostics;
using System.Numerics;
using GoBoard.Core;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;
using Valve.VR;

namespace GoBoard.Vr;

// Explicit native check: a transparent, uniquely named overlay, no settings writes
// or input events. Kept out of the headset-independent regression test suite.
internal static class ShortcutResizeCheck
{
    public static int Run()
    {
        var error = EVRInitError.None;
        var system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
        if (error != EVRInitError.None) throw new InvalidOperationException($"Initialize resize check: {error}");
        OverlayGraphics graphics = null;
        KeyboardOverlay keyboard = null;
        ulong handle = 0;
        try
        {
            var overlay = OpenVR.Overlay;
            Check(overlay.CreateOverlay("goboard.check.shortcut-resize." + Guid.NewGuid().ToString("N"),
                "GoBoard transparent shortcut resize check", ref handle));
            // Intersection queries require ShowOverlay. Keep it transparent and
            // away from the play space, with no mouse input method enabled.
            Check(overlay.SetOverlayAlpha(handle, 0));
            Check(overlay.ShowOverlay(handle));
            var transform = Matrix4x4.CreateFromYawPitchRoll(.3f, -.2f, .1f) * Matrix4x4.CreateTranslation(0, -100, 0);
            var pose = OpenVrPose.ToOpenVr(transform);
            Check(overlay.SetOverlayTransformAbsolute(handle, ETrackingUniverseOrigin.TrackingUniverseStanding, ref pose));
            graphics = new();
            keyboard = new(system, overlay, handle, graphics, shortcutsOnly: true);
            var grids = Enumerable.Range(1, ProgrammableKeySettings.MaxColumns).Reverse()
                .SelectMany(c => Enumerable.Range(1, ProgrammableKeySettings.MaxRows).Reverse().Select(r => (Columns: c, Rows: r))).ToArray();
            // Include default settings before enablement, every size in both
            // directions, and returns to previously submitted sizes.
            var settings = new BoardSettings { SoundEnabled = false };
            (string Theme, int Columns, int Rows)? lastIdentity = null;
            string lastFingerprint = null;
            var updates = 0;
            Verify(settings);
            foreach (var theme in new[] { BoardThemes.SteamSoft, BoardThemes.SteamFlat })
            foreach (var grid in grids.Concat(grids.Reverse()))
                Verify(settings with { Theme = theme, ProgrammableKeys = new() { Enabled = true, Columns = grid.Columns, Rows = grid.Rows } });
            Console.WriteLine("SteamVR shortcut resize check passed: 81 updates, all 20 grids growing/shrinking, both themes, texture readback, physical dimensions, pointer scale, ray hitboxes and UV orientation at 50/100/150% scale. No input or user settings changed.");
            return 0;

            void Verify(BoardSettings next)
            {
                keyboard.ApplySettings(next, resized: true);
                var state = keyboard.State;
                var meters = ProgrammableKeys.MetersPerUnit * (++updates % 3 + 1) / 2f;
                var expectedWidth = state.Width * meters;
                var expectedHeight = state.Height * meters;
                Check(overlay.SetOverlayWidthInMeters(handle, expectedWidth));
                keyboard.BeginFrame(true);
                keyboard.EndFrame();
                Require(GL.GetError() == ErrorCode.NoError, "OpenGL publication error");
                var texture = KeyboardOverlay.ShortcutTextureInfo;
                uint width = 0, height = 0;
                Check(overlay.GetOverlayTextureSize(handle, ref width, ref height));
                Require(width == texture.Width && height == texture.Height, "Texture dimensions changed");
                var mouse = new HmdVector2_t();
                Check(overlay.GetOverlayMouseScale(handle, ref mouse));
                Require(mouse.v0 == texture.Width && mouse.v1 == texture.Height, "Pointer aspect differs from raster");
                var bounds = new VRTextureBounds_t();
                Check(overlay.GetOverlayTextureBounds(handle, ref bounds));
                Require(bounds.uMin == 0 && bounds.uMax == 1 && bounds.vMin == 1 && bounds.vMax == 0, "Texture orientation changed");
                float physicalWidth = 0, texelAspect = 0;
                Check(overlay.GetOverlayWidthInMeters(handle, ref physicalWidth));
                Check(overlay.GetOverlayTexelAspect(handle, ref texelAspect));
                Require(Math.Abs(physicalWidth - expectedWidth) < .00001f &&
                    Math.Abs(physicalWidth * height / width / texelAspect - expectedHeight) < .00001f,
                    "Stale physical panel dimensions");

                bool Ray(float x, float y, out VROverlayIntersectionResults_t hit)
                {
                    var source = Vector3.Transform(new((x - state.Width / 2f) * meters,
                        (state.Height / 2f - y) * meters, 1), transform);
                    var direction = Vector3.TransformNormal(-Vector3.UnitZ, transform);
                    var ray = new VROverlayIntersectionParams_t
                    {
                        eOrigin = ETrackingUniverseOrigin.TrackingUniverseStanding,
                        vSource = new() { v0 = source.X, v1 = source.Y, v2 = source.Z },
                        vDirection = new() { v0 = direction.X, v1 = direction.Y, v2 = direction.Z }
                    };
                    hit = new();
                    return overlay.ComputeOverlayIntersection(handle, ref ray, ref hit);
                }
                // SteamVR's intersection cache can lag a width/pose change.
                // Wait for an off-center ray, then check every key without retries.
                var ready = Stopwatch.StartNew();
                while (!Ray(state.Width * .25f, state.Height * .25f, out var center) ||
                    Math.Abs(center.vUVs.v0 - .25f) > .0005f || Math.Abs(center.vUVs.v1 - .75f) > .0005f)
                {
                    Require(ready.Elapsed.TotalSeconds < 2, "SteamVR ray coordinates do not match the displayed grid");
                    Thread.Sleep(10);
                }
                foreach (var key in state.Keys)
                {
                    var b = key.Bounds;
                    foreach (var (x, y) in new[] { (b.X + b.Width / 2, b.Y + b.Height / 2),
                        (b.X + 1, b.Y + 1), (b.X + b.Width - 1, b.Y + b.Height - 1) })
                    {
                        Require(Ray(x, y, out var hit), $"Ray missed {key.Id}");
                        var p = KeyboardOverlay.ShortcutPointerPosition(hit.vUVs.v0 * mouse.v0, hit.vUVs.v1 * mouse.v1, state);
                        Require(Math.Abs(p.X - x) < .03f && Math.Abs(p.Y - (state.Height - y)) < .03f &&
                            state.Hit(p.X, p.Y) == key, $"Ray targets the wrong shortcut at {key.Id}");
                    }
                }
                foreach (var (x, y) in new[] { (-2f, state.Height / 2f), (state.Width + 2f, state.Height / 2f),
                    (state.Width / 2f, -2f), (state.Width / 2f, state.Height + 2f) })
                    Require(!Ray(x, y, out _), "Ray hit outside the visible panel");
                using var pixels = new SKBitmap(texture);
                Check(overlay.GetOverlayImageData(handle, pixels.GetPixels(), (uint)pixels.ByteCount, ref width, ref height));
                var fingerprint = Convert.ToHexString(SHA256.HashData(pixels.Bytes));
                var identity = (next.Theme, next.ProgrammableKeys.Columns, next.ProgrammableKeys.Rows);
                if (lastIdentity.HasValue && lastIdentity != identity)
                    Require(lastFingerprint != fingerprint, "Panel image did not update");
                lastIdentity = identity; lastFingerprint = fingerprint;
                if (Environment.GetEnvironmentVariable("GOBOARD_SHORTCUT_ARTIFACTS") is { Length: > 0 } directory &&
                    (identity.Columns, identity.Rows) is (1, 1) or (4, 5))
                {
                    Directory.CreateDirectory(directory);
                    // Undo the fixed-raster stretch for a preview of the actual
                    // physical proportions reported by SteamVR above.
                    using var preview = pixels.Resize(new SKImageInfo(state.Width * 3, state.Height * 3,
                        SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear));
                    using var png = preview.Encode(SKEncodedImageFormat.Png, 100);
                    using var file = File.Create(Path.Combine(directory, $"vr-resize-{identity.Columns}x{identity.Rows}-{next.Theme}.png"));
                    png.SaveTo(file);
                }
            }
        }
        finally
        {
            keyboard?.Dispose();
            if (handle != 0) OpenVR.Overlay.DestroyOverlay(handle);
            OpenVR.Shutdown();
            graphics?.Dispose();
        }
    }

    private static void Check(EVROverlayError error)
    {
        if (error != EVROverlayError.None) throw new InvalidOperationException($"Shortcut resize check: {error}");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
