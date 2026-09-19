namespace GoBoard.Core;

internal interface IKeySink
{
    void Down(ushort scan);
    void Up(ushort scan);
    void Stroke(ushort scan, ushort[] chord)
    {
        var pressed = new List<ushort>();
        Exception failure = null;
        try
        {
            foreach (var modifier in chord) { Down(modifier); pressed.Add(modifier); }
            Down(scan); pressed.Add(scan);
        }
        catch (Exception ex) { failure = ex; }
        for (var i = pressed.Count - 1; i >= 0; i--)
            try { Up(pressed[i]); } catch (Exception ex) { failure ??= ex; }
        if (failure != null) throw failure;
    }
}

internal enum ModifierMode { Idle, OneShot, Locked }
internal readonly record struct KeyboardVisualPointer(uint Cursor, bool Focused, float X, float Y,
    KeyboardKey Held, long PressSequence, KeyboardKey LastPressed, float PressX, float PressY);

// Input geometry and pointer ownership are independent of OpenVR and Windows.
internal sealed class KeyboardState(IKeySink sink)
{
    private sealed class Pointer
    {
        public uint? Device;
        public bool Focused, Down;
        public KeyboardKey Hover, Held;
        public double Entered, PressedAt, Released = double.NegativeInfinity;
        public float X = float.NaN, Y = float.NaN, PressX, PressY;
        public long PressSequence;
        public KeyboardKey LastPressed;
    }
    private sealed class HeldKey(KeyboardKey key, ushort[] modifiers, double now)
    {
        public readonly KeyboardKey Key = key;
        public readonly ushort[] Modifiers = modifiers;
        public int Owners = 1;
        public double RepeatAt = now + 0.45;
    }
    private readonly Dictionary<uint, Pointer> pointers = new();
    private readonly Dictionary<ushort, HeldKey> held = new();
    private readonly Dictionary<ushort, ModifierMode> modifiers = new();
    private uint? grabOwner;
    private bool resizing;
    private double resizeWatermark = double.NegativeInfinity;
    public void SetResizing(bool value, double now)
    {
        if (resizing == value) return;
        resizing = value;
        resizeWatermark = now;
        Cancel(now);
    }
    private bool ResizeBlocks(double time) => resizing || time <= resizeWatermark + .001;
    private readonly Dictionary<uint, double> grabWatermarks = new();
    public void SetGrabOwner(uint? device, double now)
    {
        if (grabOwner == device) return;
        // Only the grabbing hand loses keyboard capture. Preserve the other
        // hand's held/repeating key and shared logical modifier modes.
        if (grabOwner.HasValue) { LoseDevice(grabOwner.Value, now); grabWatermarks[grabOwner.Value] = now; }
        grabOwner = device;
        if (device.HasValue) { LoseDevice(device.Value, now); grabWatermarks[device.Value] = now; }
    }
    public int Revision { get; private set; }
    // Hover is drawn separately when pointer effects are enabled.
    public int ContentRevision { get; private set; }
    public int VisualRevision { get; private set; }
    public int CancellationRevision { get; private set; }
    public IEnumerable<KeyboardVisualPointer> VisualPointers => pointers.Select(p => new KeyboardVisualPointer(
        p.Key, p.Value.Focused, p.Value.X, p.Value.Y, p.Value.Held, p.Value.PressSequence,
        p.Value.LastPressed, p.Value.PressX, p.Value.PressY));
    public bool Shift => Mode(0x2a) != ModifierMode.Idle;
    public bool AltGr => Mode(0xe038) != ModifierMode.Idle;
    public WindowsLayout Layout { get; private set; } = new((nint)WindowsLayout.UsHandle);
    public void SetLayout(WindowsLayout layout, double now)
    {
        if (ReferenceEquals(Layout, layout)) return;
        Cancel(now, clearFocus: false);
        Layout = layout;
        // A layout change can alter geometry; discard hover from the old key list.
        foreach (var p in pointers.Values) p.Hover = null;
        Revision++; ContentRevision++;
    }
    public ModifierMode Mode(ushort scan) => modifiers.GetValueOrDefault(scan);
    public bool HasHeldKeys => held.Count > 0;
    public bool Pressed(KeyboardKey key) => held.ContainsKey(key.Scan);
    public bool Hovered(KeyboardKey key) => pointers.Values.Any(p => p.Hover == key);
    public IEnumerable<uint> FocusedDevices => pointers.Values.Where(p => p.Focused && p.Device.HasValue).Select(p => p.Device.Value).Distinct().ToArray();

    public void Enter(uint cursor, uint? device, double time)
    {
        if (ResizeBlocks(time)) return;
        if (device.HasValue && (device == grabOwner || PredatesGrabRelease(device.Value, time))) return;
        var p = Get(cursor);
        if (time < p.Entered || time < p.Released) return;
        if (p.Device != device) { Release(p); p.Down = false; Hover(p, null); }
        p.Device = device;
        if (!p.Focused) VisualRevision++;
        p.Focused = true;
        p.Entered = time;
    }

    public void Leave(uint cursor, uint? device, double time)
    {
        var p = Get(cursor);
        if (Mismatch(p.Device, device) || time < p.Entered) return;
        Release(p);
        if (p.Focused) VisualRevision++;
        p.Focused = false;
        p.Down = false;
        p.Released = Math.Max(p.Released, time);
        Hover(p, null);
    }

    public void Move(uint cursor, uint? device, float x, float y)
    {
        if (resizing) return;
        var p = Get(cursor);
        if (!p.Focused || Mismatch(p.Device, device)) return;
        Position(p, x, y);
        var key = KeyboardLayout.HitOpenVr(x, y, Layout.Keys);
        Hover(p, key);
        // Leaving a captured key cancels it; sliding while held never types a new key.
        if (p.Held != null && p.Held != key) Release(p);
    }

    public void ObserveMotion(uint cursor, uint device, float x, float y, double time, double now, bool hoverTarget)
    {
        if (ResizeBlocks(time)) return;
        if (device == grabOwner || PredatesGrabRelease(device, time)) return;
        var p = Get(cursor);
        if (!hoverTarget || !double.IsFinite(time) || now - time > .20 || time > now + .001 ||
            time < p.Entered || time <= p.Released + .001 ||
            !float.IsFinite(x) || !float.IsFinite(y)) return;
        if (x < 0 || y < 0 || x >= OverlayGeometry.PanelWidth || y >= OverlayGeometry.PanelHeight)
        {
            Move(cursor, device, x, y); // Existing capture still cancels on key departure.
            return;
        }
        // A fresh controller-identified motion delivered to this live overlay
        // can restore hover after cancellation or a primary-laser handover.
        // Motion never presses a key and cannot revive input older than a leave/up.
        if (!p.Focused) Enter(cursor, device, time);
        Move(cursor, device, x, y);
    }

    public bool Press(uint cursor, uint? device, float x, float y, double time, double now)
    {
        if (ResizeBlocks(time)) return false;
        var p = Get(cursor);
        var source = device ?? p.Device;
        if (source.HasValue && (source == grabOwner || PredatesGrabRelease(source.Value, time))) return false;
        if (!p.Focused || p.Down || Mismatch(p.Device, device) || !(device ?? p.Device).HasValue ||
            !double.IsFinite(time) || now - time > 0.20 || time > now + 0.001 || time < p.Entered || time <= p.Released + 0.001) return false;
        var key = KeyboardLayout.HitOpenVr(x, y, Layout.Keys);
        if (key == null) return false;
        p.Device ??= device;
        Hover(p, key);
        if (key.IsModifier)
        {
            if (key.Id == "Win")
            {
                if (Mode(key.Scan) == ModifierMode.Idle) modifiers[key.Scan] = ModifierMode.OneShot;
                else
                {
                    // A second click is a bare Windows-key tap, not a lock.
                    // Clear logical modifiers only after successful delivery.
                    Stroke(key.Scan, []);
                    modifiers.Clear();
                }
            }
            else modifiers[key.Scan] = (ModifierMode)(((int)Mode(key.Scan) + 1) % 3);
        }
        else if (held.TryGetValue(key.Scan, out var shared)) shared.Owners++;
        else
        {
            // The IME button is a standalone mode action. Keep armed modifiers
            // for the next typed key, but never apply them to the toggle.
            ushort[] modes = key.Scan == KeyboardLayout.ImeToggleKey ? [] :
                new ushort[] { 0xe05b, 0x1d, 0x38, 0xe038, 0x2a }.Where(scan => Mode(scan) != ModifierMode.Idle).ToArray();
            // Layouts with AltGr use Ctrl+right Alt. Keep consumption tied to the
            // logical modifier modes, not the extra synthetic Ctrl event.
            ushort[] chord = key.Scan == KeyboardLayout.ImeToggleKey ? [] : new ushort[] { 0xe05b, 0x1d, 0x38, 0xe038, 0x2a }
                .Where(scan => modes.Contains(scan) || (scan == 0x1d && Layout.HasAltGr && AltGr)).ToArray();
            Stroke(key.Scan, chord);
            held.Add(key.Scan, new HeldKey(key, chord, now));
            // Consume together on the next ordinary key, not on another modifier.
            foreach (var scan in modes)
                if (Mode(scan) == ModifierMode.OneShot) modifiers[scan] = ModifierMode.Idle;
        }
        p.Down = true;
        p.PressedAt = time;
        p.Held = key;
        Position(p, x, y);
        p.PressX = p.X; p.PressY = p.Y; p.LastPressed = key; p.PressSequence++;
        Revision++; ContentRevision++;
        return true;
    }

    public bool Up(uint cursor, uint? device, double time)
    {
        var p = Get(cursor);
        if (Mismatch(p.Device, device) || time < p.Entered || time < p.Released || (p.Down && time + 0.001 < p.PressedAt)) return false;
        var released = p.Down;
        Release(p);
        p.Down = false;
        p.Released = time;
        return released;
    }

    public void Tick(double now)
    {
        foreach (var entry in held.Values)
        {
            if (!entry.Key.Repeat || now < entry.RepeatAt) continue;
            Stroke(entry.Key.Scan, entry.Modifiers);
            entry.RepeatAt = now + 0.06; // Never burst to catch up after a stalled frame.
        }
    }

    public void LoseDevice(uint device, double now)
    {
        foreach (var p in pointers.Values.Where(p => p.Device == device))
        {
            VisualRevision++;
            Release(p); p.Focused = false; p.Down = false; p.Released = now; Hover(p, null);
        }
    }

    public void Cancel(double now, bool clearFocus = true)
    {
        CancellationRevision++;
        held.Clear();
        modifiers.Clear();
        foreach (var p in pointers.Values)
        {
            p.Held = null; p.Down = false; p.Released = now;
            if (clearFocus) { p.Focused = false; p.Hover = null; }
        }
        Revision++; ContentRevision++;
    }

    private void Release(Pointer p)
    {
        if (p.Held == null) return;
        if (!p.Held.IsModifier)
        {
            var scan = p.Held.Scan;
            if (held[scan].Owners == 1) held.Remove(scan);
            else held[scan].Owners--;
        }
        p.Held = null;
        Revision++; ContentRevision++;
    }
    private void Hover(Pointer p, KeyboardKey key)
    {
        if (p.Hover == key) return;
        p.Hover = key; Revision++;
    }
    private void Position(Pointer p, float x, float y)
    {
        y = OverlayGeometry.PanelHeight - y;
        if (p.X == x && p.Y == y) return;
        p.X = x; p.Y = y; VisualRevision++;
    }
    private Pointer Get(uint cursor)
    {
        if (!pointers.TryGetValue(cursor, out var p)) pointers[cursor] = p = new Pointer();
        return p;
    }
    private static bool Mismatch(uint? a, uint? b) => a.HasValue && b.HasValue && a != b;
    private bool PredatesGrabRelease(uint device, double time)
        => grabWatermarks.TryGetValue(device, out var watermark) && time <= watermark + .001;

    private void Stroke(ushort scan, ushort[] chord)
    {
        // Modifiers are logical keyboard modes, not globally held Windows keys.
        // Balanced strokes prevent a one-shot leaking into an overlapping second
        // pointer's key and prevent locked Alt/Ctrl affecting dashboard clicks.
        sink.Stroke(scan, chord);
    }
}
