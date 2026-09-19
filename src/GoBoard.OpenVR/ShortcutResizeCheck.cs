using System.Security.Cryptography;
using GoBoard.Core;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;
using Valve.VR;

namespace GoBoard.Vr;

// Explicit native check: a hidden, uniquely named overlay, no settings writes
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
                "GoBoard hidden shortcut resize check", ref handle));
            var pose = new HmdMatrix34_t { m0 = 1, m5 = 1, m10 = 1 };
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
            Verify(settings);
            foreach (var theme in new[] { BoardThemes.SteamSoft, BoardThemes.SteamFlat })
            foreach (var grid in grids.Concat(grids.Reverse()))
                Verify(settings with { Theme = theme, ProgrammableKeys = new() { Enabled = true, Columns = grid.Columns, Rows = grid.Rows } });
            Console.WriteLine("SteamVR shortcut resize check passed: 81 updates, all 20 grids growing/shrinking, both themes, texture readback, physical dimensions, pointer scale and UV orientation. No input or user settings changed.");
            return 0;

            void Verify(BoardSettings next)
            {
                keyboard.ApplySettings(next, resized: true);
                var state = keyboard.State;
                var expectedWidth = state.Width * ProgrammableKeys.MetersPerUnit;
                var expectedHeight = state.Height * ProgrammableKeys.MetersPerUnit;
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
                Require(mouse.v0 == state.Width && mouse.v1 == state.Height, "Stale pointer coordinates");
                var bounds = new VRTextureBounds_t();
                Check(overlay.GetOverlayTextureBounds(handle, ref bounds));
                Require(bounds.uMin == 0 && bounds.uMax == 1 && bounds.vMin == 1 && bounds.vMax == 0, "Texture orientation changed");
                var bottom = new HmdMatrix34_t(); var top = new HmdMatrix34_t();
                Check(overlay.GetTransformForOverlayCoordinates(handle, ETrackingUniverseOrigin.TrackingUniverseStanding,
                    new() { v0 = 0, v1 = 0 }, ref bottom));
                Check(overlay.GetTransformForOverlayCoordinates(handle, ETrackingUniverseOrigin.TrackingUniverseStanding,
                    new() { v0 = state.Width, v1 = state.Height }, ref top));
                Require(Math.Abs(top.m3 - bottom.m3 - expectedWidth) < .00001f &&
                    Math.Abs(top.m7 - bottom.m7 - expectedHeight) < .00001f, "Stale physical panel dimensions");
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
