using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal static class SettingsPanel
{
    public static SKBitmap Render(BoardSettings settings, SettingsPointerState pointers = null, string error = null, bool desktopMode = false)
    {
        var bitmap = new SKBitmap(SettingsControls.Width * 2, SettingsControls.Height * 2, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(2);
        canvas.Clear(new SKColor(0x0c, 0x15, 0x1e));
        using var paint = new SKPaint { IsAntialias = true };
        using var face = SKTypeface.FromFamilyName("Segoe UI");
        using var heading = new SKFont(face, 38);
        using var label = new SKFont(face, 25);
        using var small = new SKFont(face, 19);
        var text = new SKColor(0xf1, 0xf6, 0xfc);
        var accent = new SKColor(0x66, 0xc0, 0xf4);
        void Text(string value, float x, float y, SKFont font, SKColor color)
        {
            paint.Color = color;
            canvas.DrawText(value, x, y, SKTextAlign.Left, font, paint);
        }
        Text("GoBoard", 64, 72, heading, text);
        var effectsPage = pointers?.Page == SettingsPage.Effects;
        Text(effectsPage ? "Effects · Changes apply immediately" : "Settings · Changes apply immediately", 64, 110, small, accent);
        if (effectsPage)
        {
            Text("Character changes", 64, 306, small, accent);
            for (var i = 0; i < SettingsControls.Parameters.Length; i++)
            {
                var x = 64 + i % 2 * 392; var y = 414 + i / 2 * 84;
                Text(SettingsControls.Parameters[i].Label, x, y - 10, small, accent);
                paint.Color = text;
                canvas.DrawText(SettingsControls.ParameterValue(i, settings.Effects), x + 174, y + 29, SKTextAlign.Center, small, paint);
            }
        }
        else
        {
        Text($"Keyboard size   {settings.SizePercent}%", 64, 186, label, text);
        Text(desktopMode ? "Desktop scale · 50–150%" : $"{OverlayGeometry.PanelWidthInMeters * settings.Scale * 100:F0} cm wide · 50–150%", 64, 216, small, accent);
        Text("Key sounds", 64, 290, label, text);
        Text("Sound preset", 64, 347, small, accent);
        Text("Click a preset to select and preview", 64, 470, small, accent);
        Text($"Volume   {settings.VolumePercent}%", 64, 511, label, text);
        Text("Keyboard arrangement", 64, 578, label, text);
        Text("Auto · ANSI · ISO", 64, 608, small, accent);
        Text("Keyboard theme", 64, 648, small, accent);
        }
        if (error != null)
            Text("Settings unavailable · Last working values kept", 64, 760, small, new SKColor(0xff, 0xb0, 0xa0));
        foreach (var c in SettingsControls.ForPage(pointers?.Page ?? SettingsPage.General))
        {
            var preset = SettingsControls.SoundFor(c.Action);
            var selected = preset == KeySounds.Canonical(settings.Sound) ||
                c.Action == SettingsAction.ToggleSound && settings.SoundEnabled ||
                c.Action == SettingsAction.SteamSoft && BoardThemes.Normalize(settings.Theme) == BoardThemes.SteamSoft ||
                c.Action == SettingsAction.SteamFlat && BoardThemes.Normalize(settings.Theme) == BoardThemes.SteamFlat ||
                c.Action == SettingsAction.GeneralTab && !effectsPage || c.Action == SettingsAction.EffectsTab && effectsPage ||
                c.Action == SettingsAction.Afterglow && settings.Effects.Afterglow ||
                c.Action == SettingsAction.Spotlight && settings.Effects.Spotlight ||
                c.Action == SettingsAction.Edges && settings.Effects.Edges ||
                c.Action == SettingsAction.Ripples && settings.Effects.Ripples ||
                c.Action == SettingsAction.PressFlash && settings.Effects.PressFlash ||
                c.Action == SettingsAction.TransitionNone && settings.Effects.Transition == CharacterTransition.None ||
                c.Action == SettingsAction.Crossfade && settings.Effects.Transition == CharacterTransition.Crossfade ||
                c.Action == SettingsAction.Lift && settings.Effects.Transition == CharacterTransition.Lift;
            var enabled = SettingsControls.Enabled(c.Action, settings);
            var b = c.Bounds;
            var rect = new SKRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height);
            var softTheme = c.Action == SettingsAction.SteamSoft;
            var hovered = enabled && pointers?.Hovered(c.Action) == true;
            if (softTheme)
            {
                // Render the actual theme surface at twice the keyboard's logical key size.
                canvas.Save();
                canvas.Scale(2);
                Panel.DrawKeySurface(canvas, new KeyboardKey("ThemePreview", "", 0,
                    new(b.X / 2, b.Y / 2, b.Width / 2, b.Height / 2)),
                    KeyboardTheme.Soft, hovered, filled: false, armed: selected);
                canvas.Restore();
                if (selected)
                {
                    paint.Color = KeyboardTheme.Soft.Accent;
                    canvas.DrawRoundRect(new SKRect(rect.MidX - 12, rect.Bottom - 9, rect.MidX + 12, rect.Bottom - 6), 1.5f, 1.5f, paint);
                }
            }
            else
            {
                paint.Color = selected ? accent : new SKColor(0x1b, 0x2c, 0x39);
                canvas.DrawRoundRect(rect, 10, 10, paint);
            }
            if (!softTheme && hovered)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 3;
                paint.Color = text;
                canvas.DrawRoundRect(rect, 10, 10, paint);
                paint.Style = SKPaintStyle.Fill;
            }
            paint.Color = !enabled ? new SKColor(0x66, 0x78, 0x82) : selected ? new SKColor(0x09, 0x19, 0x23) : text;
            if (softTheme) paint.Color = selected ? KeyboardTheme.Soft.Accent : KeyboardTheme.Soft.Text;
            var title = c.Action == SettingsAction.ToggleSound ? settings.SoundEnabled ? "On" : "Off" : c.Label;
            if (c.Action == SettingsAction.Geometry) title = settings.Geometry switch
            { KeyboardGeometry.Ansi => "ANSI", KeyboardGeometry.Iso => "ISO", _ => "Auto" };
            var font = preset.HasValue || effectsPage || c.Action is SettingsAction.Defaults or SettingsAction.ResetPosition or SettingsAction.GeneralTab or SettingsAction.EffectsTab ? small : label;
            var baseline = rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
            if (preset.HasValue)
            {
                SoundPresetIcon.Draw(canvas, preset.Value, rect.MidX - SoundPresetIcon.Size / 2, rect.Top + 10);
                var labelY = rect.Top + 68 - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
                canvas.DrawText(title, rect.MidX, labelY, SKTextAlign.Center, font, paint);
            }
            else canvas.DrawText(title, rect.MidX, baseline, SKTextAlign.Center, font, paint);
        }
        return bitmap;
    }

    public static SKBitmap Icon()
    {
        using var stream = typeof(SettingsPanel).Assembly.GetManifestResourceStream("GoBoard.Logo.png");
        using var logo = SKImage.FromEncodedData(stream);
        var bitmap = new SKBitmap(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawImage(logo, new SKRect(0, 0, bitmap.Width, bitmap.Height),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        return bitmap;
    }
}
