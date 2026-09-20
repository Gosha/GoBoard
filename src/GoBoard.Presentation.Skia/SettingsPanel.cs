using System.Reflection;
using GoBoard.Core;
using SkiaSharp;

namespace GoBoard.Presentation.Skia;

internal static class SettingsPanel
{
    // Packaging embeds the readable release version; assembly/file versions use MSI's numeric mapping.
    private static readonly string VersionLabel = "v" + (typeof(SettingsPanel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown");

    public static SKBitmap Render(BoardSettings settings, SettingsPointerState pointers = null, string error = null, bool desktopMode = false, AutostartState autostart = null)
    {
        autostart ??= AutostartState.Preview;
        var bitmap = new SKBitmap(SettingsControls.Width * 2, SettingsControls.Height * 2, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(2);
        canvas.Clear(new SKColor(0x0c, 0x15, 0x1e));
        using var paint = new SKPaint { IsAntialias = true };
        using var face = SKTypeface.FromFamilyName("Segoe UI");
        using var heading = new SKFont(face, 38);
        using var label = new SKFont(face, 25);
        using var small = new SKFont(face, 19);
        using var version = new SKFont(face, 16);
        var text = new SKColor(0xf1, 0xf6, 0xfc);
        var accent = new SKColor(0x66, 0xc0, 0xf4);
        void Text(string value, float x, float y, SKFont font, SKColor color)
        {
            paint.Color = color;
            canvas.DrawText(value, x, y, SKTextAlign.Left, font, paint);
        }
        Text("GoBoard", 64, 72, heading, text);
        Text(VersionLabel, 64, 904, version, new SKColor(0x81, 0x90, 0x9e));
        var effectsPage = pointers?.Page == SettingsPage.Effects;
        var shortcutsPage = pointers?.Page is SettingsPage.Shortcuts or SettingsPage.ShortcutKey or SettingsPage.ShortcutPreset;
        var choosingPreset = pointers?.Page == SettingsPage.ShortcutPreset;
        var choosingKey = pointers?.Page == SettingsPage.ShortcutKey;
        var slot = pointers?.ShortcutSlot ?? 0;
        var shortcut = settings.ProgrammableKeys.Get(slot);
        var keyNumber = settings.ProgrammableKeys.NumberFor(slot);
        var layout = pointers?.Layout ?? new WindowsLayout((nint)WindowsLayout.UsHandle);
        Text(shortcutsPage ? "Shortcuts" : effectsPage ? "Effects" : "Settings", 64, 110, small, accent);
        if (choosingPreset)
        {
            Text($"Choose a preset · Key {keyNumber}", 64, 151, label, text);
            var name = shortcut.LabelFor(layout);
            var chord = shortcut.ChordFor(layout);
            var current = name == chord ? $"Current: {chord}" : $"Current: {name} · {chord}";
            using var currentFont = new SKFont(face, Math.Min(19, 19 * 740 / Math.Max(1, small.MeasureText(current))));
            Text(current, 64, 184, currentFont, accent);
            foreach (var category in Enum.GetValues<ProgrammableKeys.PresetCategory>())
                Text(category.ToString(), 64 + (int)category * 189, 228, small, accent);
            if (error == null) Text(layout.Notice ?? "Select a preset to assign it and return.", 64, 806, small, accent);
        }
        else if (choosingKey)
        {
            Text($"Key {keyNumber} · {layout.Name}", 64, 151, label, text);
            Text(shortcut.Shift ? "Choose a key · Shift labels" : "Choose a key", 64, 180, small, accent);
            Text("Numpad", 64, 480, small, accent);
            Text("Media & volume", 270, 480, small, accent);
            Text("Browser", 270, 616, small, accent);
            using var hint = new SKFont(face, 14);
            Text("Numpad follows Num Lock.", 64, 740, hint, accent);
            Text("Choose modifiers on the Shortcuts page.", 310, 795, hint, accent);
            if (error == null) Text(layout.Notice ?? "Keys and labels follow the active Windows layout.", 64, 834, hint, accent);
        }
        else if (shortcutsPage)
        {
            Text("Floating button", 64, 183, label, text);
            paint.Color = new SKColor(0x1b, 0x2c, 0x39);
            canvas.DrawRect(423, 232, 1, 458, paint);
            Text("Keys", 64, 248, label, text);
            Text("Columns", 64, 295, small, text);
            Text("Rows", 64, 351, small, text);
            paint.Color = text;
            canvas.DrawText(settings.ProgrammableKeys.Columns.ToString(), 310, 295, SKTextAlign.Center, small, paint);
            canvas.DrawText(settings.ProgrammableKeys.Rows.ToString(), 310, 351, SKTextAlign.Center, small, paint);
            Text($"Edit Key {keyNumber}", 454, 248, label, text);
            var chord = shortcut.ChordFor(layout);
            using var chordFont = new SKFont(face, Math.Min(19, 19 * 350 / Math.Max(1, small.MeasureText(chord))));
            Text(chord, 454, 282, chordFont, accent);
            Text("Preset", 454, 332, small, accent);
            Text("Modifiers", 454, 444, small, accent);
            Text("Main key", 454, 616, small, accent);
            Text("Click the floating button to expand or collapse shortcuts.", 64, 734, small, accent);
        }
        else if (effectsPage)
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
        Text(desktopMode ? "Desktop scale · 50–150%" : $"{OverlayGeometry.WidthInMeters(settings.NumpadEnabled) * settings.Scale * 100:F0} cm wide · 50–150%", 64, 216, small, accent);
        Text("Key sounds", 64, 290, label, text);
        Text("Sound preset", 64, 347, small, accent);
        Text("Click a preset to select and preview", 64, 470, small, accent);
        Text($"Volume   {settings.VolumePercent}%", 64, 511, label, text);
        Text("Arrangement", 64, 578, label, text);
        Text("Auto · ANSI · ISO", 64, 608, small, accent);
        Text("Keyboard theme", 64, 648, small, accent);
        paint.Color = new SKColor(0x1b, 0x2c, 0x39);
        canvas.DrawLine(64, 836, 804, 836, paint);
        Text("Start with SteamVR", 64, 783, label, text);
        Text(autostart.Status, 64, 815, small, autostart.Error == null ? accent : new SKColor(0xff, 0xb0, 0xa0));
        }
        if (error != null)
            Text("Settings unavailable · Last working values kept", 64, choosingPreset ? 806 : choosingKey ? 834 : effectsPage || shortcutsPage ? 760 : 860, small, new SKColor(0xff, 0xb0, 0xa0));
        foreach (var c in SettingsControls.ForPage(pointers?.Page ?? SettingsPage.General, settings, layout, slot))
        {
            var preset = SettingsControls.SoundFor(c.Action);
            var selected = preset == KeySounds.Canonical(settings.Sound) ||
                c.Action == SettingsAction.ToggleSound && settings.SoundEnabled ||
                c.Action == SettingsAction.ToggleNumpad && settings.NumpadEnabled ||
                c.Action == SettingsAction.SteamSoft && BoardThemes.Normalize(settings.Theme) == BoardThemes.SteamSoft ||
                c.Action == SettingsAction.SteamFlat && BoardThemes.Normalize(settings.Theme) == BoardThemes.SteamFlat ||
                c.Action == SettingsAction.GeneralTab && !effectsPage && !shortcutsPage || c.Action == SettingsAction.EffectsTab && effectsPage ||
                c.Action == SettingsAction.ShortcutsTab && shortcutsPage ||
                c.Action == SettingsAction.ToggleShortcuts && settings.ProgrammableKeys.Enabled ||
                c.Action == SettingsControls.SlotAction(slot) ||
                c.Action == SettingsAction.KeyChoiceFirst + shortcut.Scan ||
                SettingsControls.PresetIndex(c.Action) is var presetIndex && presetIndex >= 0 &&
                    ProgrammableKeys.Presets[presetIndex].ForLayout(layout) == shortcut ||
                c.Action == SettingsAction.ShortcutCtrl && shortcut.Ctrl ||
                c.Action == SettingsAction.ShortcutAlt && shortcut.Alt ||
                c.Action == SettingsAction.ShortcutShift && shortcut.Shift ||
                c.Action == SettingsAction.ShortcutWin && shortcut.Win ||
                c.Action == SettingsAction.Afterglow && settings.Effects.Afterglow ||
                c.Action == SettingsAction.Spotlight && settings.Effects.Spotlight ||
                c.Action == SettingsAction.Edges && settings.Effects.Edges ||
                c.Action == SettingsAction.Ripples && settings.Effects.Ripples ||
                c.Action == SettingsAction.PressFlash && settings.Effects.PressFlash ||
                c.Action == SettingsAction.TransitionNone && settings.Effects.Transition == CharacterTransition.None ||
                c.Action == SettingsAction.Crossfade && settings.Effects.Transition == CharacterTransition.Crossfade ||
                c.Action == SettingsAction.Lift && settings.Effects.Transition == CharacterTransition.Lift;
            var enabled = c.Selectable && (c.Action == SettingsAction.Autostart ? autostart.CanClick : SettingsControls.Enabled(c.Action, settings));
            var b = c.Bounds;
            var rect = new SKRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height);
            var keyboardChoice = choosingKey && c.Action >= SettingsAction.KeyChoiceFirst && (b.Y < 460 || b.X < 240 && b.Y < 720);
            void DrawSurface()
            {
                if (keyboardChoice)
                    Panel.DrawKey(canvas, new KeyboardKey("Choice", c.Label, 0, b,
                        CutoutWidth: c.CutoutWidth, CutoutTop: c.CutoutTop), paint);
                else canvas.DrawRoundRect(rect, 10, 10, paint);
            }
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
                DrawSurface();
            }
            if (!softTheme && hovered)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 3;
                paint.Color = text;
                DrawSurface();
                paint.Style = SKPaintStyle.Fill;
            }
            paint.Color = !enabled ? new SKColor(0x66, 0x78, 0x82) : selected ? new SKColor(0x09, 0x19, 0x23) : text;
            if (softTheme) paint.Color = selected ? KeyboardTheme.Soft.Accent : KeyboardTheme.Soft.Text;
            var title = c.Action == SettingsAction.ToggleSound ? settings.SoundEnabled ? "On" : "Off" : c.Label;
            if (c.Action == SettingsAction.ToggleNumpad) title = settings.NumpadEnabled ? "Numpad: On" : "Numpad: Off";
            if (c.Action == SettingsAction.Autostart) title = autostart.ButtonLabel;
            if (c.Action == SettingsAction.ToggleShortcuts) title = settings.ProgrammableKeys.Enabled ? "Shown" : "Hidden";
            if (c.Action == SettingsAction.ChooseShortcutPreset) title = shortcut.LabelFor(layout) + "   ›";
            if (c.Action == SettingsAction.ChooseShortcutKey) title = ProgrammableKeys.KeyName(shortcut.Scan, layout, shortcut.Shift) + "   ›";
            if (c.Action == SettingsAction.Geometry) title = settings.Geometry switch
            { KeyboardGeometry.Ansi => "ANSI", KeyboardGeometry.Iso => "ISO", _ => "Auto" };
            var font = preset.HasValue || effectsPage || shortcutsPage || c.Action is SettingsAction.Autostart or SettingsAction.Defaults or SettingsAction.ResetPosition or SettingsAction.GeneralTab or SettingsAction.EffectsTab or SettingsAction.ShortcutsTab ? small : label;
            using var fitted = new SKFont(face, Math.Min(font.Size, font.Size * (rect.Width - 12) / Math.Max(1, font.MeasureText(title))));
            font = fitted;
            var baseline = rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
            if (SettingsControls.SlotIndex(c.Action) is var index && index >= 0)
            {
                canvas.DrawText(title, rect.MidX, rect.Top + 20, SKTextAlign.Center, font, paint);
                var assignment = settings.ProgrammableKeys.Get(index).LabelFor(layout);
                using var assignmentFont = new SKFont(face, 14);
                if (rect.Width < 110)
                {
                    assignmentFont.Size = 12;
                    if (assignmentFont.MeasureText(assignment) > rect.Width - 12)
                    {
                        while (assignment.Length > 0 && assignmentFont.MeasureText(assignment + "…") > rect.Width - 12)
                            assignment = assignment[..^1];
                        assignment += "…";
                    }
                }
                else assignmentFont.Size = Math.Min(14, 14 * (rect.Width - 12) / Math.Max(1, assignmentFont.MeasureText(assignment)));
                canvas.DrawText(assignment, rect.MidX, rect.Top + 42, SKTextAlign.Center, assignmentFont, paint);
            }
            else if (preset.HasValue)
            {
                SoundPresetIcon.Draw(canvas, preset.Value, rect.MidX - SoundPresetIcon.Size / 2, rect.Top + 10);
                var labelY = rect.Top + 68 - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
                canvas.DrawText(title, rect.MidX, labelY, SKTextAlign.Center, font, paint);
            }
            else canvas.DrawText(title, rect.MidX + c.CutoutWidth / 2, baseline, SKTextAlign.Center, font, paint);
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
