namespace GoBoard.Core;

internal enum SettingsEditorEffect { None, AuditionSound, ToggleAutostart }

// Owns the edit sequence shared by desktop and VR. Native events are translated
// by the hosts; layout observation and playback stay outside Core.
internal sealed class SettingsEditor(SettingsStore store, Func<KeyboardGeometry, WindowsLayout> layout)
{
    private string actionError;
    public SettingsPointerState Pointers { get; } = new();
    public BoardSettings Current => store.Current;
    public string Error => actionError ?? store.Error;

    public void Refresh() => Pointers.Configure(Current, layout(Current.Geometry));
    public void Reset() => Pointers.Reset();

    public SettingsEditorEffect Process(uint device, float x, float y, double time, double now,
        bool down = false, bool up = false, bool leave = false)
    {
        Refresh();
        var action = Pointers.Process(device, x, y, time, now, down, up, leave);
        if (action == SettingsAction.Autostart) return SettingsEditorEffect.ToggleAutostart;
        if (!action.HasValue || !SettingsControls.Enabled(action.Value, Current)) return SettingsEditorEffect.None;

        // Recheck against the latest file under SettingsStore's mutex. Other
        // editors may have changed it since this host last refreshed.
        var saved = store.Update(s => SettingsControls.Enabled(action.Value, s)
            ? SettingsControls.Apply(action.Value, s, Pointers.ShortcutSlot, Pointers.Layout) : s);
        actionError = saved ? null : store.Error;
        Refresh();
        return saved && SettingsControls.AuditionsSound(action.Value)
            ? SettingsEditorEffect.AuditionSound : SettingsEditorEffect.None;
    }
}
