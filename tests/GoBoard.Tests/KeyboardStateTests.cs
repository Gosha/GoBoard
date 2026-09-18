using GoBoard.Core;
using GoBoard.Platform.Windows;
using GoBoard.Vr;
using System.Numerics;
using Xunit;

namespace GoBoard.Tests;

public sealed class KeyboardStateTests
{
    private sealed class Sink : IKeySink
    {
        public readonly List<(ushort Scan, bool Down)> Events = new();
        public ushort? FailDown;
        public void Down(ushort scan)
        {
            if (scan == FailDown) throw new InvalidOperationException("Simulated injection failure");
            Events.Add((scan, true));
        }
        public void Up(ushort scan) => Events.Add((scan, false));
    }

    [Fact]
    public void VerifiedInteractions()
    {
        static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        var sink = new Sink(); var state = new KeyboardState(sink);
        KeyboardKey Key(string id) => state.Layout.Keys.Single(k => k.Id == id);
        (float X, float Y) Point(string id) { var b = Key(id).Bounds; return (b.X + b.Width / 2, OverlayGeometry.PanelHeight - b.Y - b.Height / 2); }
        bool Press(uint cursor, uint device, string id, double t) { var p = Point(id); return state.Press(cursor, device, p.X, p.Y, t, t); }
        void Move(uint cursor, uint device, string id) { var p = Point(id); state.Move(cursor, device, p.X, p.Y); }

        Require(KeyboardLayout.Keys.Count == 86 && KeyboardLayout.Keys.Select(k => k.Id).Distinct().Count() == 86, "Keyboard layout lost or duplicated a key.");
        Require(KeyboardLayout.Keys.All(k => k.Id != "Start") && KeyboardLayout.Keys.Select(k => k.Scan).Distinct().Count() == 84, "Unexpected physical key count (Shift/Ctrl each have two buttons).");
        foreach (var k in KeyboardLayout.Keys)
        {
            var p = Point(k.Id);
            Require(KeyboardLayout.HitOpenVr(p.X, p.Y) == k, $"Pointer orientation misses {k.Id}.");
        }
        var backquote = Key("Backquote").Bounds;
        Require(KeyboardLayout.Hit(backquote.X + backquote.Width + 1, backquote.Y + backquote.Height / 2) == null && KeyboardLayout.Hit(float.NaN, 60) == null, "Gap or invalid coordinate hit a key.");
        Require(!Press(0, 7, "a", 1), "Unfocused pointer typed.");
        state.Enter(0, 7, 1); state.Enter(1, 8, 1);
        Require(Press(0, 7, "Shift", 1.1), "Shift did not press.");
        Require(state.Mode(0x2a) == ModifierMode.OneShot && sink.Events.Count == 0, "First click did not arm a logical one-shot.");
        Require(Press(1, 8, "a", 1.2), "Second pointer could not consume Shift.");
        state.Up(1, 8, 1.3);
        Require(!state.Shift && sink.Events.SequenceEqual(new (ushort, bool)[] { (0x2a, true), (0x1e, true), (0x1e, false), (0x2a, false) }), "One-shot Shift did not send a balanced chord and clear.");
        state.Leave(0, 7, 1.4);
        Require(!state.Shift && sink.Events.Last() == (0x2a, false), "Focus loss left Shift down.");

        state.Enter(0, 7, 2);
        Require(Press(0, 7, "a", 2.1), "Key press rejected.");
        Require(!state.Up(0, 8, 2.2), "Wrong device reported a key-up click.");
        Require(state.Pressed(Key("a")), "Wrong device released the key.");
        Move(0, 7, "s");
        Require(!state.HasHeldKeys && state.Hovered(Key("s")), "Leaving key did not release it.");
        Require(!Press(0, 7, "s", 2.3), "Sliding a held pointer typed another key.");
        Require(state.Up(0, 7, 2.4), "Valid release did not report a key-up click.");
        Require(!state.Up(0, 7, 2.41), "Duplicate release reported a key-up click.");
        Require(Press(0, 7, "s", 2.5), "Fresh press after cancellation rejected.");
        state.Cancel(2.6);
        Require(!state.HasHeldKeys, "Hiding did not release all keys.");

        sink.Events.Clear(); state.Enter(0, 7, 3); state.Enter(1, 8, 3);
        Press(0, 7, "Backspace", 3.1); Press(1, 8, "Backspace", 3.2);
        Require(sink.Events.Count == 2, "Two pointers duplicated initial stroke.");
        state.Up(0, 7, 3.3);
        Require(sink.Events.Count == 2 && state.HasHeldKeys, "First owner released a shared key.");
        state.Tick(3.54); Require(sink.Events.Count == 2, "Repeat began too early.");
        state.Tick(3.56); Require(sink.Events.Count == 4, "Held Backspace did not repeat.");
        state.Tick(5); Require(sink.Events.Count == 6, "Delayed repeat burst instead of one stroke.");
        state.LoseDevice(8, 5.1);
        Require(!state.HasHeldKeys && sink.Events.Last() == (0x0e, false), "Device loss left key down.");
        state.Tick(6); Require(sink.Events.Count == 6, "Repeat continued after release.");

        state.Enter(0, 7, 7);
        var a = Point("a");
        Require(!state.Press(0, 7, a.X, a.Y, 7.01, 7.5), "Stale down typed.");
        Press(0, 7, "a", 7.6); state.Up(0, 7, 7.7);
        Require(!state.Press(0, 7, a.X, a.Y, 7.65, 7.8), "Old pre-release down typed.");
        Press(0, 7, "a", 7.9); state.Up(0, 7, 7.75);
        Require(state.HasHeldKeys, "Late old up released a newer press.");
        state.Cancel(8, clearFocus: false);
        Require(!state.HasHeldKeys && !state.Shift, "Target change left a chord held.");
        double time = 9;
        state.Enter(0, 7, time); state.Enter(1, 8, time);
        void Tap(string id) { time += .1; Require(Press(0, 7, id, time), "Modifier test press rejected."); state.Up(0, 7, time + .01); }
        sink.Events.Clear();
        Tap("Ctrl"); Tap("Alt");
        Require(state.Mode(0x1d) == ModifierMode.OneShot && state.Mode(0x38) == ModifierMode.OneShot, "Modifiers consumed each other.");
        Tap("s");
        Require(sink.Events.SequenceEqual(new (ushort, bool)[] { (0x1d, true), (0x38, true), (0x1f, true), (0x1f, false), (0x38, false), (0x1d, false) }), "Ctrl+Alt+S chord incorrect.");
        Require(state.Mode(0x1d) == ModifierMode.Idle && state.Mode(0x38) == ModifierMode.Idle, "Stacked one-shots did not clear together.");
        sink.Events.Clear();
        Tap("Ctrl"); Tap("Ctrl"); Tap("s"); Tap("s");
        var ctrlS = new (ushort, bool)[] { (0x1d, true), (0x1f, true), (0x1f, false), (0x1d, false) };
        Require(state.Mode(0x1d) == ModifierMode.Locked && sink.Events.SequenceEqual(ctrlS.Concat(ctrlS)), "Locked Ctrl did not persist for two strokes.");
        Tap("Ctrl"); Require(state.Mode(0x1d) == ModifierMode.Idle, "Third click did not unlock.");
        Tap("Shift"); sink.Events.Clear();
        time += .1; Press(0, 7, "a", time);
        time += .1; Press(1, 8, "b", time);
        Require(sink.Events.SequenceEqual(new (ushort, bool)[] { (0x2a, true), (0x1e, true), (0x1e, false), (0x2a, false), (0x30, true), (0x30, false) }), "One-shot leaked into overlapping second-pointer key.");
        state.Cancel(++time, clearFocus: false);
        Tap("Alt"); Tap("Alt"); Tap("Shift"); state.Cancel(++time);
        Require(!state.Shift && state.Mode(0x38) == ModifierMode.Idle, "Cancellation did not clear locked and one-shot modes.");
        state.Enter(0, 7, time); Tap("Ctrl"); sink.Events.Clear(); sink.FailDown = 0x1f;
        bool failed = false;
        try { Tap("s"); } catch (InvalidOperationException ex) when (ex.Message == "Simulated injection failure") { failed = true; }
        Require(failed && sink.Events.SequenceEqual(new (ushort, bool)[] { (0x1d, true), (0x1d, false) }), "Failed chord left a modifier down.");
        Require(!state.HasHeldKeys && state.Mode(0x1d) == ModifierMode.OneShot, "Failed stroke consumed the next-key modifier or started repeat.");
        sink.FailDown = null; state.Cancel(++time, clearFocus: false);
        void Chord(string[] ids, params ushort[] scans)
        {
            sink.Events.Clear(); foreach (var id in ids) Tap(id);
            var expected = scans.Select(s => (s, true)).Concat(scans.Reverse().Select(s => (s, false)));
            Require(sink.Events.SequenceEqual(expected), $"Incorrect chord: {string.Join('+', ids)}");
        }
        Chord(["Ctrl", "a"], 0x1d, 0x1e);
        Chord(["Ctrl", "Shift", "f"], 0x1d, 0x2a, 0x21);
        Chord(["Ctrl", "Left"], 0x1d, 0xe04b);
        Chord(["Win", "r"], 0xe05b, 0x13);
        Chord(["Win", "Shift", "Left"], 0xe05b, 0x2a, 0xe04b);
        sink.Events.Clear(); Tap("Win");
        Require(state.Mode(0xe05b) == ModifierMode.OneShot && sink.Events.Count == 0, "First Win click sent input instead of arming.");
        Tap("Win");
        Require(state.Mode(0xe05b) == ModifierMode.Idle && sink.Events.SequenceEqual(new (ushort, bool)[] { (0xe05b, true), (0xe05b, false) }), "Second Win click did not tap and clear.");
        var afterTap = sink.Events.Count;
        state.Tick(time + 1);
        Require(sink.Events.Count == afterTap, "Windows tap repeated.");
        Tap("Win");
        Require(state.Mode(0xe05b) == ModifierMode.OneShot && sink.Events.Count == afterTap, "Third Win click did not re-arm.");
        state.Cancel(++time, clearFocus: false);
        Chord(["Ctrl", "Shift", "Win", "Win"], 0xe05b);
        Require(state.Mode(0xe05b) == ModifierMode.Idle && state.Mode(0x1d) == ModifierMode.Idle && !state.Shift, "Standalone Win tap left modifiers armed.");
        Tap("Win"); sink.FailDown = 0xe05b; sink.Events.Clear();
        failed = false;
        try { Tap("Win"); } catch (InvalidOperationException ex) when (ex.Message == "Simulated injection failure") { failed = true; }
        Require(failed && state.Mode(0xe05b) == ModifierMode.OneShot && sink.Events.Count == 0, "Failed Windows tap consumed its arm or left input held.");
        sink.FailDown = null; Tap("Win");
        Require(state.Mode(0xe05b) == ModifierMode.Idle, "Windows tap could not retry after failure.");
        for (var i = 1; i <= 12; i++) Chord([$"F{i}"], (ushort)(i <= 10 ? 0x3a + i : 0x57 + i - 11));
        Require(WindowsKeyboard.ScanFlags(0xe04b, false) == 9 && WindowsKeyboard.ScanFlags(0xe05b, true) == 11 && WindowsKeyboard.ScanFlags(0x3b, false) == 8, "Extended scan-code flags incorrect.");
        state.Cancel(++time); sink.Events.Clear();
        var pointers = new OverlayPointers();
        var handA = pointers.Resolve(0, 7, true).Value;
        var handB = pointers.Resolve(0, 8).Value;
        time += .1; state.Enter(handA, 7, time);
        time += .1; var b = Point("b"); state.ObserveMotion(handB, 8, b.X, b.Y, time, time, true);
        Require(sink.Events.Count == 0, "Focus recovery typed without a down.");
        time += .1; Require(Press(handA, 7, "a", time), "First hand failed after slot routing.");
        time += .1; Require(Press(handB, 8, "b", time), "Second hand needed an exit/re-enter after slot handover.");
        state.Up(pointers.Resolve(1, 7).Value, 7, time + .01);
        Require(!state.Pressed(Key("a")) && state.Pressed(Key("b")), "A slot change released the wrong hand.");
        time += .1; state.Up(pointers.Resolve(0, 8).Value, 8, time);
        time += .1; state.Cancel(time, clearFocus: false); // Grab/desktop-target pause.
        time += .1; Require(Press(handB, 8, "b", time), "Resume required another focus-enter.");
        time += .1; state.Leave(handB, 8, time);
        state.ObserveMotion(handB, 8, b.X, b.Y, time - .05, time + .01, true);
        Require(!Press(handB, 8, "b", time + .02), "Old motion revived focus after leave.");
        time += .1; state.ObserveMotion(handB, 8, b.X, b.Y, time, time, false);
        Require(!Press(handB, 8, "b", time + .01), "Motion on an inactive target restored focus.");
        time += .1; state.ObserveMotion(handB, 8, b.X, b.Y, time, time, true);
        Require(Press(handB, 8, "b", time + .01), "Fresh motion failed to restore focus.");
        state.Cancel(++time);
        // A captured grab suppresses only its controller. The opposite hand
        // keeps hover, repeat and armed modifiers throughout movement/release.
        foreach (var owner in new uint[] { 7, 8 })
        {
            var other = owner == 7 ? 8u : 7u;
            state.Cancel(++time); sink.Events.Clear();
            state.Enter(owner, owner, time); state.Enter(other, other, time);
            Require(Press(other, other, "Shift", time += .1), "Could not arm Shift before grabbing.");
            state.Up(other, other, time += .1);
            Require(Press(owner, owner, "a", time += .1), "Initial owner capture failed.");
            Require(Press(other, other, "Backspace", time += .1), "Initial other-hand repeat failed.");
            state.SetGrabOwner(owner, time += .1);
            Require(!state.Pressed(Key("a")) && state.Pressed(Key("Backspace")), "Grab cancelled both hands or retained its own key.");
            var count = sink.Events.Count;
            state.Tick(time += .5);
            Require(sink.Events.Count > count, "Other-hand repeat stopped during grab.");
            state.Up(other, other, time += .1);
            Require(Press(other, other, "Shift", time += .1), "Other hand could not arm Shift during grab.");
            state.Up(other, other, time += .1);
            state.Enter(owner, owner, time += .1);
            state.ObserveMotion(owner, owner, b.X, b.Y, time, time, true);
            Require(!Press(owner, owner, "b", time + .01), "Grabbing hand typed through its captured trigger.");
            Require(Press(other, other, "b", time += .1) && !state.Shift, "Other hand could not use one-shot Shift during grab.");
            state.Up(other, other, time += .1);
            var oldGrabTime = time;
            state.SetGrabOwner(null, time += .1);
            Require(Press(other, other, "a", time += .1), "Releasing grab interrupted other hand.");
            state.Up(other, other, time += .1);
            state.Enter(owner, owner, oldGrabTime);
            state.ObserveMotion(owner, owner, b.X, b.Y, oldGrabTime, time, true);
            Require(!Press(owner, owner, "b", oldGrabTime), "Queued keyboard input from the grab typed after release.");
            state.ObserveMotion(owner, owner, b.X, b.Y, time += .1, time, true);
            Require(Press(owner, owner, "b", time += .1), "Released hand could not resume typing with fresh motion.");
            state.Up(owner, owner, time += .1);
        }
        state.Cancel(++time);
        state.SetLayout(new WindowsLayout((nint)WindowsLayout.SwedishHandle), ++time);
        Require(state.Layout.Keys.Count == 87 && state.Layout.Keys.All(k => k.Id != "Start") && state.Layout.Keys.Single(k => k.Id == "Iso").Scan == 0x56, "Swedish ISO geometry or Start removal incorrect.");
        foreach (var k in state.Layout.Keys)
        {
            var p = Point(k.Id);
            Require(KeyboardLayout.HitOpenVr(p.X, p.Y, state.Layout.Keys) == k, $"Swedish hit geometry misses {k.Id}.");
        }
        state.Enter(0, 7, time);
        Chord(["AltGr", "2"], 0x1d, 0xe038, 0x03);
        Tap("AltGr"); Tap("AltGr"); Tap("Shift");
        time += .1; Press(0, 7, "a", time);
        state.SetLayout(new WindowsLayout((nint)WindowsLayout.UsHandle), ++time);
        Require(!state.HasHeldKeys && !state.AltGr && !state.Shift && state.Layout.Keys.All(k => k.Id != "Iso"), "Layout switch retained an old capture/modifier/ISO key.");
        state.SetLayout(new WindowsLayout((nint)0x08090809), ++time);
        Require(!state.Layout.Supported && !Press(0, 7, "a", time + .1), "Unsupported layout silently typed US keys.");
        Console.WriteLine("Two-hand focus checks passed: reused cursor slots, slot changes during a press, late other-hand release, pause/resume, stale-motion rejection and live-motion recovery.");
        Console.WriteLine("Grab/typing checks passed in both hand directions: owner-only capture, other-hand typing/modifiers/repeat, release and stale queued input rejection.");
        Console.WriteLine("Keyboard checks passed: 86 US/87 Swedish buttons, Win arm/tap/re-arm and failure cleanup, ISO geometry, AltGr chords, layout-change cancellation, unsupported-layout guard, shortcuts, repeat and pointer ownership.");
    }

    [Fact]
    public void DashboardRelativePose() => RelativePose.Verify();

    [Fact]
    public void ControllerRelativeGrabPose() => GrabPose.Verify();

    [Fact]
    public void GrabOwnershipAndEventOrdering() => GrabInput.Verify();

    [Fact]
    public void CursorSlotsDoNotBecomeControllerIdentity() => OverlayPointers.Verify();

    [Fact]
    public void OpenVrMatrixConventionRoundTrips()
    {
        var expected = Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(1, 2, -3);
        Assert.True(RelativePose.Near(OpenVrPose.FromOpenVr(OpenVrPose.ToOpenVr(expected)), expected));
    }

    [Fact]
    public void KeyDownAndUpAudioAreDistinctWithoutPlayback()
    {
        var down = KeyAudio.CreateClick(released: false);
        var up = KeyAudio.CreateClick(released: true);
        Assert.Equal(5804, down.Length);
        Assert.Equal(3404, up.Length);
        Assert.NotEqual(down, up);
    }
}
