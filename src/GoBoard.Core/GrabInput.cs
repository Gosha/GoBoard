namespace GoBoard.Core;

// Pure pointer ownership rules. The host drains the entire event queue before
// committing Active to a controller transform, so a queued release wins.
internal sealed class GrabInput(float width = OverlayGeometry.GrabWidth,
    float height = OverlayGeometry.GrabHeight, bool retainCaptureOnLeave = false,
    Func<float, float, bool> hitTest = null)
{
    internal sealed record Press(uint Cursor, uint Device, double Time, float X, float Y);
    private sealed class Pointer
    {
        public bool Focused;
        public uint? Device;
        public double Entered, LastRelease = double.NegativeInfinity;
    }
    private readonly Dictionary<uint, Pointer> pointers = new();
    public Press Active { get; private set; }
    public bool HasFocus => pointers.Values.Any(p => p.Focused);
    public IEnumerable<uint> FocusedDevices => pointers.Values.Where(p => p.Focused && p.Device.HasValue).Select(p => p.Device.Value).Distinct().ToArray();
    public void RemoveUntracked(Func<uint, bool> tracked, double now)
    {
        foreach (var (cursor, p) in pointers)
            if (p.Device is { } device && !tracked(device))
            {
                Leave(cursor, device, now);
                if (Active?.Cursor == cursor) Cancel();
            }
    }

    public void Enter(uint cursor, uint? device, double time)
    {
        var p = Get(cursor);
        if (!double.IsFinite(time) || time < p.Entered || time < p.LastRelease) return;
        if (Active?.Cursor == cursor && device.HasValue && Active.Device != device.Value) Cancel();
        p.Focused = true;
        p.Device = device;
        p.Entered = time;
    }

    public void Leave(uint cursor, uint? device, double time)
    {
        var p = Get(cursor);
        if (!double.IsFinite(time) || time < p.Entered || Mismatch(p.Device, device)) return;
        p.Focused = false;
        p.LastRelease = Math.Max(p.LastRelease, time);
        if (Active?.Cursor == cursor && !retainCaptureOnLeave) Cancel();
    }

    public string Down(uint cursor, uint? device, float x, float y, double time, double now)
    {
        var p = Get(cursor);
        if (Active != null) return "already grabbed";
        if (!p.Focused) return "pointer has no handle focus";
        if (!double.IsFinite(time) || now - time > 0.20 || time > now + 0.001) return "stale press";
        if (time < p.Entered || time <= p.LastRelease + 0.001) return "press predates focus/release";
        if (Mismatch(p.Device, device)) return "controller does not own this pointer";
        var source = device ?? p.Device;
        if (!source.HasValue) return "unknown controller";
        if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || y < 0 || x > width || y > height ||
            (hitTest != null && !hitTest(x, y)))
            return "press outside handle target";
        p.Device = source;
        Active = new Press(cursor, source.Value, time, x, y);
        return null;
    }

    public void Up(uint cursor, uint? device, double time)
    {
        var p = Get(cursor);
        if (!double.IsFinite(time) || Mismatch(p.Device, device)) return;
        p.LastRelease = Math.Max(p.LastRelease, time);
        if (Active?.Cursor == cursor && !Mismatch(Active.Device, device) && time + 0.001 >= Active.Time) Cancel();
    }

    public void ObserveMotion(uint cursor, uint device, float x, float y, double time, double now, bool hoverTarget)
    {
        var p = Get(cursor);
        if (!hoverTarget || !double.IsFinite(time) || now - time > .20 || time > now + .001 ||
            time < p.Entered || time <= p.LastRelease + .001 ||
            !float.IsFinite(x) || !float.IsFinite(y) || x < 0 || y < 0 || x > width || y > height ||
            (hitTest != null && !hitTest(x, y))) return;
        if (!p.Focused) Enter(cursor, device, time);
    }

    public void Cancel() => Active = null;
    public void Reset() { pointers.Clear(); Cancel(); }
    private Pointer Get(uint cursor)
    {
        if (!pointers.TryGetValue(cursor, out var p)) pointers[cursor] = p = new Pointer();
        return p;
    }
    private static bool Mismatch(uint? a, uint? b) => a.HasValue && b.HasValue && a != b;

    public static void Verify()
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var input = new GrabInput();
        Require(input.Down(0, 7, 90, 30, 1, 1) != null, "Unfocused press started a grab.");
        input.Enter(0, 7, 1);
        Require(input.Down(0, 7, 90, 30, 1.01, 1.02) == null, "Focused press rejected.");
        input.Up(0, 7, 1.03); // down+up in one queue must never attach
        Require(input.Active == null, "Queued release left a grab active.");
        Require(input.Down(0, 7, 90, 30, 1.02, 1.04) != null, "Late pre-release press restarted grabbing.");
        input.Leave(0, 7, 1.05);
        Require(input.Down(0, 7, 90, 30, 1.06, 1.07) != null, "Dashboard press restarted after focus leave.");
        input.Enter(0, 7, 2);
        Require(input.Down(0, 7, 90, 30, 2.01, 2.5) != null, "Old queued press accepted.");
        Require(input.Down(0, 8, 90, 30, 2.51, 2.52) != null, "Another controller stole the pointer.");
        Require(input.Down(0, 7, 190, 30, 2.51, 2.52) != null, "Out-of-bounds press accepted.");
        Require(input.Down(0, 7, 90, 30, 2.51, 2.52) == null, "Fresh re-grab rejected.");
        input.Up(0, 8, 2.53);
        Require(input.Active != null, "Another controller released the owner.");
        input.Enter(1, 8, 2.54);
        Require(input.Down(1, 8, 90, 30, 2.55, 2.56) != null, "Second pointer stole an active grab.");
        input.Leave(0, 7, 2.57);
        Require(input.Active == null, "Owner leaving for dashboard did not invalidate queued input.");
        input.Reset(); input.Enter(0, null, 3);
        Require(input.Down(0, null, 90, 30, 3.01, 3.02) != null, "Unknown source guessed a controller.");
        input.Enter(0, 8, 3.03);
        Require(input.Down(0, null, 90, 30, 3.04, 3.05) == null && input.Active.Device == 8, "Focus-bound controller could not resolve mouse source.");
        input.Reset();
        Require(input.Active == null && !input.HasFocus, "Hidden overlay retained a press.");
        // Replay the runtime failure: slot 0 moves from device 8 to device 7
        // without a new focus-enter. The other hand's delayed release is unrelated.
        var pointers = new OverlayPointers();
        var first = pointers.Resolve(0, 8, true).Value;
        input.Enter(first, 8, 4);
        Require(input.Down(first, 8, 90, 30, 4.1, 4.1) == null, "First hand failed.");
        input.Up(first, 8, 4.2);
        var second = pointers.Resolve(0, 7).Value;
        input.ObserveMotion(second, 7, 90, 30, 4.3, 4.3, true);
        Require(input.Active == null, "Motion started a grab.");
        Require(input.Down(second, 7, 90, 30, 4.4, 4.4) == null, "Primary slot handover still needs an outside click.");
        input.Up(pointers.Resolve(0, 8).Value, 8, 4.5);
        Require(input.Active?.Device == 7, "Other hand's release ended the current grab.");
        input.Leave(second, 7, 4.6);
        input.ObserveMotion(second, 7, 90, 30, 4.55, 4.65, true);
        Require(input.Down(second, 7, 90, 30, 4.56, 4.65) != null, "Old motion revived a press after target loss.");
        input.ObserveMotion(second, 7, 90, 30, 4.7, 4.7, true);
        Require(input.Down(second, 7, 90, 30, 4.8, 4.8) == null, "Fresh motion did not restore target focus.");
        input.Up(second, 7, 4.9);
        Require(input.Active == null, "Queued release failed after focus recovery.");
        input.Enter(first, 8, 5);
        Require(input.Down(second, 7, 90, 30, 5.1, 5.1) == null, "New grab required an outside click.");
        input.Leave(first, 8, 5.2); // Other hand moves from handle to keyboard.
        Require(input.Active?.Device == 7, "Other hand leaving the handle cancelled the grab.");
        input.Up(first, 8, 5.3);
        Require(input.Active?.Device == 7, "Typing hand released the grab owner.");
        input.Up(second, 7, 5.4);
        Require(input.Active == null, "Owner could not release while other hand was typing.");
        Console.WriteLine("Grab input checks passed: focus, queued release, stale/out-of-order presses, bounds, controller ownership, target changes, unknown sources, hide/reset.");
    }
}
