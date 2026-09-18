# SteamVR stereo dashboard experiment

Independent executable and dashboard entry named **Stereo Experiment**. Shares only the vendored OpenVR SDK and pinned SkiaSharp version with GoBoard. No project reference to the keyboard POC, startup registration, or persistent SteamVR settings.

Requires Windows x64, .NET 10 SDK, and SteamVR with a connected headset. From the repository root:

```powershell
.\experiments\SteamVR.StereoDashboard\start.ps1
.\experiments\SteamVR.StereoDashboard\stop.ps1
```

Start opens the dashboard and selects the experiment. Add `-NoShow` to register the tab without switching to it. Logs and the independent stop signal live in `.runtime/stereo-dashboard/`.

The main dashboard panel uses `CreateDashboardOverlay`, `SideBySide_Parallel`, and one static opaque 2048x640 RGBA upload via `SetOverlayRaw`. Left and right halves each contain a full-resolution 1024x640 view. Square texels (`SetOverlayTexelAspect(1)`) preserve the per-eye aspect ratio. The tab icon is a separate, ordinary 128x128 texture. The main panel is 1.2 m wide; SteamVR controls its dashboard placement and visibility.

The background, grid, and labels have zero disparity. Three circles have +12, 0, and -12 pixels of left-minus-right disparity, producing near, surface, and far depth cues. These are fixed stereo illustrations, not a head-tracked 3D scene; depth is relative and depends on viewing geometry. There are no clickable controls.

## Headset check

1. Select the goggles icon / **Stereo Experiment** in the dashboard.
2. With both eyes open, the left circle should float in front of its grid, the middle circle should sit on it, and the right circle should sit behind it.
3. Close the right eye: only the bottom-left **L** marker should remain. Close the left eye: only the bottom-right **R** marker should remain.
4. Circles should be round, and each eye should see one complete panel.
5. Switch dashboard tabs and close/reopen the dashboard. The experiment should behave like a normal tab. Stop it and confirm the tab disappears.

## Local checks

```powershell
dotnet build experiments/SteamVR.StereoDashboard -c Release -p:RestoreLockedMode=true
dotnet run --project experiments/SteamVR.StereoDashboard -c Release -- --render artifacts/stereo-dashboard.png
dotnet run --project experiments/SteamVR.StereoDashboard -c Release -- --seconds 3
```

The PNG is a left | right stereo pair, not a fused desktop preview. `--render` does not initialize SteamVR. The bounded runtime check verifies dashboard transform type, stereo flag readback, uploaded texture dimensions, event handling, and cleanup. It cannot establish perceived depth or eye routing without the headset check.

Locally verified: Release build, PNG layout, live dashboard-tab creation, stereo flag readback, both image-loaded events, and 2048x640 texture dimensions with the headset connected. Perceived depth and correct eye routing still require the headset check above.

API references: [Valve overlay overview](https://github.com/ValveSoftware/openvr/wiki/IVROverlay_Overview) and [OpenVR 2.15.6 header](https://github.com/ValveSoftware/openvr/blob/v2.15.6/headers/openvr.h).
