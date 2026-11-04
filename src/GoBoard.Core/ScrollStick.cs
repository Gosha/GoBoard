namespace GoBoard.Core;

// While the dashboard owns controller input, SteamVR withholds the joystick
// from controller state and IVRInput. It still reaches the pointed-at overlay
// as smooth scroll events, repeated while the stick is held.
internal sealed class ScrollStick
{
    // Full deflection reports about this vertical delta per event.
    public const float FullDeflection = .22f;
    // A held stick repeats faster than this; afterwards treat it as released.
    public const double HoldSeconds = .15;
    private float value;
    private double time = double.NegativeInfinity;

    public void Observe(float yDelta, double time)
    {
        if (!float.IsFinite(yDelta) || !double.IsFinite(time) || time < this.time) return;
        value = Math.Clamp(yDelta / FullDeflection, -1, 1);
        this.time = time;
    }

    public float Read(double now) => now - time <= HoldSeconds ? value : 0;

    public void Reset()
    {
        value = 0;
        time = double.NegativeInfinity;
    }
}
