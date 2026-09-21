using System.Numerics;

namespace GoBoard.Core;

// The panel pose stays rigid. Only its physical dimensions change, around the center of its main keys.
internal sealed class ResizePose
{
    private readonly Vector2 corner;
    private readonly Vector3 localRay;
    private readonly Vector2 initialHit;
    private readonly float initialScale;
    public float Scale { get; private set; }
    public int SizePercent => Math.Clamp((int)MathF.Round(Scale * 100, MidpointRounding.AwayFromZero), 50, 150);

    private ResizePose(Vector3 ray, Vector2 hit, float scale, bool numpad)
    {
        localRay = ray; initialHit = hit; initialScale = Scale = scale;
        corner = new(OverlayGeometry.RightEdgeInMeters(numpad), -OverlayGeometry.PanelHeightInMeters / 2);
    }

    public static ResizePose Capture(Matrix4x4 panel, Matrix4x4 controller, Vector3 panelPoint, float scale, bool numpad = false)
    {
        if (!float.IsFinite(scale) || scale < .5f || scale > 1.5f ||
            !Matrix4x4.Invert(controller, out var inverse)) return null;
        var direction = Vector3.Transform(panelPoint, panel) - controller.Translation;
        if (!float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() < 1e-8f) return null;
        var localRay = Vector3.TransformNormal(Vector3.Normalize(direction), inverse);
        if (!Intersect(panel, controller, localRay, out var hit)) return null;
        return new(localRay, hit, scale, numpad);
    }

    public float Update(Matrix4x4 panel, Matrix4x4 controller)
    {
        if (Intersect(panel, controller, localRay, out var hit))
            Scale = Math.Clamp(initialScale + Vector2.Dot(hit - initialHit, corner) / corner.LengthSquared(), .5f, 1.5f);
        return Scale;
    }

    private static bool Intersect(Matrix4x4 panel, Matrix4x4 controller, Vector3 localRay, out Vector2 hit)
    {
        hit = default;
        if (!Matrix4x4.Invert(panel, out var inverse)) return false;
        var origin = Vector3.Transform(controller.Translation, inverse);
        var direction = Vector3.TransformNormal(Vector3.TransformNormal(localRay, controller), inverse);
        if (!float.IsFinite(origin.LengthSquared()) || !float.IsFinite(direction.LengthSquared()) ||
            MathF.Abs(direction.Z) < 1e-5f) return false;
        var distance = -origin.Z / direction.Z;
        if (!float.IsFinite(distance) || distance <= 0) return false;
        var point = origin + distance * direction;
        hit = new(point.X, point.Y);
        return float.IsFinite(hit.LengthSquared());
    }
}
