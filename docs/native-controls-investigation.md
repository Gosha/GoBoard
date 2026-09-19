# Native grab controls: investigation results

Historical investigation of native controls. The user subsequently selected a custom grab line, implemented in GoBoard: dragging the keyboard changes its local offset, and dragging the dashboard carries both. The findings below explain that decision; they do not claim native controls are implemented.

## Confirmed corrections

- `MinimalControlBar` was incorrectly treated as an enable flag. Valve's developer explains that the flag suppresses the dashboard control bar. See the January 2025 replies in [Valve's discussion](https://steamcommunity.com/app/250820/discussions/3/4633736292852602721/).
- The native-control experiment cleared that flag and requested `EnableControlBar`. The request succeeds and reads back, but the user confirms there is still no native handle. Successful flag storage is not evidence that SteamVR rendered controls.
- The panel now uses an explicit 512x256 mouse scale, matching rectangular intersection mask, and `NoBackside`. Live OpenVR ray tests hit the center and near-edge pixels and miss beyond the 0.75 x 0.375 m image. The user reports improved bounds/following.
- The earlier follower filtered recent submitted poses and dashboard-carried targets to avoid confusing delayed readback with independent grabs. The custom-grab implementation now changes the local offset explicitly and does not infer dragging from runtime readbacks.

## Native-frame path observed locally

The installed SteamVR dashboard scripts create native external-overlay frames only for overlays whose internal `eOverlayType` is `1`, also requiring the visible-in-dashboard flag. Those frames own the native controls, docking, and mounted panel scene-graph nodes. Setting the visible-in-dashboard flag on a normal overlay has not made it enter this path.

Read-only reference locations in this installation:

- `C:/Program Files (x86)/Steam/steamapps/common/SteamVR/resources/webinterface/dashboard/chunk~8012d0c89.js`: external overlay frame eligibility and construction.
- `C:/Program Files (x86)/Steam/steamapps/common/SteamVR/resources/webinterface/dashboard/chunk~336cd93ad.js`: external frame-page mounting and panel anchors.
- `C:/Program Files (x86)/Steam/steamapps/common/SteamVR/resources/webinterface/dashboard/systemui.js`: frame controls and docking.

These are implementation details, not a supported API. No installed SteamVR files were modified and no private scene-graph messages were injected.

## Public-API detachment probe

A temporary dashboard overlay was created, its dashboard tab hidden, and the following public calls tested. The overlay was then destroyed.

| Call | Observed result |
| --- | --- |
| `CreateDashboardOverlay` | `None` (success) |
| `SetOverlayFlag(NoDashboardTab, true)` | `None` |
| `SetOverlayTransformAbsolute` | `PermissionDenied` |
| `GetOverlayTransformType` | Still `DashboardTab` |
| `ShowOverlay` | `PermissionDenied` |
| `HideOverlay` | `PermissionDenied` |
| `DestroyOverlay` | `None` |

Thus creating a native dashboard frame and then detaching/positioning it like a normal overlay is not a usable public-API workaround on this runtime. The current [OpenVR header](https://github.com/ValveSoftware/openvr/blob/v2.15.6/headers/openvr.h) likewise documents that independent show/hide calls are not applicable to dashboard overlays, and exposes no overlay-parent transform setter.

## Remaining limitation

No supported public-API route has been found that combines a separate freely positioned overlay, SteamVR's native frame controls, and application-controlled dashboard-relative positioning. This does not prove every private integration is impossible. Further native work would require understanding SteamVR's internal frame/scene-graph protocol or obtaining an API extension from Valve; it should be kept separate from the production app until independently demonstrated.

GoBoard retains the corrected bounds and dashboard following and adds a custom SkiaSharp grab line using SteamVR pointer events and tracked controller poses. Native grabbing remains unimplemented; the unused native-control flags were removed.

