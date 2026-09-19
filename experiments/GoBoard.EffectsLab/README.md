# GoBoard Skia Effects Lab

A standalone Windows desktop experiment using the application's real Skia keyboard renderer, layout, legends and modifier state machine. It compiles linked production source files; effect code stays in this directory. It does not send keyboard input to Windows or require SteamVR.

Swedish and US legends are read from the installed Windows layout tables through the application's `WindowsLayoutProvider`, so unavailable characters have empty labels rather than the old built-in table's em-dash placeholders. Reading these tables does not activate or change the desktop input language.

The experiment explicitly uses **Steam Flat**, independent of the main application's default theme. Its sliding/flipping character crops assume flat key faces. The linked renderer includes its theme definitions and surface cache dependencies.

## Run

Requires Windows x64 and the .NET 10 SDK:

```powershell
./experiments/GoBoard.EffectsLab/start.ps1
```

Close the window to stop it. `-NoBuild` runs the last build.

## Character transitions

The **Characters** tab animates the characters that change when Shift, AltGr or Caps is pressed, such as **a → A** and **e → €**. Keycaps and modifier indicators update instantly. Enlarged previews of a, e and 2 show the same animation frame as the keyboard. The initial preset is **Lift**, at 300 ms; pointer effects start disabled to make the character motion easier to judge. Choose:

- **Crossfade:** old and new characters dissolve together.
- **Lift:** characters slide upward a few logical units while fading.
- **Flip:** characters compress vertically through the key center and expand as the new character.
- **Cascade:** changed characters dissolve in a distance-based wave from the clicked key.
- **None:** instantaneous production state changes.

Adjust transition duration, cascade spread, and lift distance independently. Timing/style parameters are captured when a transition starts. Selecting another style clears the current transition. Zero duration is instant.

**Loop character changes** demonstrates a → A → a, e → € → e, then Caps on/off. Direct buttons let you trigger these while keeping the pointer in the controls. Stop the loop to interact with the keyboard normally. The separate **Pointer** tab retains the original effects and sweep demo.

Input state changes immediately; only its presentation animates. The lower comparison always shows the target state immediately. Unchanged keys stay still. Rapid changes capture the current transition frame and continue toward the new target, including when a one-shot is consumed before its entry animation finishes. Changing layouts discards the old transition geometry.

Each primary character, Shift label, AltGr label and dead-key dot has an independent identity. Only labels whose text/appearance changes animate. For example, Shift swaps the two left-hand labels on Swedish `2` while its `@` stays exactly in place; the secondary `€` on `e` also stays still during e→E. Unchanged-label pixels are restored directly from the production bitmap, avoiding motion or antialiasing flicker.

## Try

- **Soft afterglow:** immediate entry by default, with a 220 ms ease-out on departure. Sweeping across keys leaves a short fading trail. Re-entering a key restores its highlight immediately.
- **Pointer spotlight:** a circular radial highlight follows the pointer across key faces, clipped out of gaps and the ISO Enter cutout.
- **Proximity edges:** nearby borders catch the light, including before the pointer enters that key.
- **Click feedback:** a clipped expanding ring, short press flash, and tint while the mouse button is held.
- **Combined:** all five effects, independently switchable. **Baseline** turns the experimental effects off.

Move and click on the **upper** keyboard; the lower copy mirrors the same state with an instant hover outline. Uncheck the baseline comparison for a larger single keyboard. Automatic pointer sweep provides a repeatable moving pointer with occasional local clicks.

The sliders control entry/exit time, radius in logical keyboard units (one normal key is 44 units wide), strength, and ripple duration. Timing changes apply to the next transition/click. Swedish ISO and US ANSI layouts are available.

Ctrl/Alt/Shift use production semantics: click once to arm for the next ordinary key, twice to lock, and a third time to clear. The persistent one-shot underline/ring and locked fill/padlock remain distinct from transient effects. Both sides of Ctrl and Shift share the same state. The status bar displays local chords; no OS input injection is linked. Win retains its production one-shot/second-click tap behavior.

**Save keyboard PNG + settings** exports a PNG of the current keyboard and a JSON file with the chosen layout/effect settings. Use the automatic sweep to capture a live effect while operating the dialog. Settings are an experiment record, not an application configuration file.

## Verify

```powershell
dotnet build experiments/GoBoard.EffectsLab/GoBoard.EffectsLab.csproj -c Release
dotnet experiments/GoBoard.EffectsLab/bin/Release/net10.0-windows/GoBoard.EffectsLab.dll --self-test
dotnet experiments/GoBoard.EffectsLab/bin/Release/net10.0-windows/GoBoard.EffectsLab.dll --gallery artifacts/effects-lab
dotnet experiments/GoBoard.EffectsLab/bin/Release/net10.0-windows/GoBoard.EffectsLab.dll --transition-gallery artifacts/effects-lab
dotnet experiments/GoBoard.EffectsLab/bin/Release/net10.0-windows/GoBoard.EffectsLab.dll --smoke artifacts/effects-lab
dotnet experiments/GoBoard.EffectsLab/bin/Release/net10.0-windows/GoBoard.EffectsLab.dll --benchmark
dotnet experiments/GoBoard.EffectsLab/bin/Release/net10.0-windows/GoBoard.EffectsLab.dll --animation-smoke artifacts/effects-lab
dotnet experiments/GoBoard.EffectsLab/bin/Release/net10.0-windows/GoBoard.EffectsLab.dll --pointer-benchmark artifacts/effects-lab/pointer
dotnet experiments/GoBoard.EffectsLab/bin/Release/net10.0-windows/GoBoard.EffectsLab.dll --pointer-smoke artifacts/effects-lab/pointer
```

Self-tests cover fade interruption/reentry, gaps and ISO picking, pulse expiry, both modifier modes, layout changes, rendering every preset on both layouts, and pixel equality with baseline once effects settle. The bounded desktop smoke run opens a real form, exercises its mouse coordinate conversion and Ctrl+Alt+S, saves a screenshot/result, then closes.

Transition checks additionally cover every preset's intermediate/final rendering, one-shot consumption, unchanged-key and modifier-button exclusion, pixel continuity when retargeting Shift→AltGr, instant paired Ctrl state changes, layout resets, zero duration and disabled transitions. The transition gallery compares intermediate and settled Shift/AltGr states for each style.

The benchmark measures the keyboard, instant comparison, and all three enlarged previews together. The animation smoke check uses the real WinForms timer/paint loop: a 700 ms transition must show at least ten intermediate frames spanning more than 500 ms and then settle. It writes the measured frame count and paint timings to `animation-smoke.txt`.

The pointer benchmark covers all six presets, isolated ripple/press flash, and a combined stress case. It uses the same 1140×810 canvas with keyboard, baseline and three close-ups; each case has 60 warm-up and 120 measured frames. It reports median/p95/max draw time and managed allocations, with deterministic pixel hashes for visual comparison. Default timings/radius apply except the documented stress case. The desktop pointer check exercises real mouse routing for all six presets, uses 700 ms exit/ripple durations to inspect scheduling, and verifies intermediate frames, completion, and no repainting once settled. These are draw/paint timings, not input-to-display latency or SteamVR frame rates.

`PointerChecks.cs` also verifies that each effect settles pixel-for-pixel to baseline, disabled effects don't create pulses or schedule exit frames, and flash-only animation stops when its shorter visible flash finishes. Benchmark JSON/Markdown and desktop text reports are written into the requested output folder.

## Implementation notes

`Effects.cs` contains time-driven envelopes and local input state. `LabRenderer.cs` caches the production bitmap and key paths, then draws Skia overlays. `LabForm.cs` handles desktop input, controls, comparison and a 16 ms animation timer. The timer invalidates only during animation (plus a final settled frame); steady hover does not repaint continuously. The production bitmap rebuilds only when key state or toggles change.

`CharacterLayers.cs` mirrors Panel's Steam Flat character placement into separate transparent label images. `KeyTransitions.cs` compares label identities and animates only changed labels; on interruption it snapshots each moving label separately. Unchanged labels and final settled frames use the production bitmap. Plain repeated key taps without a visual state change reuse the cached bitmap. `TransitionChecks.cs` includes pixel-for-pixel checks that @ and € stay stationary at intermediate times in all four styles, plus interrupted-transition checks and contact sheets.

After splitting character layers, the same animation benchmark measured a 5.03 ms median draw time (5.86 ms maximum). The real desktop check showed 29 intermediate frames over 696 ms, median paint 6.28 ms, with all 55 deterministic checks passing on the development machine.

Rendering caches immutable `SKImage` snapshots of the current and previous state and uses `DrawImage` for each crop. Calling `DrawBitmap` per character repeatedly converted the full mutable keyboard bitmap: the measured animated-frame median was 667 ms with the comparison and close-ups enabled. Caching reduced the same benchmark to 4.37 ms. The live desktop check showed 30 intermediate frames across 698 ms, with median paint time 5.44 ms on the development machine. Offscreen keys are also culled from close-up rendering. These figures describe this CPU desktop experiment, not guaranteed frame rates on other machines.

Pointer lights share one shader per light/view. Keys with no face effect skip clipping work, and pulse lookup avoids allocating per-key LINQ iterators. Disabled click effects create no pulse; animation scheduling considers only visible/enabled effects. In the pointer benchmark, these changes preserved all nine sampled pixel hashes and reduced managed allocation from roughly 29–44 KB/frame to 3–4 KB/frame. Combined median draw time remained about 3.2–3.3 ms (stress about 4 ms), so the measured gain is reduced allocation and redundant work, not a large frame-time speedup.

This uses Skia's CPU raster path, with a WinForms/GDI presentation step, like the desktop preview. The displayed frame time includes drawing/presentation work and may include a base bitmap rebuild; it is not a SteamVR/GPU performance measurement. No production animation changes are made by this experiment.
