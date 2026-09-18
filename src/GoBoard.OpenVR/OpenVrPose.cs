using System.Numerics;
using GoBoard.Core;
using Valve.VR;

namespace GoBoard.Vr;

internal static class OpenVrPose
{
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

    public static bool TryRigid(HmdMatrix34_t raw, out Matrix4x4 result) =>
        RelativePose.TryRigid(FromOpenVr(raw), out result);
}
