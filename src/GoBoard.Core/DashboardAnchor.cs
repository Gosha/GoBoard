using System.Numerics;

namespace GoBoard.Core;

internal enum DashboardAnchorMode { Pending, Dashboard, Headset }

// Chooses what the keyboard follows while the dashboard is open. On SteamVR PC
// headsets the dashboard bar reports where the user sees the dashboard. While
// streaming to a Steam Frame, the dashboard is drawn on the headset and the PC's
// bar pose can be far from it (reported behind and below the user). If the bar is
// not near and in front of the headset when the dashboard opens, place the
// keyboard in front of the headset instead and keep it there until it closes.
internal sealed class DashboardAnchor
{
    public const double SettleSeconds = 0.5;
    public const float MaxDistance = 2.5f;
    // Keyboard center relative to the eyes in the headset fallback.
    public const float FallbackForward = 0.55f;
    public const float FallbackDrop = 0.40f;
    public static readonly float FallbackTilt = 30 * MathF.PI / 180;

    private double? openedAt;
    private Matrix4x4 headsetParent;
    public DashboardAnchorMode Mode { get; private set; }

    public void Close()
    {
        openedAt = null;
        Mode = DashboardAnchorMode.Pending;
    }

    // Returns the parent pose to follow, or null to stay hidden while undecided.
    public Matrix4x4? Resolve(double now, Matrix4x4 bar, Matrix4x4? head)
    {
        openedAt ??= now;
        if (Mode == DashboardAnchorMode.Pending)
        {
            // Without a headset pose the bar cannot be judged; keep the PC behavior.
            if (!head.HasValue || IsPlausible(bar, head.Value)) Mode = DashboardAnchorMode.Dashboard;
            else if (now - openedAt.Value >= SettleSeconds)
            {
                headsetParent = HeadsetParent(head.Value);
                Mode = DashboardAnchorMode.Headset;
            }
            else return null;
        }
        return Mode == DashboardAnchorMode.Headset ? headsetParent : bar;
    }

    public static bool IsPlausible(Matrix4x4 bar, Matrix4x4 head)
    {
        var offset = bar.Translation - head.Translation;
        if (!float.IsFinite(offset.LengthSquared()) || offset.Length() > MaxDistance) return false;
        return Vector3.Dot(offset with { Y = 0 }, Forward(head)) > 0;
    }

    // Horizontal viewing direction. OpenVR devices look along -Z.
    public static Vector3 Forward(Matrix4x4 head)
    {
        var forward = new Vector3(-head.M31, 0, -head.M33);
        // Looking straight down or up: the top of the headset points forward or back.
        if (forward.LengthSquared() < 0.01f) forward = new Vector3(head.M21, 0, head.M23) * MathF.Sign(head.M32);
        return forward.LengthSquared() < 1e-6f ? -Vector3.UnitZ : Vector3.Normalize(forward);
    }

    // A dashboard-like parent that places the default keyboard offset at a
    // comfortable typing position, tilted toward the eyes.
    public static Matrix4x4 HeadsetParent(Matrix4x4 head)
    {
        var forward = Forward(head);
        var rotation = Matrix4x4.CreateRotationX(-FallbackTilt) * Matrix4x4.CreateRotationY(MathF.Atan2(-forward.X, -forward.Z));
        var center = head.Translation + forward * FallbackForward - Vector3.UnitY * FallbackDrop;
        var parent = rotation;
        parent.Translation = center - Vector3.TransformNormal(RelativePose.DefaultLocal.Translation, rotation);
        return parent;
    }
}
