using System.Numerics;

namespace GoBoard.Core;

internal enum ResizeResult { Active, Cancelled, Completed, SaveFailed }

// A drag is a transient preview. Commit only its size against the latest file.
internal sealed class ResizeSession(SettingsStore settings)
{
    private ResizePose pose;
    private int startingPercent;
    private bool watchTrigger;
    public bool Active => pose != null;
    public float Scale => pose?.Scale ?? settings.Current.Scale;

    public bool Begin(Matrix4x4 panel, Matrix4x4 controller, float x, float y,
        bool triggerAvailable = false, bool triggerHeld = true)
    {
        if (Active || (triggerAvailable && !triggerHeld)) return false;
        startingPercent = settings.Current.SizePercent;
        pose = ResizePose.Capture(panel, controller, OverlayGeometry.ResizePoint(Scale, x, y), Scale);
        watchTrigger = triggerAvailable && triggerHeld;
        return Active;
    }

    public void Update(Matrix4x4 panel, Matrix4x4 controller) => pose?.Update(panel, controller);

    public ResizeResult Track(Matrix4x4 panel, Matrix4x4? controller, bool triggerAvailable,
        bool triggerHeld, bool ownerReleased, bool ownsCapture)
    {
        if (!Active || !controller.HasValue || (!ownerReleased && !ownsCapture) ||
            (watchTrigger && !triggerAvailable && !ownerReleased))
        {
            Cancel();
            return ResizeResult.Cancelled;
        }
        Update(panel, controller.Value);
        // Dashboard overlay events are authoritative. Legacy polling is often
        // unavailable while the dashboard owns input; use it only as a fallback.
        if (ownerReleased || (triggerAvailable && !triggerHeld))
            return Complete() ? ResizeResult.Completed : ResizeResult.SaveFailed;
        watchTrigger |= triggerAvailable && triggerHeld;
        return ResizeResult.Active;
    }

    public bool Synchronize()
    {
        if (!Active || settings.Current.SizePercent == startingPercent) return false;
        Cancel();
        return true;
    }

    public bool Complete()
    {
        if (!Active) return false;
        var percent = pose.SizePercent;
        Cancel();
        // Avoid a write for a click without a size change. Still observe other editors.
        if (percent == startingPercent) return settings.Reload() || settings.Error == null;
        return settings.Update(s => s.SizePercent == startingPercent ? s with { SizePercent = percent } : s);
    }

    public void Cancel() => pose = null;
}
