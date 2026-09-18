# GoBoard proof of concept

The original implementation lives in `src/GoBoard.Poc` and remains a working reference. The production app lives in `src/GoBoard.App`; see [development](development.md) for its architecture and commands. Stop either app before launching the other.

The PoC follows the focused Windows application between standard US English (79 keys) and Swedish (80 keys). It includes letters, the number row, punctuation, Space/Backspace/Enter, F1–F12, Esc/Tab, navigation/arrows, click-to-arm/lock Ctrl/Alt/AltGr/Shift, and an arm-then-tap Win key. It retains the earlier interface; dimensions and visuals below describe the PoC.

## Run

Requires Windows x64, the .NET 10 SDK, and SteamVR running with a connected headset. The first build restores SkiaSharp and OpenTK from NuGet.

Run all commands from the repository root in PowerShell:

```powershell
.\start-poc.ps1
```

Open the SteamVR menu. A separate 0.898 m wide (0.364 m tall) GoBoard overlay appears initially 0.30 m below its bar. It remains visible alongside any dashboard tab and hides when the menu closes. Moving the dashboard carries the panel with it. Point at the narrow grab line beneath GoBoard and hold the trigger to move and rotate only GoBoard. The 10 cm line has a forgiving 18 x 6 cm target: only the line brightens on hover and turns mint while grabbed. The line is centered 3.75 cm below the panel edge, with the padded target still clear of the panel. Both overlays use SteamVR's native dark translucent backfaces. Release to retain the new dashboard-relative position; moving the dashboard then carries both panel and handle. No settings tab is created yet; a future settings entry will be distinct from the keyboard overlay. The app neither switches dashboard tabs nor registers for startup. Placement resets when the POC restarts.

To type, open SteamVR Desktop and click a text field (for example, the scratch Notepad document at `artifacts/typing-check.txt`). Point at a GoBoard key and pull the trigger. The footer names the focused application; GoBoard never activates a desktop window while typing.

- Hovered keys have a mint outline; held keys turn mint.
- Click Ctrl, Alt, AltGr, or Shift once for the next ordinary key, again to lock it, and a third time to clear it. One-shot modifiers show a ring and underline; locked modifiers have a mint fill and padlock. Modifiers stack: Ctrl, Alt, S sends Ctrl+Alt+S and clears both one-shots. Shift updates letters and punctuation in either active mode; physical Shift/Caps Lock are also reflected. Swedish AltGr updates symbol labels and uses the same one-shot/locked behavior; US labels that key RAlt.
- Click Win once to arm the next shortcut (Win then R sends Win+R). Click Win again while armed to send a standalone Windows-key tap and clear armed modifiers. There is no Win lock mode, timing requirement, or separate Start button; after the tap, the next Win click arms again.
- Letters, navigation keys, Space, and Backspace repeat after 450 ms, then every 60 ms. Enter, Escape, Insert, F1–F12, and modifiers do not repeat. Repeats retain the chord captured at the initial press.
- Moving off a held key cancels it. Sliding while holding the trigger does not press other keys. Pointer loss, controller tracking loss, hiding, changing foreground app/input layout, and normal shutdown stop captured keys/repeat. Hiding, target changes, and shutdown also clear armed/locked modifiers. Modifiers apply only during balanced key strokes, so a locked modifier does not alter desktop mouse clicks.
- The language badge and geometry follow the foreground application input layout automatically. Swedish includes å/ä/ö, its ISO extra key, and AltGr symbols; US uses ANSI geometry. Physical scan codes preserve Windows shortcut and dead-key behavior. Other layouts, including UK English, show an unsupported status and disable typing rather than display incorrect legends.

Stop it gracefully with:

```powershell
.\stop-poc.ps1
```

Startup and error logs are in `.runtime/poc.log` and `.runtime/poc.error.log`. To run in a terminal instead, with Ctrl+C to close:

```powershell
dotnet run --project src/GoBoard.Poc -c Release
```

## Rendering and controller implementation

The POC renders an opaque 2532x1026 RGBA panel with SkiaSharp, uploading it through persistent, double-buffered OpenGL textures when visual state changes (about 9.91 MiB per upload). OpenTK owns an invisible, unfocused context. The new texture finishes uploading before SetOverlayTexture publishes it; both textures live until OpenVR shuts down. This replaces the asynchronous raw-image loading path used by the earlier build, which blinked during interaction. SkiaSharp still draws the pixels; no Direct3D implementation is used. Text and paths are drawn directly at 3x resolution from a 844x342 logical canvas, rather than upscaling the earlier bitmap. Physical size is 89.77 x 36.38 cm, giving about 2.82 pixels/mm; physical dimensions, logical input coordinates, and texture resolution are independent. It uses `CreateOverlay` and `VisibleInDashboard`, with explicit 844x342 mouse coordinates, a matching rectangular intersection mask, and `NoBackside=false` to enable SteamVR's native backing when viewed from behind. A second overlay uses a 540x180 texture and 180x60 logical input coordinates. It contains a 10 cm x 4 mm line inside an 18 x 6 cm input target, with a line-only hover highlight and a transparent front background in every state. The target remains below the panel and does not overlap its input bounds. GoBoard reads the dashboard bar's pose and follows it using a stored local offset. During a grab, it captures the panel-to-controller pose and uses `SetOverlayTransformTrackedDeviceRelative` for both panel and handle. SteamVR drives held movement directly, without application smoothing or a pose-copy loop. On release, a display-predicted controller snapshot supplies the retained dashboard offset. Delayed runtime pose readbacks are not used. Pointer-up, tracking loss, dashboard closure, or loss of focus from the owning controller ends the grab. Both overlays enable MultiCursor. Input state is keyed by controller identity because SteamVR can reuse a cursor slot for another hand without a new focus-enter. Fresh controller-identified motion on the live overlay can restore hover; it never initiates typing or grabbing, and pre-release/pre-leave motion is rejected. Grabbing suppresses only the owning controller; the other hand can type, use modifiers, and repeat keys while moving the panel. Releasing the grab preserves the other hand input state. Timestamp guards prevent queued keyboard events from the grab owner typing after release. A new grab requires current handle focus, a matching controller identity, an in-bounds press less than 200 ms old, and controller-specific handle focus surviving the full event queue. Releases and focus changes invalidate queued presses. Controller identity comes from the event or the focused hand; it is never guessed from the dashboard primary controller. One pointer owns the grab at a time. The keyboard and handle upload pixels only when their hover/pressed/modifier/status visuals change; pointer movement within one key does not redraw the texture. Visible input and dashboard following use `WaitFrameSync` to match SteamVR frames; hidden lifecycle checks run at 10 Hz. The matching OpenVR 2.15.6 binding/library and their license are vendored under `vendor/openvr`; NuGet dependencies are pinned in the project and lock file. Use an up-to-date SteamVR runtime.

The anchor key `valve.steam.gamepadui.bar` is an internal SteamVR name, not a guaranteed SDK contract. If it cannot be found or read, the POC hides its panel and logs a compatibility warning. It never writes to SteamVR's dashboard transform.

## Checks

For a bounded integration check, run with `--seconds 3`. For a PNG without starting SteamVR, use `--render panel.png`:

```powershell
dotnet run --project src/GoBoard.Poc -c Release -- --seconds 3
dotnet run --project src/GoBoard.Poc -c Release -- --render panel.png
dotnet run --project src/GoBoard.Poc -c Release -- --self-test
dotnet run --project src/GoBoard.Poc -c Release -- --input-check
dotnet run --project src/GoBoard.Poc -c Release -- --layout-check
```

The explicit `--input-check` command temporarily opens its own disposable text field, tests real Windows input, and closes it. It types only into that test window, aborts if focus moves, and restores the previous foreground window. It does not start SteamVR. The separate --shell-check command opens and dismisses Run and Start, verifies the observed shell window, and never enters a command. These checks temporarily activate their own test window; normal overlay typing never activates a desktop window.

## Recorded verification

These notes record checks and headset feedback from PoC development; they are not a fresh verification of the current production app.

Verified locally: Release build and model checks for dashboard following, grab ownership, pointer geometry, modifier modes, Ctrl+A, Ctrl+Shift+F, Ctrl+Left, all F keys, Windows chords, repeat, stale input, cancellation, and failure cleanup. The Windows Rich Edit integration test passed real text, Ctrl+A selection, Ctrl+Left word movement, Ctrl+Shift+Left selection, Ctrl+Shift+F delivery with both modifiers, all F1–F12 down/up pairs, and extended-arrow delivery. Win+R opened the real Run dialog and Escape dismissed it; no command was entered. The automated standalone Start-window check was inconclusive, but the user subsequently confirmed that hotkeys, function keys, and the Windows key work in-headset. Tests never type into user documents.

Focus regressions now replay the logged slot-0 device switch, releases after slot changes, pause/resume, and stale-motion rejection. The user confirmed typing with both hands. Grabbing still needed correction; the current revision removes the shared hover veto and supports typing with the other hand while dragging. The user subsequently confirmed that inactive-hand grabbing now works. A runtime trace shows grabs starting from both primary and secondary hands and ending on owner release.

Earlier headset feedback confirmed typing and the independent overlay's grab/follow/backface behavior. The previous panels were expanded to 2532x1026 to fit the full number and punctuation rows at the same pixel density and letter-key size. Persistent OpenGL submission previously passed 100 alternating hidden-overlay updates. Blink removal and the expanded panel still need headset confirmation. Native-control history is in [these notes](native-controls-investigation.md).

## Manual headset checks

For a layout check in-headset, focus a disposable text document, switch its Windows input language between Swedish and US English, and watch the badge and keys follow. Try åäö, Shift then Å/Ä/Ö, AltGr then 2 (@), AltGr then E (€), 1234567890, and comma/period/slash. Swedish slash is Shift then 7. Try the acute accent then E for é. Switching target/layout clears armed modifiers.

For a shortcut check, focus a disposable text document and try Ctrl → A, Ctrl → Left, and Ctrl → Shift → Left. In an editor that supports it, Ctrl → Shift → F should open its search UI. Try F2/F5/F11 where appropriate, then Win → Win to open the menu and Win → R to open Run (Escape dismisses it). Application behavior remains an acceptance check; key-delivery tests alone do not prove every application's shortcut handling.

## Additional verification history

The --layout-check command uses only its own disposable text window and already-loaded layouts, restores its test-thread layout and the previous foreground window, and never changes installed/default language settings. It passed Swedish → US → Swedish detection, actual åäö/ÅÄÖ, AltGr @/€/|/{[]}, dead-key é, digits, punctuation, and modifier cancellation. Initial and subsequent legend lookups preserve a pending accent. Standard-layout legend tables were captured from Windows translation APIs; rendering uses those fixed tables so it never reads or modifies the live dead-key buffer. Headset confirmation of the new layouts remains pending.

Latest shortcut regression attempt: the test aborted safely before F11 because focus moved to Discord after F10. It did not send further input to Discord. Earlier shortcut/F-key acceptance remains recorded above; the new layout integration check and all model checks passed.

Grab/typing regression checks pass for both hand directions: starting a grab cancels only its own keyboard capture; the other hand can type, consume Shift, and continue repeat; grab release preserves the other hand; queued input from the grab owner cannot type after release. Owner releases and focus loss still end a grab, and another hand cannot release or steal it.

Inactive-hand grab acceptance: the user reproduced the interaction with grab-event tracing enabled and reported it working. Both overlays read back MultiCursor=true. The trace contains primary slot 0/device 7 and secondary slot 1/device 8 grab starts, owner releases, and unrelated other-hand focus changes that do not cancel the capture. Legacy controller-state polling is unavailable on both hands in the dashboard; the observed release path is the identified overlay pointer-up event.

## Original scope and planning

The following notes preserve the original PoC planning context. Proposals and open decisions here may have been superseded; consult [development](development.md) for the current implementation.

### Confirmed scope

An independent [stereo dashboard experiment](../experiments/SteamVR.StereoDashboard/README.md) adds a separate SteamVR dashboard tab displaying a stereoscopic texture. Its executable, launch/stop scripts, and runtime files are isolated from the keyboard POC.

- A keyboard for use in SteamVR.
- An original interface design.
- Swedish and US English keyboard layouts that automatically match Windows input language.
- Selective functionality inspired by YuuBoard, without requiring feature parity.
- Media controls are out of scope.

### Proposed first working version

Proposals below are implementation defaults, not additional confirmed requirements.

- A SteamVR overlay with letters, numbers, punctuation, editing/navigation keys, and modifiers.
- Swedish standard and US English layouts.
- Visible current layout and modifier state.
- Press/release handling, held-key repeat, and safe key release when the overlay is hidden or shuts down.
- Independent controller pointers with shared click-to-arm/lock modifiers.
- A desktop preview for iterating on the interface without a headset.

The first integration milestone should prove typing into a focused Windows application while the SteamVR desktop remains usable. A dashboard tab alone does not establish that interaction or replacement of SteamVR's built-in keyboard button.

### Decisions still open

- Visual direction and keyboard density, including whether a numpad belongs in the initial version.
- Exact dashboard placement and launch interaction.
- The SkiaSharp-to-OpenVR rendering bridge, to be selected after a small integration prototype.

### Implementation direction

Use C#/.NET with **SkiaSharp** for drawing and **OpenVR** for the SteamVR overlay. SkiaSharp is the selected renderer; Unity is not part of the implementation.

Keep key geometry, pointer interaction, Windows layout detection, and input injection independent of SkiaSharp. The renderer consumes the keyboard state and draws onto an `SKCanvas`, allowing a desktop preview and the VR overlay to share the same drawing code.

The current VR keyboard uses CPU SkiaSharp drawing with a persistent OpenGL texture bridge. Rendering Skia directly onto GPU surfaces is a possible later optimization; measure full-keyboard interaction cost before changing the drawing backend.

See [the technical design](technical-design.md) for implementation boundaries and acceptance checks.

### References

- [YuuBoard product reference](https://yuuzami.itch.io/yuuboard)
- [kurohuku's SteamVR Overlay Tutorial for Unity](https://dev.to/kurohuku/series/27740)
- [Valve OpenVR overlay overview](https://github.com/ValveSoftware/openvr/wiki/IVROverlay_Overview)
