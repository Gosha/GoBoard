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
dotnet run --project src/GoBoard.App -c Release -- --render-settings .runtime\settings-vr.png
dotnet run --project src/GoBoard.App -c Release -- --render-desktop-settings .runtime\settings-desktop.png
```

Run the overlay with `./start-goboard.ps1` and stop it with `./stop-goboard.ps1`. Production logs and the graceful stop signal live under `.runtime/app`; the POC continues to use its original `.runtime/poc.*` and `.runtime/stop` files.

## Desktop mode

Run `./start-goboard.ps1 -Desktop`, or `GoBoard.exe --desktop [--stop-file PATH] [--seconds N]`. Desktop mode starts the keyboard and provides Settings/Close controls without initializing SteamVR or loading its native DLL. The launch script builds this mode under `artifacts/desktop-build` so existing settings windows from the standard build do not lock its output. Both script launch modes share the existing PID guard, stop signal, and logs.

The keyboard is a topmost, non-activating Windows Forms surface displaying the real Skia keyboard. Select a text field in another app, then click the keys; dragging the header moves the window. Settings opens an ordinary focused window, so select your text target again afterward. Closing the keyboard also closes its settings window. Foreground HWND/HKL detection, layout legends, balanced scan-code input, logical modifiers, and repeat reuse the production model/backend. Unknown layouts remain usable with US English geometry and labels, regardless of the previously recognized layout. Windows still interprets scan codes using its active layout, so the panel warns that output may differ from the labels. Lost mouse capture, hiding, target/layout changes, resizing, and closing cancel pending input. This mode uses one mouse pointer; two-controller behavior remains specific to VR.

Size uses the shared 50–150% setting, with 100% corresponding to 858 logical desktop pixels plus a 42-pixel header before display scaling. It scales with monitor DPI and fits the monitor's work area. The VR physical width and desktop pixel width share a percentage, not a physical measurement. Sound settings are identical in both modes. Desktop placement is session-only.

Desktop preview and a guarded native integration check (the latter temporarily focuses a disposable text window):

```powershell
dotnet run --project src/GoBoard.App -c Release -- --render-desktop .runtime\desktop-keyboard.png
dotnet run --project src/GoBoard.App -c Release -- --desktop-input-check
```

`GoBoard.exe --desktop-launch-check` starts the keyboard first and clicks its Settings button without injecting keys. Run it through `Start-Process -WindowStyle Hidden -Wait` to reproduce the PowerShell launcher's startup flags. It verifies that Settings is natively visible, has the normal taskbar window style, and is not topmost. This catches Windows applying the hidden-console startup hint to the first activating window even while WinForms reports `Visible=true`; the settings host explicitly shows that window after the hint is consumed.

The native check sends mouse messages to the real keyboard window, verifies its non-activation style and behavior, types only into its disposable text field, and restores the previous foreground window. It covers text, one-shot/locked Shift, Ctrl+A, repeat release, capture-loss cancellation, cleanup, and absence of the OpenVR native module. Unit tests cover pixel-to-key mapping at multiple sizes/DPIs and monitor constraints. The desktop preview has been visually inspected. Physical pointer dragging, mixed-DPI monitor transitions, and application-specific shortcuts remain manual acceptance checks.

The Windows integration commands remain explicit because they activate a disposable native test window or Windows shell UI:

```powershell
dotnet run --project src/GoBoard.App -c Release -- --input-check
dotnet run --project src/GoBoard.App -c Release -- --layout-check
dotnet run --project src/GoBoard.App -c Release -- --shell-check
```

Do not use them while working in a user document. `--input-check` and `--layout-check` guard their disposable test target and abort when focus changes. The recent broad shortcut check stopped safely when F10 transferred focus, so application-level shortcut behavior remains a manual acceptance check.

## Settings and keyboard sound

Run `./settings-goboard.ps1` (or `GoBoard.exe --settings`) for the desktop settings window, including when SteamVR is stopped. While the overlay runs, **GoBoard Settings** is also available as a separate SteamVR dashboard tab. Closing the desktop window leaves the keyboard running. The script builds into `artifacts/settings-build` so it can update settings while the keyboard is running; close existing settings windows from that build before rebuilding it.

Both interfaces use the same Skia `SettingsPanel`, shared button geometry, and release-to-activate pointer model, and edit `%LOCALAPPDATA%\GoBoard\settings.json`. Desktop Windows Forms only hosts the rendered panel and window chrome; resizing preserves its aspect ratio and maps mouse input through the same viewport. Size is 50–150%, with 5% steps; volume is 0–100%, with 10% steps in both interfaces. Sound can be muted or switched between **Cushioned wood** and **Soft low thud**. Clicking a preset auditions it in either interface. Reset to defaults restores 100% size/volume, sound on, and Cushioned wood. Changes propagate between processes within about half a second and survive restarts. Windows mixer volume is not changed.

The shared panel shows physical width when configuring VR, or desktop scale when opened from the desktop keyboard. A settings error appears on the panel; desktop users can hover for the full error. `GoBoard.exe --settings-input-check` exercises actual window mouse messages at several sizes using a disposable settings file, including save persistence, preset selection, disabled buttons, and drag/capture cancellation. It does not edit the user's settings.

Size changes preserve the keyboard center, logical input coordinates, texture resolution, and current dashboard-relative placement. The grab target keeps its original physical size and 7.5 mm gap beneath the resized keyboard, including while held. Resizing cancels pending keyboard captures and modifiers. Sound changes stop old playback before replacing pinned audio buffers.

Settings writes merge only the edited field into the latest file under a per-file mutex, then replace the file atomically. Unreadable or malformed files leave the last working values active and show an error; failed writes do not change applied settings. If manually repairing a malformed file, close the editors and fix or remove the file at the path above. Pure regression tests cover concurrent edits, restart persistence, failure handling, scaling geometry, settings pointer cancellation, and audio amplitude. Desktop and dashboard preview images have been inspected. Live dashboard activation, controller clicks, resize during a grab, and sound routing still need in-headset acceptance with SteamVR running.

The default is Cushioned wood, with quieter release sounds. `GOBOARD_KEY_SOUND=soft-low-thud` remains a fallback only when no production settings file exists; saved settings take precedence. The POC remains unchanged and still uses the environment variable on every launch.

## Project boundaries

| Project | Responsibility |
| --- | --- |
| `GoBoard.Core` | Keyboard geometry and legends, modifier/repeat state, pointer identity, grab ownership, relative pose math, and shared settings persistence/interaction. It has no Windows, SkiaSharp, or OpenVR dependency. |
| `GoBoard.Platform.Windows` | Foreground thread/HKL observation, balanced scan-code `SendInput`, controlled native checks, and nonblocking key-down/key-up audio. |
| `GoBoard.Presentation.Skia` | The keyboard, grab-line, and settings dashboard drawing. It consumes Core state and does not own input or VR lifecycle. |
| `GoBoard.OpenVR` | Independent overlay lifecycle, dashboard-relative placement, two-controller event routing, controller-relative grabs, and persistent double-buffered OpenGL texture submission. |
| `GoBoard.App` | Executable entry point and Windows Forms hosts for the shared Skia keyboard/settings renderers. |
| `GoBoard.Tests` | Discoverable pure regression tests for layout/state, two-hand typing while dragging, stale events, relative poses, OpenVR matrix conversion, and extended scan flags. |

The outer margin is 4 logical units on all sides; key sizes and internal spacing are unchanged. Physical dimensions follow the cropped logical bounds so removing padding also reduces the dashboard area covered.

The production panel is 858 x 302 logical units, rendered at 2574 x 906 and displayed at about 91.2 x 32.1 cm at the default 100% size. OpenVR mouse coordinates use a bottom-left origin; Skia uses top-left. The shared geometry performs that inversion. Texture UV bounds remain U 0→1 and V 1→0 in `OverlayGraphics`; changing that flips the panel.

## Midnight mint keyboard

The production renderer follows `artifacts/keyboard-mockups/01-midnight-mint-v5-flat-low-contrast.png`: flat dark surfaces, faint borders, pale primary/Shift legends, mint AltGr legends, and no permanent header/footer. Navigation uses the standard 3-column by 2-row cluster: Insert / Home / PgUp above Delete / End / PgDn, with inverted-T arrows below and Print Screen / Scroll Lock / Pause above. All three system keys are non-repeating. Pause is injected as VK_PAUSE; Print Screen retains its E0 scan prefix. Close remains deferred; settings use the separate dashboard tab and desktop window. The POC retains its earlier design.

There are 86 US or 87 Swedish buttons. Caps sends a non-repeating Caps Lock stroke and reflects the actual Windows toggle. Menu sends the extended application-menu key. Left/right Shift buttons share one logical Shift mode, and left/right Ctrl buttons share one Ctrl mode; these aliases deliberately use the same scan code so they cannot inject duplicate modifiers. Swedish ISO Enter has one continuous L-shaped face and an excluded lower-left hit region. US uses a rectangular ANSI Enter and backslash above it. Space has an empty legend.

Primary legends reflect the current Shift/Caps/AltGr output. Non-letter keys also show their Shift alternative, while supported Swedish AltGr alternatives appear in mint. A small mint dot marks an active dead-key legend. Hover uses a thin outline, a pressed key uses mint fill, one-shot modifiers show a hollow circle and underline, and locked modifiers show a padlock. Caps and Scroll Lock use a dot and dark teal fill reflecting the Windows toggle state. Fallback layouts and input failures display a wrapped notice in the existing gap between navigation and arrows; normal operation has no footer text.

Headless previews use the real renderer and a sink that cannot inject input. `--layout us|sv` and `--state idle|hover|pressed|oneshot|locked|shift|caps|scrolllock|altgr|unsupported|error` require `--render`. These options never override live Windows layout detection. Preview images are in `artifacts/keyboard-implementation`. Regression tests cover shape overlap, Enter's cutout, shared modifiers, Caps/Menu/Insert non-repeat, and rendering of every preview state in both layouts. Visual readability and two-controller interaction still require an in-headset acceptance check.

Both overlay surfaces enable `MultiCursor`. Cursor slots are treated only as transient laser slots; explicit tracked-device identity owns focus and capture. The global hover query must not cancel per-hand input or grabs. The complete grab event queue is drained before attachment so a queued release wins. Grabbing cancels only the owner's keyboard capture, and timestamp watermarks reject queued typing from that controller after release.

The renderer uses fixed Windows-derived eight-state US/Swedish legend tables. Do not replace them with live `ToUnicodeEx` calls: even nonmutating worker-thread calls can disturb a pending global dead-key accent. Unknown HKLs use US English geometry, legend tables, and modifier behavior with a visible fallback notice. The actual HKL remains available for input targeting and change detection; switching HKLs still cancels captures, repeat, and modifiers. Input errors take precedence over the fallback notice.

Set `GOBOARD_TRACE_GRAB=1` before launching only when controller event diagnostics are needed. Normal operation keeps high-frequency motion logs disabled while retaining rejected-edge and lifecycle messages.



