# Mechanical keyboard sample preparation

Status: the dependency **Create keyboard design mockups (2)**, task `01a0b5f5-3852-7a53-9b51-e84d3d236157` on `local`, completed successfully before integration began on 2026-09-18. The implementation preserves its General/Effects tabs and effect behavior.

This folder contains the offline preparation and provenance. Runtime copies live in `assets/audio/mechanical`; GoBoard embeds eighteen press/release WAVs, exposing two recorded presets alongside its existing sounds.

## Assets and provenance

Four extracted candidates each for Cherry MX Blue, Cherry MX Clear, and modified Gateron Yellow. The last source uses Gateron Yellow bottoms with MX tops and lubricated sliders; do not label it as an unmodified stock switch recording. See `sources.json` for exact authors, URLs, original hashes, and cut times; `prepared-manifest.json` records output hashes and sample-frame boundaries.

All sources list [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/):

- [Cherry MX Blue — SamsterBirdies](https://freesound.org/people/SamsterBirdies/sounds/489424/)
- [Cherry MX Clear — humi74](https://freesound.org/people/humi74/sounds/412923/)
- [Modified Gateron Yellow — zrrion](https://freesound.org/people/zrrion/sounds/665075/)

The original downloads remain in the user's Downloads directory. The earlier comparison recordings remain in `artifacts/keyboard-auditions/` and are louder than these prepared assets. Prepared WAVs use 44.1 kHz mono 16-bit PCM, DC removal, 2 ms attack and 10 ms tail fades, and integrated energy targeted to the existing Cushioned wood preset with a 0.16 peak ceiling. This is mathematical level matching, not a claim of equal perceived loudness. There is no pitch shifting, EQ, or denoising. These are automatically selected candidates, not ear-approved isolated down/up pairs: some may include both edges or adjacent typing.

To regenerate with NumPy and SoundFile available:

```powershell
python experiments/GoBoard.SoundSamples/prepare.py --originals C:/Users/gosha/Downloads
```

The local preparation run used the bundled Python and `--decoder-path .runtime/audio-tools`. SoundFile is only an offline decoder; the application must not acquire a Python or codec dependency.

## Integration plan and rationale

1. Inspect the finished design task and current diff. Preserve its final layout, animation behavior, and any unrelated user edits. Do not send that task instructions or modify its experiment. Adapt this plan to its actual resulting settings UI.
2. Review candidate cuts at the intended volume. Remove or recut obvious multi-key fragments; retain provenance and exact frame boundaries. Ship only these small prepared WAVs and credits, not full source recordings or audition medleys.
3. Add stable `KeySound` values for `CherryMxBlue`, `CherryMxClear`, and `GateronYellowModified` in `GoBoard.Core/BoardSettings.cs`. Keep existing serialized names, defaults, environment fallback, and unknown-value handling intact. Label the last preset "Gateron Yellow (modified)".
4. Add the twelve WAVs as embedded resources in `GoBoard.Platform.Windows`, with deterministic resource names. Preload and apply the volume to PCM data when settings change, not in the input loop. Preserve RIFF headers/sample rate and saturate numeric conversion. Missing or invalid assets must fall back safely and never prevent typing.
5. Extend `KeyAudio` to choose four variants in a deterministic round-robin for each sampled preset. Keep allocation and file I/O out of `Click`. Play the entire cut on key-down; on sampled key-up return without calling `PlaySound`, so release cannot interrupt the press or play a duplicate full keystroke. Keep synthesized presets' current down/up behavior. Muting/changing settings/disposal must stop playback before unpinning every allocated buffer. Keep rapid key-down replacement semantics initially; a polyphonic mixer is outside this change unless actual audition reveals it is necessary.
6. Expose all five presets through the shared Skia settings UI and shared hit geometry, adapting to the design task's final result. A compact previous/next selector with a centered selected preset name can fit the existing sound row; do not fix this layout before the design task finishes. Selection should preview the newly selected sound. Keep desktop and VR preview conditions in sync (`SettingsForm` and `SettingsOverlay`). Reuse the existing persisted volume/mute controls and release-to-activate interaction.
7. Update documentation and include credits in the distributable. Leave the POC separate unless required by a newly explicit request.

## Validation to perform during implementation

- Verify all embedded WAVs can be decoded as supported PCM, have nonzero content and correct resource names; volume scaling, mute, and peak safety work for every preset and variant.
- Update the existing test that iterates `CreateClick` over every enum value: synthesized releases remain distinct; sampled release is a deliberate no-op, not another full sample. Cover that a sampled release cannot stop or restart native playback through an appropriate test seam.
- Check settings persistence/restart and defaults, selector wraparound/labels, and both desktop and VR preview action paths. Retain current concurrent-settings-write tests.
- Verify no buffer is released while native playback can reference it, including preset changes, mute, disposal, and partial load failure. Exercise quick clicks and held-key releases manually; avoid an unsolicited audio-engine rewrite.
- Run the repository's documented Release build/tests, then render desktop and VR settings to inspect text fit and hit targets. Run the existing settings input check after adapting its hard-coded two-preset expectation. Headset listening remains a separate hardware acceptance check if SteamVR is unavailable.

Prepared assets pass format, sample-count, endpoint-fade, non-silence, finite-value, and peak checks. The application now has dedicated tests for all twelve embedded variants, volume scaling, selector persistence, sampled key-up behavior, playback lifetime, partial-bank failure, and WAV validation. See the task's final validation report for executed checks.

## Executed validation — 2026-09-18

- Release build with locked package restore succeeded, zero warnings/errors, in `artifacts/audio-build` to avoid replacing running applications' files.
- Full suite: 113 passed, zero failed or skipped, including existing effects regressions.
- Native `--settings-input-check` passed using disposable settings at three window sizes; all five presets, wraparound, persistence, Effects controls, and capture cancellation were exercised.
- Separate native audio smoke check at 30% volume: all twelve `PlaySound` calls succeeded; sampled release and mute returned without playback. No saved user settings were modified.
- Desktop and VR settings PNGs with the longest preset name were visually inspected in `artifacts/audio-validation`; no overlap or clipping.
- `SOUND-CREDITS.md` is present in the application's build output. Runtime WAVs match the prepared manifest hashes.

These checks confirm implementation and native API operation, not subjective audio quality or in-headset routing. The user's already-running instances were not restarted.

## User-selected paired preset — 2026-09-19

`selected-favorites.json` preserves the supplied export. Its last four entries, G02, GO1, GO3 and GO4 (excluding G05), form the additional **Gateron Yellow (paired)** preset. Run `node experiments/GoBoard.SoundSamples/export-pairs.mjs` to reproduce the eight PCM WAVs and `paired-manifest.json` from the lab contexts. The exporter verifies original/catalog context hashes and validates ranges. It uses the lab's 1 ms attack, saved 6 ms tail fades, shared pair energy gain, 70% release level and zero pitch shift. Saved 65% lab master volume is baked in; the shared GoBoard volume applies on top. The saved 200 ms hold controls settings previews only, while keyboard releases follow actual controller/mouse up events.

Four variants rotate on accepted presses, with per-pointer matching releases and cancellation on lost input. Unlike the legacy whole-keystroke presets, this one uses eight preallocated native wave-output voices to preserve overlapping tails; only an exhausted pool replaces its oldest voice. Device initialization failure falls back to synthesized presses with silent releases. Memory remains allocated until the driver unprepares each buffer, including partial initialization cleanup. See Microsoft's [audio block lifetime documentation](https://learn.microsoft.com/en-us/windows/win32/multimedia/audio-data-blocks). An unprepare failure logs and retains the unmanaged block until process exit rather than freeing in-use memory.

The user selected these cuts for VR audition. Their sound quality and VR output routing are not automatically validated by file or API checks.

The user's subsequent G05 trial assigns its saved pair to Space, Enter, Backspace and both Shift keys within the paired preset. Exported `press-5.wav` and `release-5.wav` retain G05's 37 ms fades, 70% release level and 65% master level. Other keys retain the four-pair rotation; a large-key press does not advance it. Releases use the pair captured on press, even if the pointer moves or another controller presses a different key. VR and desktop pass the accepted key's scan code through the same routing. The exporter and browser-parity checks now cover all five pairs.

Validation: Release build passed with zero warnings/errors; all 149 tests passed. Browser/export parity checks compared every output PCM frame against the lab processing (within one 16-bit rounding unit). Native audio exercised interleaved presses/releases, preview, cancellation, volume reinitialization and mute. The native settings input check passed. The existing VR instance exited through its stop file; the updated build started with a connected headset and visible overlay, with the paired preset selected and other settings preserved. The prior settings file was backed up by `.runtime/PairedAudioCheck` before selecting the preset. No headset listening judgment was made.

G05 trial validation: Release build passed with zero warnings/errors; all 155 tests passed, including the six large-key scan mappings and interleaved large/regular releases. All five pairs passed browser/export PCM parity. Native playback checks included the G05 press and release. The prior VR process exited gracefully, and the G05 build started with a connected headset and the paired preset selected. Listening acceptance is left to the user.


Current pitch trial supersedes the G05 routing: all keys use the four regular pairs. Space, Shift and Enter receive a logical-area-based pitch reduction (one semitone per doubling, capped at -3); all paired key presses add random -0.2 to +0.2 semitone variation in 0.1 steps. Each pointer retains both sample and pitch until release. Pitch cuts are cached at settings changes, with varispeed duration changes and amplitude-safe linear interpolation. G05 assets remain for comparison. Release build and all 157 tests passed, including ANSI/ISO size mapping, jitter ownership, PCM bounds and duration. Windows audio checks passed and the pitch build started in VR with a connected headset; perceptual acceptance remains with the user.


Cherry MX Blue selection — 2026-09-19: `selected-blue-favorites.json` preserves the complete latest browser export (created 14:10 UTC), downloaded by the user after automation initialization failed. BO3, B01 and B03 rotate on normal keys; B07 covers Space, Enter, Backspace and both Shift keys. `export-pairs.mjs --blue` reproduces the eight WAVs with the exact saved trims, 6 ms fades, 70% release gain, 65% master level and zero pitch shift, with hashes in `blue-paired-manifest.json`. No additional Blue pitch variation is applied. The approved Gateron audio and pitch behavior are unchanged, now labelled Gateron Yellow; the old whole-keystroke Gateron bank is no longer embedded or selectable, with saved settings migrating to the approved preset. Release build, all 163 tests, source/export PCM parity, native settings checks and Windows audio smoke checks passed.


## Approved live presets

Cherry MX Blue and Gateron Yellow were approved by the user during VR typing. Cherry MX Clear is removed from the live selector and embedded resources; old Clear settings migrate to Blue. Clear's source material and lab candidates remain archived here. Blue retains BO3/B01/B03 for normal keys and B07 for Space, Enter, Backspace and Shift.
