namespace GoBoard.Core;

// Release-to-activate input shared by desktop and VR floating keyboard controls.
internal sealed class FloatingButtonState
{
    private readonly GrabInput input = new(MainKeyboardControls.Size, MainKeyboardControls.Size);
    private double acceptAfter = double.NegativeInfinity;
    public bool Hovered => input.HasFocus;
    public bool Pressed => input.Active != null;
    public uint? CapturedDevice => input.Active?.Device;
    public int Revision { get; private set; }

    public void Reset(double now)
    {
        if (Hovered || Pressed) Revision++;
        input.Reset();
        acceptAfter = Math.Max(acceptAfter, now);
    }

    public bool Process(uint pointer, uint device, float x, float y, double time, double now,
        bool down = false, bool up = false, bool leave = false)
    {
        if (!double.IsFinite(time) || time <= acceptAfter || time > now + .001 || now - time > .2) return false;
        var before = (Hovered, Pressed);
        var inside = !leave && float.IsFinite(x) && float.IsFinite(y) &&
            x >= 0 && y >= 0 && x < MainKeyboardControls.Size && y < MainKeyboardControls.Size;
        if (inside) input.ObserveMotion(pointer, device, x, y, time, now, true);
        else input.Leave(pointer, device, time);
        if (down && inside) input.Down(pointer, device, x, y, time, now);
        var clicked = up && inside && input.Active is { } press && press.Cursor == pointer && press.Device == device && time >= press.Time;
        if (up) input.Up(pointer, device, time);
        if (clicked) Reset(now);
        else if (before != (Hovered, Pressed)) Revision++;
        return clicked;
    }
}
