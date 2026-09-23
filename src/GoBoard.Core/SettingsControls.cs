namespace GoBoard.Core;

internal enum SettingsPage { General, Effects, Shortcuts, ShortcutKey, ShortcutPreset, Inactivity }
internal enum SettingsAction
{
    Smaller, Larger, ToggleSound, Wood, Thud, Quieter, Louder, Defaults, Geometry, SteamSoft, SteamFlat,
    GeneralTab, EffectsTab, Afterglow, Spotlight, Edges, Ripples, PressFlash,
    TransitionNone, Crossfade, Lift, TransitionFaster, TransitionSlower, TravelLess, TravelMore,
    EnterFaster, EnterSlower, LeaveFaster, LeaveSlower, RadiusLess, RadiusMore,
    StrengthLess, StrengthMore, RippleFaster, RippleSlower, ResetEffects,
    CherryBlue, GateronYellow, ResetPosition,
    ShortcutsTab, ToggleShortcuts, Slot1, Slot2, Slot3, Slot4, Slot5, Slot6, Slot7, Slot8,
    ChooseShortcutPreset, BackToShortcut, ShortcutCtrl, ShortcutAlt, ShortcutShift, ShortcutWin, ChooseShortcutKey,
    ResetShortcuts, Slot9, Slot10, FewerColumns, MoreColumns, FewerRows, MoreRows,
    Slot11, Slot12, Slot13, Slot14, Slot15, Slot16, Slot17, Slot18, Slot19, Slot20,
    Autostart, ToggleNumpad, ToggleNumpadButton, PresetChoiceFirst = 100,
    InactivityTab = 500, IdleOff, IdleHide, IdleMinimize, IdleTransparent,
    IdleSooner, IdleLater, RevealSooner, RevealLater, FadeFaster, FadeSlower,
    IdleOpacityLess, IdleOpacityMore, IdleSizeLess, IdleSizeMore, ResetInactivity,
    KeyChoiceFirst = 1000
}
internal sealed record SettingsControl(SettingsAction Action, string Label, KeyBounds Bounds,
    bool Selectable = true, float CutoutWidth = 0, float CutoutTop = 0)
{
    public bool Contains(float x, float y) => Selectable && Bounds.Contains(x, y) &&
        !(CutoutWidth > 0 && x < Bounds.X + CutoutWidth && y >= Bounds.Y + CutoutTop);
}

// Preserve the dashboard aspect ratio in a resizable desktop window. Painting
// and hit testing use this same viewport, including any letterboxed margins.
internal readonly record struct SettingsViewport(float X, float Y, float Scale)
{
    public float Width => SettingsControls.Width * Scale;
    public float Height => SettingsControls.Height * Scale;
    public static SettingsViewport Fit(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return default;
        var scale = Math.Min(width / SettingsControls.Width, height / SettingsControls.Height);
        return new((width - SettingsControls.Width * scale) / 2, (height - SettingsControls.Height * scale) / 2, scale);
    }
    public (float X, float Y) ToPanel(float x, float y) => Scale > 0 &&
        x >= X && x < X + Width && y >= Y && y < Y + Height
        ? ((x - X) / Scale, (y - Y) / Scale) : (float.NaN, float.NaN);
}

internal static class SettingsControls
{
    public const int Width = 900, Height = 950;
    public static readonly SettingsControl[] All =
    [
        new(SettingsAction.Smaller, "−", new(588, 158, 100, 64)),
        new(SettingsAction.Larger, "+", new(704, 158, 100, 64)),
        new(SettingsAction.ToggleSound, "", new(588, 260, 216, 64)),
        new(SettingsAction.Wood, KeySounds.Name(KeySound.CushionedWood), new(64, 358, 173, 88)),
        new(SettingsAction.Thud, KeySounds.Name(KeySound.SoftLowThud), new(253, 358, 173, 88)),
        new(SettingsAction.CherryBlue, KeySounds.Name(KeySound.CherryMxBlue), new(442, 358, 173, 88)),
        new(SettingsAction.GateronYellow, KeySounds.Name(KeySound.GateronYellowPairs), new(631, 358, 173, 88)),
        new(SettingsAction.Quieter, "−", new(588, 474, 100, 64)),
        new(SettingsAction.Louder, "+", new(704, 474, 100, 64)),
        new(SettingsAction.Geometry, "", new(588, 550, 216, 64)),
        new(SettingsAction.ToggleNumpadButton, "", new(356, 550, 216, 64)),
        new(SettingsAction.SteamSoft, BoardThemes.Name(BoardThemes.SteamSoft), new(64, 668, 340, 64)),
        new(SettingsAction.SteamFlat, BoardThemes.Name(BoardThemes.SteamFlat), new(420, 668, 384, 64)),
        new(SettingsAction.Autostart, "Register autostart", new(588, 758, 216, 64)),
        new(SettingsAction.ResetPosition, "Reset position", new(356, 870, 216, 48)),
        new(SettingsAction.Defaults, "Reset to defaults", new(588, 870, 216, 48))
    ];

    public static readonly SettingsControl[] Tabs =
    [new(SettingsAction.GeneralTab, "General", new(284, 32, 120, 54)),
     new(SettingsAction.EffectsTab, "Effects", new(416, 32, 120, 54)),
     new(SettingsAction.ShortcutsTab, "Shortcuts", new(548, 32, 120, 54)),
     new(SettingsAction.InactivityTab, "Inactivity", new(680, 32, 124, 54))];

    public static readonly (string Label, SettingsAction Less, SettingsAction More)[] InactivityParameters =
    [
        ("After pointers leave", SettingsAction.IdleSooner, SettingsAction.IdleLater),
        ("Hover to reveal", SettingsAction.RevealSooner, SettingsAction.RevealLater),
        ("Hide / shrink / fade duration", SettingsAction.FadeFaster, SettingsAction.FadeSlower),
        ("Transparent mode opacity", SettingsAction.IdleOpacityLess, SettingsAction.IdleOpacityMore),
        ("Minimized keyboard size", SettingsAction.IdleSizeLess, SettingsAction.IdleSizeMore)
    ];
    public static readonly SettingsControl[] Inactivity =
    [
        new(SettingsAction.IdleOff, "Off", InactivitySettingsLayout.ModeBounds(0)),
        new(SettingsAction.IdleHide, "Hide", InactivitySettingsLayout.ModeBounds(1)),
        new(SettingsAction.IdleMinimize, "Minimize", InactivitySettingsLayout.ModeBounds(2)),
        new(SettingsAction.IdleTransparent, "Transparent", InactivitySettingsLayout.ModeBounds(3)),
        .. InactivityParameters.SelectMany((p, i) => new[] {
            new SettingsControl(p.Less, "−", InactivitySettingsLayout.DecreaseBounds(i)),
            new SettingsControl(p.More, "+", InactivitySettingsLayout.IncreaseBounds(i)) }),
        new(SettingsAction.ResetInactivity, "Reset inactivity", InactivitySettingsLayout.ResetBounds)
    ];
    public static string InactivityValue(int index, InactivitySettings s) => index switch
    {
        0 => $"{s.DelayMs / 1000.0:0.##} s", 1 => s.RevealMs == 0 ? "Instant" : $"{s.RevealMs} ms",
        2 => s.TransitionMs == 0 ? "Instant" : $"{s.TransitionMs} ms",
        3 => $"{s.OpacityPercent}%", _ => $"{s.SizePercent}%"
    };
    public static readonly SettingsControl[] Shortcuts =
    [
        new(SettingsAction.ToggleShortcuts, "", new(588, 148, 216, 54)),
        new(SettingsAction.FewerColumns, "−", new(226, 266, 44, 44)),
        new(SettingsAction.MoreColumns, "+", new(350, 266, 44, 44)),
        new(SettingsAction.FewerRows, "−", new(226, 322, 44, 44)),
        new(SettingsAction.MoreRows, "+", new(350, 322, 44, 44)),
        .. Enumerable.Range(0, ProgrammableKeySettings.Capacity).Select(i => new SettingsControl(SlotAction(i), $"Key {i + 1}", new(0, 0, 158, 54))),
        new(SettingsAction.ChooseShortcutPreset, "Choose preset   ›", new(454, 350, 350, 54)),
        new(SettingsAction.ShortcutWin, "Win", new(454, 464, 167, 48)),
        new(SettingsAction.ShortcutCtrl, "Ctrl", new(637, 464, 167, 48)),
        new(SettingsAction.ShortcutAlt, "Alt", new(454, 524, 167, 48)),
        new(SettingsAction.ShortcutShift, "Shift", new(637, 524, 167, 48)),
        new(SettingsAction.ChooseShortcutKey, "", new(454, 636, 350, 54)),
        new(SettingsAction.ResetShortcuts, "Reset shortcuts", new(588, 770, 216, 48))
    ];
    public static SettingsAction SlotAction(int slot) => slot < 8 ? SettingsAction.Slot1 + slot : slot == 8 ? SettingsAction.Slot9 : slot == 9 ? SettingsAction.Slot10 : SettingsAction.Slot11 + slot - 10;
    public static int SlotIndex(SettingsAction action) => action is >= SettingsAction.Slot1 and <= SettingsAction.Slot8 ? (int)action - (int)SettingsAction.Slot1 : action == SettingsAction.Slot9 ? 8 : action == SettingsAction.Slot10 ? 9 :
        action is >= SettingsAction.Slot11 and <= SettingsAction.Slot20 ? (int)action - (int)SettingsAction.Slot11 + 10 : -1;
    public static int PresetIndex(SettingsAction action)
    {
        var index = action - SettingsAction.PresetChoiceFirst;
        return index >= 0 && index < ProgrammableKeys.Presets.Length ? index : -1;
    }
    public static readonly SettingsControl[] PresetChoices = BuildPresetChoices();
    private static SettingsControl[] BuildPresetChoices()
    {
        var controls = new List<SettingsControl>();
        foreach (var category in Enum.GetValues<ProgrammableKeys.PresetCategory>())
        {
            var row = 0;
            for (var i = 0; i < ProgrammableKeys.Presets.Length; i++)
            {
                var preset = ProgrammableKeys.Presets[i];
                if (preset.Category != category) continue;
                controls.Add(new(SettingsAction.PresetChoiceFirst + i, preset.Label,
                    new(64 + (int)category * 189, 244 + row++ * 60, 173, 54)));
            }
        }
        controls.Add(new(SettingsAction.BackToShortcut, "Back", new(64, 828, 173, 54)));
        return controls.ToArray();
    }
    public static SettingsControl[] KeyChoices(WindowsLayout layout = null, bool shift = false)
    {
        layout ??= new((nint)WindowsLayout.UsHandle);
        const float scale = 740f / OverlayGeometry.PanelWidth;
        // Reuse the physical arrangement, including ISO Enter's shared drawing/hit shape.
        var controls = layout.Keys.Select(key => new SettingsControl(SettingsAction.KeyChoiceFirst + key.Scan,
            key.IsModifier || key.Scan == KeyboardLayout.ImeToggleKey ? key.Label : ProgrammableKeys.KeyName(key.Scan, layout, shift),
            new(64 + key.Bounds.X * scale, 200 + key.Bounds.Y * scale, key.Bounds.Width * scale, key.Bounds.Height * scale),
            !key.IsModifier && ProgrammableKeys.IsAllowed(key.Scan), key.CutoutWidth * scale, key.CutoutTop * scale)).ToList();
        controls.AddRange(ProgrammableKeys.NumpadKeys.Select(key => new SettingsControl(SettingsAction.KeyChoiceFirst + key.Scan,
            key.Label, key.Bounds with { X = 64 + key.Bounds.X, Y = 496 + key.Bounds.Y })));
        for (var i = 0; i < ProgrammableKeys.SystemKeys.Length; i++)
        {
            var key = ProgrammableKeys.SystemKeys[i];
            var browser = i >= 7; var index = browser ? i - 7 : i;
            controls.Add(new(SettingsAction.KeyChoiceFirst + key.Scan, browser ? char.ToUpperInvariant(key.Label[8]) + key.Label[9..] : key.Label,
                new(270 + index % 4 * 135.5f, (browser ? 632 : 496) + index / 4 * 48, 127.5f, 40)));
        }
        controls.Add(new(SettingsAction.KeyChoiceFirst, "None", new(64, 768, 216, 42)));
        return controls.ToArray();
    }
    public static readonly (string Label, SettingsAction Less, SettingsAction More)[] Parameters =
    [
        ("Character duration", SettingsAction.TransitionFaster, SettingsAction.TransitionSlower),
        ("Lift distance", SettingsAction.TravelLess, SettingsAction.TravelMore),
        ("Hover fade in", SettingsAction.EnterFaster, SettingsAction.EnterSlower),
        ("Hover fade out", SettingsAction.LeaveFaster, SettingsAction.LeaveSlower),
        ("Light radius", SettingsAction.RadiusLess, SettingsAction.RadiusMore),
        ("Effect strength", SettingsAction.StrengthLess, SettingsAction.StrengthMore),
        ("Click duration", SettingsAction.RippleFaster, SettingsAction.RippleSlower)
    ];
    public static readonly SettingsControl[] Effects = BuildEffects();
    private static SettingsControl[] BuildEffects()
    {
        var controls = new List<SettingsControl>
        {
            new(SettingsAction.Afterglow, "Afterglow", new(64, 156, 236, 52)),
            new(SettingsAction.Spotlight, "Spotlight", new(316, 156, 236, 52)),
            new(SettingsAction.Edges, "Proximity edges", new(568, 156, 236, 52)),
            new(SettingsAction.Ripples, "Click ripple", new(64, 220, 236, 52)),
            new(SettingsAction.PressFlash, "Press flash / tint", new(316, 220, 236, 52)),
            new(SettingsAction.TransitionNone, "None", new(64, 322, 236, 52)),
            new(SettingsAction.Crossfade, "Crossfade", new(316, 322, 236, 52)),
            new(SettingsAction.Lift, "Lift", new(568, 322, 236, 52)),
            new(SettingsAction.ResetEffects, "Reset effects", new(588, 770, 216, 48))
        };
        for (var i = 0; i < Parameters.Length; i++)
        {
            var x = 64 + i % 2 * 392; var y = 414 + i / 2 * 84;
            controls.Add(new(Parameters[i].Less, "−", new(x, y, 54, 44)));
            controls.Add(new(Parameters[i].More, "+", new(x + 294, y, 54, 44)));
        }
        return controls.ToArray();
    }
    public static IEnumerable<SettingsControl> ForPage(SettingsPage page, BoardSettings settings = null, WindowsLayout layout = null, int slot = 0) => Tabs.Concat(page switch
    { SettingsPage.Effects => Effects,
      SettingsPage.Inactivity => Inactivity,
      SettingsPage.Shortcuts => ShortcutControls(settings?.ProgrammableKeys ?? new()),
      SettingsPage.ShortcutPreset => PresetChoices,
      SettingsPage.ShortcutKey => KeyChoices(layout, settings?.ProgrammableKeys.Get(slot).Shift ?? false), _ => All });
    private static IEnumerable<SettingsControl> ShortcutControls(ProgrammableKeySettings settings)
    {
        foreach (var control in Shortcuts)
        {
            var slot = SlotIndex(control.Action);
            if (slot < 0) { yield return control; continue; }
            if (settings.VisibleSlots.Contains(slot))
            {
                var (row, column) = ProgrammableKeySettings.PositionOf(slot);
                var gap = settings.Columns > 2 ? 8 : 14;
                var width = settings.Columns == 1 ? 158 : (330f - gap * (settings.Columns - 1)) / settings.Columns;
                yield return control with { Label = $"Key {settings.NumberFor(slot)}", Bounds = new(
                    settings.Columns == 1 ? 150 : 64 + column * (width + gap), 388 + row * 62, width, 54) };
            }
        }
    }
    public static SettingsAction? Hit(float x, float y, SettingsPage page = SettingsPage.General, BoardSettings settings = null, WindowsLayout layout = null, int slot = 0)
        => ForPage(page, settings, layout, slot).FirstOrDefault(c => c.Contains(x, y))?.Action;
    public static string ParameterValue(int index, EffectSettings e) => index switch
    {
        0 => $"{e.TransitionMs} ms", 1 => $"{e.Travel} px", 2 => e.EnterMs == 0 ? "Instant" : $"{e.EnterMs} ms",
        3 => $"{e.LeaveMs} ms", 4 => $"{e.Radius} px", 5 => $"{e.Strength}%", _ => $"{e.RippleMs} ms"
    };
    public static bool Enabled(SettingsAction action, BoardSettings s) => action switch
    {
        >= SettingsAction.IdleSooner and <= SettingsAction.IdleSizeMore => Apply(action, s) != s,
        SettingsAction.Smaller => s.SizePercent > 50,
        SettingsAction.Larger => s.SizePercent < 150,
        SettingsAction.Quieter => s.SoundEnabled && s.VolumePercent > 0,
        SettingsAction.Louder => s.SoundEnabled && s.VolumePercent < 100,
        SettingsAction.FewerColumns => s.ProgrammableKeys.Columns > 1,
        SettingsAction.MoreColumns => s.ProgrammableKeys.Columns < ProgrammableKeySettings.MaxColumns,
        SettingsAction.FewerRows => s.ProgrammableKeys.Rows > 1,
        SettingsAction.MoreRows => s.ProgrammableKeys.Rows < ProgrammableKeySettings.MaxRows,
        >= SettingsAction.TransitionFaster and <= SettingsAction.RippleSlower => Apply(action, s) != s,
        _ => true
    };
    public static KeySound? SoundFor(SettingsAction action) => action switch
    {
        SettingsAction.Wood => KeySound.CushionedWood,
        SettingsAction.Thud => KeySound.SoftLowThud,
        SettingsAction.CherryBlue => KeySound.CherryMxBlue,
        SettingsAction.GateronYellow => KeySound.GateronYellowPairs,
        _ => null
    };
    public static bool AuditionsSound(SettingsAction action) => SoundFor(action).HasValue || action is
        SettingsAction.ToggleSound or SettingsAction.Quieter or SettingsAction.Louder;
    public static BoardSettings Apply(SettingsAction action, BoardSettings s, int slot = 0, WindowsLayout layout = null) => (action switch
    {
        SettingsAction.IdleOff => s with { Inactivity = s.Inactivity with { Mode = InactivityMode.Off } },
        SettingsAction.IdleHide => s with { Inactivity = s.Inactivity with { Mode = InactivityMode.Hide } },
        SettingsAction.IdleMinimize => s with { Inactivity = s.Inactivity with { Mode = InactivityMode.Minimize } },
        SettingsAction.IdleTransparent => s with { Inactivity = s.Inactivity with { Mode = InactivityMode.Transparent } },
        SettingsAction.IdleSooner => s with { Inactivity = s.Inactivity with { DelayMs = s.Inactivity.DelayMs - 250 } },
        SettingsAction.IdleLater => s with { Inactivity = s.Inactivity with { DelayMs = s.Inactivity.DelayMs + 250 } },
        SettingsAction.RevealSooner => s with { Inactivity = s.Inactivity with { RevealMs = s.Inactivity.RevealMs - 50 } },
        SettingsAction.RevealLater => s with { Inactivity = s.Inactivity with { RevealMs = s.Inactivity.RevealMs + 50 } },
        SettingsAction.FadeFaster => s with { Inactivity = s.Inactivity with { TransitionMs = s.Inactivity.TransitionMs - 50 } },
        SettingsAction.FadeSlower => s with { Inactivity = s.Inactivity with { TransitionMs = s.Inactivity.TransitionMs + 50 } },
        SettingsAction.IdleOpacityLess => s with { Inactivity = s.Inactivity with { OpacityPercent = s.Inactivity.OpacityPercent - 5 } },
        SettingsAction.IdleOpacityMore => s with { Inactivity = s.Inactivity with { OpacityPercent = s.Inactivity.OpacityPercent + 5 } },
        SettingsAction.IdleSizeLess => s with { Inactivity = s.Inactivity with { SizePercent = s.Inactivity.SizePercent - 5 } },
        SettingsAction.IdleSizeMore => s with { Inactivity = s.Inactivity with { SizePercent = s.Inactivity.SizePercent + 5 } },
        SettingsAction.ResetInactivity => s with { Inactivity = new() },
        SettingsAction.ToggleNumpad => s with { NumpadEnabled = !s.NumpadEnabled },
        SettingsAction.ToggleNumpadButton => s with { NumpadButtonEnabled = !s.NumpadButtonEnabled },
        SettingsAction.ToggleShortcuts => s with { ProgrammableKeys = s.ProgrammableKeys with { Enabled = !s.ProgrammableKeys.Enabled } },
        SettingsAction.FewerColumns => s with { ProgrammableKeys = s.ProgrammableKeys with { Columns = s.ProgrammableKeys.Columns - 1 } },
        SettingsAction.MoreColumns => s with { ProgrammableKeys = s.ProgrammableKeys with { Columns = s.ProgrammableKeys.Columns + 1 } },
        SettingsAction.FewerRows => s with { ProgrammableKeys = s.ProgrammableKeys with { Rows = s.ProgrammableKeys.Rows - 1 } },
        SettingsAction.MoreRows => s with { ProgrammableKeys = s.ProgrammableKeys with { Rows = s.ProgrammableKeys.Rows + 1 } },
        SettingsAction.ResetShortcuts => s with { ProgrammableKeys = new() { Enabled = s.ProgrammableKeys.Enabled } },
        _ when PresetIndex(action) is var index && index >= 0 =>
            s with { ProgrammableKeys = s.ProgrammableKeys.Set(slot, ProgrammableKeys.Presets[index].ForLayout(layout)) },
        SettingsAction.ShortcutCtrl or SettingsAction.ShortcutAlt or
            SettingsAction.ShortcutShift or SettingsAction.ShortcutWin => s with { ProgrammableKeys = s.ProgrammableKeys.Set(slot, EditShortcut(action, s.ProgrammableKeys.Get(slot))) },
        >= SettingsAction.KeyChoiceFirst when (int)action - (int)SettingsAction.KeyChoiceFirst <= ushort.MaxValue && ProgrammableKeys.IsAllowed((ushort)(action - SettingsAction.KeyChoiceFirst)) =>
            s with { ProgrammableKeys = s.ProgrammableKeys.Set(slot, s.ProgrammableKeys.Get(slot) with { Scan = (ushort)(action - SettingsAction.KeyChoiceFirst) }) },
        SettingsAction.Smaller => s with { SizePercent = s.SizePercent - 5 },
        SettingsAction.Larger => s with { SizePercent = s.SizePercent + 5 },
        SettingsAction.ToggleSound => s with { SoundEnabled = !s.SoundEnabled },
        _ when SoundFor(action) is { } sound => s with { Sound = sound },
        SettingsAction.Quieter => s with { VolumePercent = s.VolumePercent - 10 },
        SettingsAction.Louder => s with { VolumePercent = s.VolumePercent + 10 },
        SettingsAction.SteamSoft => s with { Theme = BoardThemes.SteamSoft },
        SettingsAction.SteamFlat => s with { Theme = BoardThemes.SteamFlat },
        SettingsAction.ResetPosition => s with { PositionResetId = Guid.NewGuid() },
        SettingsAction.Defaults => new BoardSettings { PositionResetId = s.PositionResetId },
        SettingsAction.Geometry => s with { Geometry = (KeyboardGeometry)(((int)s.Geometry + 1) % 3) },
        SettingsAction.ResetEffects => s with { Effects = new() },
        SettingsAction.Afterglow => s with { Effects = s.Effects with { Afterglow = !s.Effects.Afterglow } },
        SettingsAction.Spotlight => s with { Effects = s.Effects with { Spotlight = !s.Effects.Spotlight } },
        SettingsAction.Edges => s with { Effects = s.Effects with { Edges = !s.Effects.Edges } },
        SettingsAction.Ripples => s with { Effects = s.Effects with { Ripples = !s.Effects.Ripples } },
        SettingsAction.PressFlash => s with { Effects = s.Effects with { PressFlash = !s.Effects.PressFlash } },
        SettingsAction.TransitionNone => s with { Effects = s.Effects with { Transition = CharacterTransition.None } },
        SettingsAction.Crossfade => s with { Effects = s.Effects with { Transition = CharacterTransition.Crossfade } },
        SettingsAction.Lift => s with { Effects = s.Effects with { Transition = CharacterTransition.Lift } },
        SettingsAction.TransitionFaster => s with { Effects = s.Effects with { TransitionMs = s.Effects.TransitionMs - 20 } },
        SettingsAction.TransitionSlower => s with { Effects = s.Effects with { TransitionMs = s.Effects.TransitionMs + 20 } },
        SettingsAction.TravelLess => s with { Effects = s.Effects with { Travel = s.Effects.Travel - 1 } },
        SettingsAction.TravelMore => s with { Effects = s.Effects with { Travel = s.Effects.Travel + 1 } },
        SettingsAction.EnterFaster => s with { Effects = s.Effects with { EnterMs = s.Effects.EnterMs - 20 } },
        SettingsAction.EnterSlower => s with { Effects = s.Effects with { EnterMs = s.Effects.EnterMs + 20 } },
        SettingsAction.LeaveFaster => s with { Effects = s.Effects with { LeaveMs = s.Effects.LeaveMs - 20 } },
        SettingsAction.LeaveSlower => s with { Effects = s.Effects with { LeaveMs = s.Effects.LeaveMs + 20 } },
        SettingsAction.RadiusLess => s with { Effects = s.Effects with { Radius = s.Effects.Radius - 10 } },
        SettingsAction.RadiusMore => s with { Effects = s.Effects with { Radius = s.Effects.Radius + 10 } },
        SettingsAction.StrengthLess => s with { Effects = s.Effects with { Strength = s.Effects.Strength - 5 } },
        SettingsAction.StrengthMore => s with { Effects = s.Effects with { Strength = s.Effects.Strength + 5 } },
        SettingsAction.RippleFaster => s with { Effects = s.Effects with { RippleMs = s.Effects.RippleMs - 20 } },
        SettingsAction.RippleSlower => s with { Effects = s.Effects with { RippleMs = s.Effects.RippleMs + 20 } },
        _ => s
    }).Normalize();
    private static KeyboardShortcut EditShortcut(SettingsAction action, KeyboardShortcut key)
    {
        return action switch
        {
            SettingsAction.ShortcutCtrl => key with { Ctrl = !key.Ctrl },
            SettingsAction.ShortcutAlt => key with { Alt = !key.Alt },
            SettingsAction.ShortcutShift => key with { Shift = !key.Shift },
            SettingsAction.ShortcutWin => key with { Win = !key.Win }, _ => key
        };
    }
}

// Release-to-activate; leaving a button cancels its capture. Per-controller
// timestamps reject stale edges without letting one hand release the other.
internal sealed class SettingsPointerState
{
    private sealed class Pointer
    {
        public double Last = double.NegativeInfinity;
        public SettingsAction? Hover, Capture;
    }
    private readonly Dictionary<uint, Pointer> pointers = new();
    public SettingsPage Page { get; private set; }
    public int ShortcutSlot { get; private set; }
    public WindowsLayout Layout { get; private set; } = new((nint)WindowsLayout.UsHandle);
    private BoardSettings settings = new();
    public void Configure(BoardSettings value, WindowsLayout layout)
    {
        if (Layout != layout || settings.ProgrammableKeys != value.ProgrammableKeys)
        {
            Reset();
            if (!value.ProgrammableKeys.VisibleSlots.Contains(ShortcutSlot)) ShortcutSlot = 0;
        }
        Layout = layout;
        settings = value;
    }
    public int Revision { get; private set; }
    public bool Hovered(SettingsAction action) => pointers.Values.Any(p => p.Hover == action);
    public void Reset() { pointers.Clear(); Revision++; }
    public void SelectPage(SettingsPage page) { Page = page; Reset(); }
    public SettingsAction? Process(uint device, float x, float y, double time, double now, bool down = false, bool up = false, bool leave = false)
    {
        if (!double.IsFinite(time) || time > now || now - time > .5) return null;
        if (!pointers.TryGetValue(device, out var p)) pointers[device] = p = new();
        if (time < p.Last) return null;
        p.Last = time;
        var hit = leave ? null : SettingsControls.Hit(x, y, Page, settings, Layout, ShortcutSlot);
        if (p.Hover != hit) Revision++;
        p.Hover = hit;
        if (leave || (p.Capture.HasValue && p.Capture != hit)) p.Capture = null;
        if (down) p.Capture = now - time <= .2 ? hit : null;
        if (!up) return null;
        var clicked = p.Capture == hit ? hit : null;
        p.Capture = null;
        if (clicked.HasValue && SettingsControls.SlotIndex(clicked.Value) >= 0)
        {
            ShortcutSlot = SettingsControls.SlotIndex(clicked.Value);
            Reset();
            return null;
        }
        if (clicked is SettingsAction.GeneralTab or SettingsAction.EffectsTab or SettingsAction.InactivityTab or SettingsAction.ShortcutsTab or SettingsAction.ChooseShortcutKey or SettingsAction.ChooseShortcutPreset or SettingsAction.BackToShortcut)
        {
            Page = clicked switch { SettingsAction.GeneralTab => SettingsPage.General, SettingsAction.EffectsTab => SettingsPage.Effects,
                SettingsAction.InactivityTab => SettingsPage.Inactivity,
                SettingsAction.ChooseShortcutKey => SettingsPage.ShortcutKey, SettingsAction.ChooseShortcutPreset => SettingsPage.ShortcutPreset, _ => SettingsPage.Shortcuts };
            Reset(); // Both hands lose captures from the old page.
            return null;
        }
        if (clicked >= SettingsAction.KeyChoiceFirst || clicked.HasValue && SettingsControls.PresetIndex(clicked.Value) >= 0) { Page = SettingsPage.Shortcuts; Reset(); }
        return clicked;
    }
}
