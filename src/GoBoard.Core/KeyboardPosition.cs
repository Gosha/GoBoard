using System.Numerics;

namespace GoBoard.Core;

// Explicit scalar properties keep settings independent of platform types and
// System.Numerics' field-only JSON representation. VR poses are dashboard-relative.
internal sealed record VrKeyboardPosition(float X, float Y, float Z, float Qx, float Qy, float Qz, float Qw)
{
    public Matrix4x4 ToMatrix() => Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new(Qx, Qy, Qz, Qw))) *
        Matrix4x4.CreateTranslation(X, Y, Z);

    public VrKeyboardPosition Normalize()
    {
        var rotation = new Quaternion(Qx, Qy, Qz, Qw);
        return float.IsFinite(new Vector3(X, Y, Z).LengthSquared()) &&
            float.IsFinite(rotation.LengthSquared()) && rotation.LengthSquared() > .0001f ? this : null;
    }

    public static VrKeyboardPosition FromMatrix(Matrix4x4 pose)
    {
        var rotation = Quaternion.CreateFromRotationMatrix(pose);
        return new(pose.M41, pose.M42, pose.M43, rotation.X, rotation.Y, rotation.Z, rotation.W);
    }
}

internal sealed record DesktopKeyboardPosition(int X, int Y)
{
    // Leave ample room for multi-monitor layouts without overflowing window bounds.
    public DesktopKeyboardPosition Normalize() => X is >= -1000000 and <= 1000000 &&
        Y is >= -1000000 and <= 1000000 ? this : null;
}

// Save only a settled placement or a newly enabled preference, never every
// animation frame. A reset/disable from another process wins over a stale move.
internal sealed class PositionMemory<T>(SettingsStore store, Func<BoardSettings, T> read,
    Func<BoardSettings, T, BoardSettings> write) where T : class
{
    private T lastPosition = read(store.Current);
    private bool enabled = store.Current.RememberPosition;

    public bool Update(T position, bool moving, BoardSettings applied)
    {
        if (moving) return true;
        var changed = !EqualityComparer<T>.Default.Equals(position, lastPosition) || enabled != applied.RememberPosition;
        lastPosition = position;
        enabled = applied.RememberPosition;
        if (!enabled || !changed) return true;
        var expectedReset = applied.PositionResetId;
        return store.Update(s => s.RememberPosition && s.PositionResetId == expectedReset ? write(s, position) : s);
    }
}
