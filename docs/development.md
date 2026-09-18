# Development

The production app is separate from `src/GoBoard.Poc`, which remains a working reference. Both use the same confirmed interaction model, but have distinct executable names, OpenVR overlay keys, and runtime files. Do not run both at once: `start-goboard.ps1` and the production executable refuse to start while `GoBoard.Poc` is running.

## Prerequisites and commands

- Windows x64
- .NET SDK 10.0.101 or a later 10.0 patch selected by `global.json`
- SteamVR only when running the overlay; builds, tests, and PNG rendering do not require a headset

```powershell
dotnet restore GoBoard.slnx --locked-mode
dotnet build GoBoard.slnx -c Release --no-restore
dotnet test GoBoard.slnx -c Release --no-build
dotnet run --project src/GoBoard.App -c Release -- --render .runtime\panel.png
dotnet run --project src/GoBoard.App -c Release -- --render .runtime\swedish.png --layout sv --state hover
```

Run the overlay with `./start-goboard.ps1` and stop it with `./stop-goboard.ps1`. Production logs and the graceful stop signal live under `.runtime/app`; the POC continues to use its original `.runtime/poc.*` and `.runtime/stop` files.

The Windows integration commands remain explicit because they activate a disposable native test window or Windows shell UI:

```powershell
dotnet run --project src/GoBoard.App -c Release -- --input-check
dotnet run --project src/GoBoard.App -c Release -- --layout-check
dotnet run --project src/GoBoard.App -c Release -- --shell-check
```

Do not use them while working in a user document. `--input-check` and `--layout-check` guard their disposable test target and abort when focus changes. The recent broad shortcut check stopped safely when F10 transferred focus, so application-level shortcut behavior remains a manual acceptance check.

## Keyboard sound presets

Both launch paths default to **A · Cushioned wood**, with press and quieter release sounds matching the listening preview. **C · Soft low thud** is retained as an alternate. To select C, stop the app and launch it from a PowerShell session with `$env:GOBOARD_KEY_SOUND = 'soft-low-thud'`. Set `$env:GOBOARD_KEY_SOUND = 'cushioned-wood'` (or remove the variable) before restarting to return to A. This setting works with both `start-goboard.ps1` and `start-poc.ps1`.

## Project boundaries

| Project | Responsibility |
| --- | --- |
| `GoBoard.Core` | Keyboard geometry and legends, modifier/repeat state, pointer identity, grab ownership, and relative pose math. It has no Windows, SkiaSharp, or OpenVR dependency. |
| `GoBoard.Platform.Windows` | Foreground thread/HKL observation, balanced scan-code `SendInput`, controlled native checks, and nonblocking key-down/key-up audio. |
| `GoBoard.Presentation.Skia` | The 858 x 302 panel and 180 x 60 grab-line drawing. It consumes Core state and does not own input or VR lifecycle. |
| `GoBoard.OpenVR` | Independent overlay lifecycle, dashboard-relative placement, two-controller event routing, controller-relative grabs, and persistent double-buffered OpenGL texture submission. |
| `GoBoard.App` | The small executable entry point. |
| `GoBoard.Tests` | Discoverable pure regression tests for layout/state, two-hand typing while dragging, stale events, relative poses, OpenVR matrix conversion, and extended scan flags. |

The outer margin is 4 logical units on all sides; key sizes and internal spacing are unchanged. Physical dimensions follow the cropped logical bounds so removing padding also reduces the dashboard area covered.

The production panel is 858 x 302 logical units, rendered at 2574 x 906 and displayed at about 91.2 x 32.1 cm. OpenVR mouse coordinates use a bottom-left origin; Skia uses top-left. The shared geometry performs that inversion. Texture UV bounds remain U 0→1 and V 1→0 in `OverlayGraphics`; changing that flips the panel.

## Midnight mint keyboard

The production renderer follows `artifacts/keyboard-mockups/01-midnight-mint-v5-flat-low-contrast.png`: flat dark surfaces, faint borders, pale primary/Shift legends, mint AltGr legends, and no permanent header/footer. Navigation uses the standard 3-column by 2-row cluster: Insert / Home / PgUp above Delete / End / PgDn, with inverted-T arrows below and Print Screen / Scroll Lock / Pause above. All three system keys are non-repeating. Pause is injected as VK_PAUSE; Print Screen retains its E0 scan prefix. Close and Settings are deferred. The POC retains its earlier design.

There are 86 US or 87 Swedish buttons. Caps sends a non-repeating Caps Lock stroke and reflects the actual Windows toggle. Menu sends the extended application-menu key. Left/right Shift buttons share one logical Shift mode, and left/right Ctrl buttons share one Ctrl mode; these aliases deliberately use the same scan code so they cannot inject duplicate modifiers. Swedish ISO Enter has one continuous L-shaped face and an excluded lower-left hit region. US uses a rectangular ANSI Enter and backslash above it. Space has an empty legend.

Primary legends reflect the current Shift/Caps/AltGr output. Non-letter keys also show their Shift alternative, while supported Swedish AltGr alternatives appear in mint. A small mint dot marks an active dead-key legend. Hover uses a thin outline, a pressed key uses mint fill, one-shot modifiers show a hollow circle and underline, and locked modifiers show a padlock. Caps and Scroll Lock use a dot and dark teal fill reflecting the Windows toggle state. Unsupported layouts and input failures display a wrapped notice in the existing gap between navigation and arrows; normal operation has no footer text.

Headless previews use the real renderer and a sink that cannot inject input. `--layout us|sv` and `--state idle|hover|pressed|oneshot|locked|shift|caps|scrolllock|altgr|unsupported|error` require `--render`. These options never override live Windows layout detection. Preview images are in `artifacts/keyboard-implementation`. Regression tests cover shape overlap, Enter's cutout, shared modifiers, Caps/Menu/Insert non-repeat, and rendering of every preview state in both layouts. Visual readability and two-controller interaction still require an in-headset acceptance check.

Both overlay surfaces enable `MultiCursor`. Cursor slots are treated only as transient laser slots; explicit tracked-device identity owns focus and capture. The global hover query must not cancel per-hand input or grabs. The complete grab event queue is drained before attachment so a queued release wins. Grabbing cancels only the owner's keyboard capture, and timestamp watermarks reject queued typing from that controller after release.

The renderer uses fixed Windows-derived eight-state US/Swedish legend tables. Do not replace them with live `ToUnicodeEx` calls: even nonmutating worker-thread calls can disturb a pending global dead-key accent. Unsupported HKLs stay visibly disabled.

Set `GOBOARD_TRACE_GRAB=1` before launching only when controller event diagnostics are needed. Normal operation keeps high-frequency motion logs disabled while retaining rejected-edge and lifecycle messages.



