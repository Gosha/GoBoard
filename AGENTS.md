# Working on GoBoard

GoBoard is a Windows x64 keyboard overlay and desktop keyboard built with C#/.NET, SkiaSharp, and OpenVR. Run commands from the repository root in PowerShell.

## Project boundaries

- `src/GoBoard.Core`: shared geometry, legends, input state, pointer ownership, pose math, and settings. Keep it independent of Windows APIs, SkiaSharp, and OpenVR.
- `src/GoBoard.Platform.Windows`: foreground HWND/HKL observation, keyboard tables, balanced native input, and audio playback.
- `src/GoBoard.Presentation.Skia`: shared keyboard/settings/handle rendering. It does not own input injection or VR lifecycle.
- `src/GoBoard.OpenVR`: overlay lifecycle, placement, controller routing, grabs/resizing, and texture submission.
- `src/GoBoard.App`: executable entry point and Windows Forms hosts.
- `tests/GoBoard.Tests`: regression tests.

Production lives in `GoBoard.slnx`. `src/GoBoard.Poc` is a separate working reference; make production changes in the production projects. Preserve the distinct executable names, overlay keys, and runtime files. Do not run production and the POC together.

## Build and validation

Use the .NET SDK selected by `global.json` (10.0.101 with `latestPatch` roll-forward).

```powershell
dotnet restore GoBoard.slnx --locked-mode
dotnet build GoBoard.slnx -c Release --no-restore
dotnet test GoBoard.slnx -c Release --no-build
```

Builds, regression tests, and PNG rendering do not require SteamVR or a headset. Use the real renderer for visual checks:

```powershell
dotnet run --project src/GoBoard.App -c Release -- --render .runtime\panel.png
dotnet run --project src/GoBoard.App -c Release -- --render .runtime\swedish.png --layout sv --state reference --theme steam-soft
dotnet run --project src/GoBoard.App -c Release -- --render-desktop .runtime\desktop-keyboard.png
dotnet run --project src/GoBoard.App -c Release -- --render-settings .runtime\settings-vr.png
dotnet run --project src/GoBoard.App -c Release -- --render-desktop-settings .runtime\settings-desktop.png
```

Inspect affected previews after rendering. Check both desktop and VR presentations when changing shared UI, and cover affected themes, layouts, or interaction states. Preview layout/theme overrides do not change the live Windows layout or saved settings.

Whenever design, buttons, or copy change, regenerate the affected README screenshots in `docs/images/` with the real renderer and visually inspect them before completing the change (`--render-settings docs/images/goboard-settings.png` for the settings page).

Use `start-goboard.ps1` for VR, `start-goboard.ps1 -Desktop` for desktop, and `stop-goboard.ps1` to stop. Add `-BuildOnly` to build without launching. Launchers isolate outputs under `artifacts/vr-build`, `artifacts/desktop-build`, and `artifacts/settings-build`; preserve this separation to avoid locked DLLs. Production logs and stop signals are under `.runtime/app`.

Native checks are separate from the normal test loop. Select checks relevant to the change; see [development.md](docs/development.md) for invocation and coverage:

- `--desktop-input-check`, `--input-check`, `--layout-check`, and `--ime-check` exercise native input using disposable foreground targets. Do not run them while working in a user document; preserve their focus guards and cleanup.
- `--settings-input-check` uses a disposable settings file. Do not substitute the user's settings file.
- `--desktop-launch-check` checks Settings visibility and window style; run through `Start-Process -WindowStyle Hidden -Wait` to reproduce launcher startup behavior.
- `--shell-check` and `--desktop-shell-check` activate Windows shell UI. Treat them as explicit integration checks.

Report what was actually verified. Pure tests and PNGs do not establish headset legibility, controller feel, real focus behavior, sound routing, or end-to-end display latency. Keep remaining hardware/manual acceptance explicit.

## Input and layout constraints

- Observe the foreground window's thread and full HKL, and revalidate the target/layout before input. Normal typing must not steal focus or change the user's Windows input language.
- Use physical scan codes for ordinary keys and retain extended-key identity through key-up. Check `SendInput` results, track only GoBoard-owned downs, and preserve cleanup after partial failures. Do not release physical modifiers borrowed from the user.
- Cancel pending input and release owned keys on target/layout changes, pointer/tracking loss, hiding, and shutdown. Geometry changes must also discard stale captures and hover state.
- Keep input state per pointer. In VR, tracked-device identity owns capture; cursor slots are transient. A grab cancels only its owner's keyboard capture, allowing the other hand to type. Preserve queued-event rejection and release ordering.
- Keep modifier consumption and repeat in the shared state model. Ordinary successful strokes consume one-shots together; repeats retain their captured chord. Win uses arm-then-tap behavior and has no locked mode.
- Generate production labels from installed Windows keyboard DLL tables and cached managed data. Do not reintroduce live `ToUnicodeEx` label queries, including worker-thread/private-desktop variants: they can inherit pending dead-key state.
- Keep physical arrangement separate from character labels. Do not infer ANSI/ISO from output at scan code `0x56`. Preserve the real HKL when using fallback labels and show the fallback notice; input errors take precedence.
- Do not log typed text.

## Rendering, settings, and dependencies

- Share geometry between rendering and hit testing. Preserve the bottom-left OpenVR to top-left Skia coordinate conversion and texture bounds U 0→1, V 1→0.
- Keep desktop and VR behavior in the shared model/renderers. Themes change visual tokens, not hit targets or input behavior; visual effects must not schedule or delay typing.
- Preserve persistent, double-buffered texture lifetime through overlay destruction and OpenVR shutdown. Keep idle/hidden rendering costs low, and distinguish renderer benchmarks from full presentation latency.
- Settings edits merge only the changed field into the latest file under a per-file mutex and replace it atomically. Failed writes must not change applied settings; malformed files retain the last working values and surface an error. Preserve stable setting IDs and migrations.
- Keep Valve's C# binding and native OpenVR library versions matched, retain dependency locks, and preserve vendor/audio licenses and credits.

## References

- [Development](docs/development.md): detailed workflows, feature implementation, benchmarks, and acceptance gaps.
- [Automatic layouts](docs/automatic-keyboard-layouts.md): current production label generation, fallback behavior, and IME scope.
- [Technical design](docs/technical-design.md): architecture rationale and interaction history; several sections describe earlier POC behavior.
- [Proof of concept](docs/proof-of-concept.md): separate reference app and its commands.
- [Layout research](docs/automatic-keyboard-layouts-research.md) and [native controls investigation](docs/native-controls-investigation.md): historical proposals/findings, not current implementation instructions.
- `experiments/`: consult the relevant README for audio preparation, effects labs, or stereo experiments.
