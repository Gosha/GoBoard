namespace GoBoard.Core;

// Shared release-to-toggle button, independent of keyboard input and settings.
internal sealed class ShortcutLauncherState
{
    private readonly GrabInput input = new(ProgrammableKeys.ToggleSize, ProgrammableKeys.ToggleSize);
    private double acceptAfter = double.NegativeInfinity;
    public bool Expanded { get; private set; }
    public bool Hovered => input.HasFocus;
    public void RemoveUntracked(Func<uint, bool> tracked, double now)
    {
        var before = (Hovered, CapturedDevice);
        input.RemoveUntracked(tracked, now);
        if (before != (Hovered, CapturedDevice)) Revision++;
    }
    public uint? CapturedDevice => input.Active?.Device;
    public int Revision { get; private set; }
    public void Reset(double now, bool collapse = false)
    {
        input.Reset(); acceptAfter = now;
        if (collapse) Expanded = false;
        Revision++;
    }
    public bool Process(uint cursor, uint device, float x, float y, double time, double now,
        bool down = false, bool up = false, bool leave = false)
    {
        if (!double.IsFinite(time) || time <= acceptAfter || time > now + .001 || now - time > .2) return false;
        var inside = !leave && float.IsFinite(x) && float.IsFinite(y) && x >= 0 && y >= 0 && x < ProgrammableKeys.ToggleSize && y < ProgrammableKeys.ToggleSize;
        var hovered = Hovered;
        if (inside) input.ObserveMotion(cursor, device, x, y, time, now, true);
        else input.Leave(cursor, device, time);
        if (down && inside) input.Down(cursor, device, x, y, time, now);
        var clicked = up && inside && input.Active is { } press && press.Cursor == cursor && press.Device == device && time >= press.Time;
        if (up) input.Up(cursor, device, time);
        if (clicked) { Expanded = !Expanded; Reset(now); }
        else if (hovered != Hovered) Revision++;
        return clicked;
    }
}
