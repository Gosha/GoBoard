using System.Numerics;

namespace GoBoard.Core;

// Keep placement in units of the parent's 100% size. Scaling changes the
// center-to-center offset, while orientation stays rigid for OpenVR poses.
internal sealed class ScaledRelativePose
{
    private Matrix4x4 unscaled = Matrix4x4.Identity;

    public void SetLocal(Matrix4x4 local, float scale)
    {
        unscaled = local;
        unscaled.Translation /= scale;
    }

    public Matrix4x4 AtScale(float scale)
    {
        var local = unscaled;
        local.Translation *= scale;
        return local;
    }
}
