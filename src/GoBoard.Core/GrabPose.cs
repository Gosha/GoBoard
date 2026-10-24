using System.Numerics;

namespace GoBoard.Core;

// Capture the whole pose, rather than snapping the panel center to the pointer.
internal sealed class GrabPose
{
    public const float StickDeadzone = .2f, MinDistance = .15f, MaxDistance = 3;
    // Full deflection changes the distance threefold per second.
    private static readonly float StickRate = MathF.Log(3);
    private readonly Matrix4x4 offset;
    public Matrix4x4 ControllerOffset => offset;

    public GrabPose(Matrix4x4 panel, Matrix4x4 controller) => offset = Capture(panel, controller);
    private GrabPose(Matrix4x4 offset) => this.offset = offset;

    public Matrix4x4 Update(Matrix4x4 controller) => offset * controller;

    // Push or pull along the controller-to-panel line, keeping orientation.
    // Pulling the stick down brings the panel closer. A changed distance returns
    // a new capture so hosts rebind their controller-relative transforms.
    public GrabPose WithStick(float stick, float seconds)
    {
        var magnitude = MathF.Abs(stick);
        if (!float.IsFinite(stick) || !float.IsFinite(seconds) || seconds <= 0 || magnitude <= StickDeadzone) return this;
        var position = offset.Translation;
        var distance = position.Length();
        if (!float.IsFinite(distance) || distance < .001f) return this;
        var speed = MathF.Sign(stick) * Math.Min(1, (magnitude - StickDeadzone) / (1 - StickDeadzone));
        var next = distance * MathF.Exp(StickRate * speed * Math.Min(seconds, .1f));
        // Never pull past the limits, but do not snap a capture already outside them.
        next = speed < 0 ? Math.Max(next, Math.Min(distance, MinDistance)) : Math.Min(next, Math.Max(distance, MaxDistance));
        if (next == distance) return this;
        var moved = offset;
        moved.Translation = position * (next / distance);
        return new(moved);
    }

    private static Matrix4x4 Capture(Matrix4x4 panel, Matrix4x4 controller)
    {
        if (!Matrix4x4.Invert(controller, out var inverse)) throw new InvalidOperationException("Invalid controller pose.");
        return panel * inverse;
    }

    public static void Verify()
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var panel = Matrix4x4.CreateRotationX(0.2f) * Matrix4x4.CreateTranslation(1, 1, -2);
        var hand = Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(0.3f, 1.2f, -0.5f);
        var grab = new GrabPose(panel, hand);
        Require(RelativePose.Near(grab.Update(hand), panel), "Grab start snapped the panel.");
        Require(RelativePose.Near(grab.ControllerOffset * hand, panel), "Runtime controller attachment changed the captured pose.");
        var delta = Matrix4x4.CreateRotationY(-0.4f) * Matrix4x4.CreateTranslation(0.2f, 0.3f, -0.1f);
        var dragged = grab.Update(hand * delta);
        Require(RelativePose.Near(dragged, panel * delta), "Grab did not preserve controller translation/rotation.");
        Require(RelativePose.Near(OverlayGeometry.GrabFromPanel * grab.ControllerOffset * (hand * delta), OverlayGeometry.GrabFromPanel * dragged), "Handle/controller attachment diverged from the panel.");
        var parent = Matrix4x4.CreateRotationY(0.5f) * Matrix4x4.CreateTranslation(2, 1, -3);
        var follower = new RelativePose();
        follower.SetWorld(parent, dragged);
        Require(RelativePose.Near(follower.Update(parent).World, dragged), "Release snapped the panel.");
        Require(RelativePose.Near(follower.Update(parent * delta).World, dragged * delta), "Released offset did not follow the dashboard.");
        Console.WriteLine("Grab checks passed: no initial snap, controller translation/rotation, release, dashboard-relative offset.");
    }
}

