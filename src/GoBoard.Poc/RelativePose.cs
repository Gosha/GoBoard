using System.Numerics;
using Valve.VR;

namespace GoBoard.Poc;

// System.Numerics uses row vectors: world = local * parent.
internal sealed class RelativePose
{
    private Matrix4x4 local = Matrix4x4.CreateTranslation(0, -0.30f, 0.10f);
    private Matrix4x4? lastWorld;


    public void SetWorld(Matrix4x4 parent, Matrix4x4 world)
    {
        if (!Matrix4x4.Invert(parent, out var inverse)) throw new InvalidOperationException("Invalid dashboard pose.");
        local = world * inverse;
    }

    public (Matrix4x4 World, bool Write) Update(Matrix4x4 parent)
    {
        // GoBoard owns movement explicitly; runtime readback can lag and must not
        // be interpreted as a new grab or used to overwrite the stored offset.
        var world = local * parent;
        var write = !lastWorld.HasValue || !Near(world, lastWorld.Value);
        if (write) lastWorld = world;
        return (world, write);
    }

    public static bool Near(Matrix4x4 a, Matrix4x4 b, float tolerance = 0.0005f)
    {
        return Vector3.Distance(a.Translation, b.Translation) < tolerance &&
            Vector3.Distance(new(a.M11, a.M12, a.M13), new(b.M11, b.M12, b.M13)) < tolerance &&
            Vector3.Distance(new(a.M21, a.M22, a.M23), new(b.M21, b.M22, b.M23)) < tolerance &&
            Vector3.Distance(new(a.M31, a.M32, a.M33), new(b.M31, b.M32, b.M33)) < tolerance;
    }

    public static Matrix4x4 FromOpenVr(HmdMatrix34_t m) => new(
        m.m0, m.m4, m.m8, 0,
        m.m1, m.m5, m.m9, 0,
        m.m2, m.m6, m.m10, 0,
        m.m3, m.m7, m.m11, 1);

    public static HmdMatrix34_t ToOpenVr(Matrix4x4 m) => new()
    {
        m0 = m.M11, m1 = m.M21, m2 = m.M31, m3 = m.M41,
        m4 = m.M12, m5 = m.M22, m6 = m.M32, m7 = m.M42,
        m8 = m.M13, m9 = m.M23, m10 = m.M33, m11 = m.M43
    };

    public static bool TryRigid(HmdMatrix34_t raw, out Matrix4x4 result)
    {
        var matrix = FromOpenVr(raw);
        result = default;
        if (!Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation) ||
            !float.IsFinite(scale.LengthSquared()) || MathF.Min(scale.X, MathF.Min(scale.Y, scale.Z)) < 0.0001f ||
            !float.IsFinite(rotation.LengthSquared()) || !float.IsFinite(translation.LengthSquared())) return false;
        // Dashboard scale must not resize our panel or distort its orientation.
        result = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation)) * Matrix4x4.CreateTranslation(translation);
        return true;
    }

    public static void Verify()
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var model = new RelativePose();
        var parent = Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(1, 1.5f, -2);
        var first = model.Update(parent);
        Require(first.Write, "Initial pose was not placed.");
        Require(Near(FromOpenVr(ToOpenVr(first.World)), first.World), "OpenVR matrix conversion failed.");
        Require(!model.Update(parent).Write, "Idle pose should not be rewritten.");
        var nextParent = Matrix4x4.CreateRotationY(-0.4f) * Matrix4x4.CreateTranslation(-2, 1, -1);
        var follow = model.Update(nextParent);
        Matrix4x4.Invert(parent, out var inverse);
        Require(follow.Write && Near(follow.World, first.World * inverse * nextParent), "Dashboard motion failed.");
        var dragged = Matrix4x4.CreateRotationX(0.2f) * Matrix4x4.CreateTranslation(0.8f, 0.3f, -0.2f) * nextParent;
        model.SetWorld(nextParent, dragged);
        var grab = model.Update(nextParent);
        Require(grab.Write && Near(grab.World, dragged), "Explicit grab was overridden.");
        Require(!model.Update(nextParent).Write, "Released panel snapped back.");
        Matrix4x4.Invert(nextParent, out inverse);
        Require(Near(model.Update(parent).World, dragged * inverse * parent), "New offset did not follow dashboard.");
        var scaled = ToOpenVr(Matrix4x4.CreateScale(2) * parent);
        Require(TryRigid(scaled, out var rigid) && Near(rigid, parent), "Dashboard scaling changed panel pose.");
        Require(!TryRigid(default, out _), "Invalid anchor was accepted.");
        Console.WriteLine("Pose checks passed: coordinate conversion, dashboard translation/rotation, explicit movement, retained offset, scale removal, invalid anchor.");
    }
}
