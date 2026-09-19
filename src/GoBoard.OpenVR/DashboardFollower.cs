using System.Numerics;
using GoBoard.Core;
using Valve.VR;

namespace GoBoard.Vr;

internal sealed class DashboardFollower(CVROverlay overlay, ulong panel, ulong handle, GrabHandle grab, ResizeHandle resize)
{
    // SteamVR's dashboard bar overlay key is not a public SDK contract. Keep this
    // compatibility dependency in one place; fail visibly if it disappears.
    private const string DashboardKey = "valve.steam.gamepadui.bar";
    private const ETrackingUniverseOrigin Origin = ETrackingUniverseOrigin.TrackingUniverseStanding;
    private readonly RelativePose pose = new();
    private bool visible;
    private bool warned;
    private ulong previousAnchor;
    private GrabPose boundGrab;
    private float panelScale = 1;
    private bool sizeChanged;

    public void SetScale(float scale)
    {
        if (panelScale == scale) return;
        Check(overlay.SetOverlayWidthInMeters(panel, OverlayGeometry.PanelWidthInMeters * scale), "Resize keyboard");
        panelScale = scale;
        sizeChanged = true;
    }


    public void ResetPosition()
    {
        // Cancel captures and drain their queued events before placing the keyboard again.
        grab.Update(false, default);
        resize.Update(false, default);
        pose.Reset();
    }

    public bool Update()
    {
        ulong anchor = 0;
        var scale = new HmdVector2_t();
        var rawParent = new HmdMatrix34_t();
        if (!overlay.IsDashboardVisible())
        {
            grab.Update(false, default); resize.Update(false, default);
            SetScale(resize.Scale); SetVisible(false); return false;
        }
        if (overlay.FindOverlay(DashboardKey, ref anchor) != EVROverlayError.None ||
            overlay.GetOverlayMouseScale(anchor, ref scale) != EVROverlayError.None ||
            overlay.GetTransformForOverlayCoordinates(anchor, Origin, new HmdVector2_t { v0 = scale.v0 / 2, v1 = 0 }, ref rawParent) != EVROverlayError.None ||
            !OpenVrPose.TryRigid(rawParent, out var parent))
        {
            grab.Update(false, default);
            resize.Update(false, default);
            SetScale(resize.Scale);
            SetVisible(false);
            if (!warned) Console.Error.WriteLine($"Waiting for the SteamVR dashboard anchor ({DashboardKey}). Open the SteamVR menu; a changed/legacy dashboard may be unsupported.");
            warned = true;
            return false;
        }
        warned = false;
        if (previousAnchor != anchor)
        {
            Console.WriteLine($"Following dashboard bar {anchor}; separate overlay, default offset 0.30 m below the bar.");
            previousAnchor = anchor;
        }

        var update = pose.Update(parent);
        // An existing resize owns the interaction. For simultaneous fresh presses,
        // moving wins this frame and the resize queue is drained without capture.
        var dragged = grab.Update(true, update.World, interactive: !resize.Active);
        if (dragged.HasValue)
        {
            pose.SetWorld(parent, dragged.Value);
            var dragUpdate = pose.Update(parent);
            update = (dragUpdate.World, update.Write || dragUpdate.Write);
        }
        resize.Update(true, update.World, canStart: grab.ActiveGrab == null);
        SetScale(resize.Scale);
        if (grab.ActiveGrab != null)
        {
            // Bind once per grab. SteamVR now tracks the controller at compositor
            // rate instead of displaying app-polled absolute poses a frame late.
            if (boundGrab != grab.ActiveGrab || sizeChanged)
            {
                var relative = OpenVrPose.ToOpenVr(grab.ActiveGrab.ControllerOffset);
                Check(overlay.SetOverlayTransformTrackedDeviceRelative(panel, grab.Controller, ref relative), "Attach panel to controller");
                var barRelative = OpenVrPose.ToOpenVr(OverlayGeometry.GrabFromScaledPanel(panelScale) * grab.ActiveGrab.ControllerOffset);
                Check(overlay.SetOverlayTransformTrackedDeviceRelative(handle, grab.Controller, ref barRelative), "Attach handle to controller");
                var resizeRelative = OpenVrPose.ToOpenVr(OverlayGeometry.ResizeFromScaledPanel(panelScale) * grab.ActiveGrab.ControllerOffset);
                Check(overlay.SetOverlayTransformTrackedDeviceRelative(resize.Handle, grab.Controller, ref resizeRelative), "Attach resize handle to controller");
                boundGrab = grab.ActiveGrab;
                Console.WriteLine($"SteamVR now tracks the held panel directly on controller {grab.Controller}; no app smoothing.");
            }
        }
        else if (boundGrab != null || update.Write || !visible || sizeChanged)
        {
            var raw = OpenVrPose.ToOpenVr(update.World);
            Check(overlay.SetOverlayTransformAbsolute(panel, Origin, ref raw), "Follow dashboard pose");
            var bar = OpenVrPose.ToOpenVr(OverlayGeometry.GrabFromScaledPanel(panelScale) * update.World);
            Check(overlay.SetOverlayTransformAbsolute(handle, Origin, ref bar), "Place grab handle");
            var corner = OpenVrPose.ToOpenVr(OverlayGeometry.ResizeFromScaledPanel(panelScale) * update.World);
            Check(overlay.SetOverlayTransformAbsolute(resize.Handle, Origin, ref corner), "Place resize handle");
            boundGrab = null;
        }
        SetVisible(true);
        sizeChanged = false;
        return true;
    }

    private void SetVisible(bool value)
    {
        if (value == visible) return;
        Check(value ? overlay.ShowOverlay(panel) : overlay.HideOverlay(panel), "Set panel visibility");
        Check(value ? overlay.ShowOverlay(handle) : overlay.HideOverlay(handle), "Set grab handle visibility");
        Check(value ? overlay.ShowOverlay(resize.Handle) : overlay.HideOverlay(resize.Handle), "Set resize handle visibility");
        visible = value;
        Console.WriteLine($"Separate panel visible: {value}.");
    }

    private static void Check(EVROverlayError error, string operation)
    {
        if (error != EVROverlayError.None) throw new InvalidOperationException($"{operation}: {error}");
    }
}


