# GoBoard sound lab

A standalone local audition tool. It does not change GoBoard code, production audio assets, or saved app preferences.

Run `./experiments/GoBoard.SoundLab/start.ps1` from the repository root, then open http://127.0.0.1:8767/.
Python 3 serves only this experiment, on loopback. There are no CDN dependencies, analytics, uploads, or backend writes.
The server PID is printed and recorded in `.runtime/sound-lab/server.pid`.

## Try it

Revision 6 adds **Gateron Black Ink**, **Holy Panda**, and **Topre**: eight ready-made
press/release pairs each. Select a pack, then hold the preview button or type in the
field. Release plays on key-up. Full source-file boundaries are already set; Reset
restores them. Ordinary 0–4 share one generic release per pack. The old recording
catalog and favorites remain available. See [KBSIM-CREDITS.md](KBSIM-CREDITS.md) for
the MIT license, source hashes, offline conversion and export details. These new
packs are lab-only and do not modify the GoBoard VR preset.

1. Start with Gateron G03, or select another candidate. There are eight fresh candidate windows and four existing-source windows per recording (24 new + 12 baseline contexts).
2. Play the variation. Compare Short, More decay, and Wide context, or adjust the start/end, tail fade, and pitch directly. These are source excerpts, not guaranteed individual complete keystrokes; longer candidates may contain multiple attacks. Hear source context when judging boundaries.
3. Compare against any of the four exact currently shipped WAVs from that family. The A/B control plays the current sample first, then the edited variation.
4. Type in the dedicated field, or play twelve evenly spaced presses. Toggle overlapping tails to compare against the app's interruption behavior. Typing outside this field does not trigger sound, and held-key repeat is ignored. Escape stops active and scheduled sound.
5. Save several variations with notes. Favorites persist in this browser's local storage. Export favorites to JSON to keep/share exact source times, pitch, fades, level settings, and notes. No app integration happens automatically.

## Press / release experiments (revision 4)

Waveforms zoom to fit by default again. The checkbox switches to fixed amplitude; the scale label makes visual amplification explicit, and raw source peak/RMS levels and activity warnings remain independent of zoom.

Enable **Split into press / release** on a window containing two sounds (C11 is one visually promising starting point, not a listening-approved pair). Drag or click between the attacks on the waveform to position both boundaries. **Press ends / split** trims the first part, and **Release starts** can move later to remove the quiet gap before the second part. Start and End still bound the whole selection. Each part receives its own edge fades; the split starts at the midpoint and is not an automatic press/release classification.

Use **Press only** and **Release only** to check the cuts. **Play press → release** and A/B use **Preview hold time**, measured from press onset to release onset. Bursts schedule twelve pairs at the selected press spacing, including overlapping held keys when the hold is longer than the spacing. With overlapping tails disabled, each event cuts off the previous voice.

Hold the on-screen button with pointer, Space or Enter, or type in the text field: keydown plays press and keyup plays release. Each held key retains its own sample/settings snapshot. Repeated keydown is ignored. Stop, sample changes, focus loss and page hiding cancel held keys and pending playback without a delayed release. The hold-time slider affects automated previews only; live holds use actual key-up timing.

The pair shares one energy-matching gain, preserving the recording's relative press/release level; **Release volume** applies an additional multiplier (default 70%). It does not independently normalize a faint return sound. Weak-part warnings remain visible; a second transient might be a neighboring keystroke rather than release. These are audition tools, not verified event labels.

Favorites and schema-version-2 JSON exports include split boundaries, release volume and preview hold time, plus absolute source timestamps for both cuts. Legacy favorites still load as whole-sample variations. This change only affects the standalone lab.

Revision 5 adds a hold-to-press button directly to each saved variation. Pointer, Space and Enter preview the saved sample, split, pitch, release level and volume without loading or changing the editor. Unsplit favorites play only their saved press sound and are labelled accordingly. Unavailable/retired sources cannot be previewed. Stop, focus loss and removing a held variation cancel its pending release.

## Sources and generation

All three sources are CC0, attributed in the source links in the UI and `samples.json`. Originals and SHA-256 hashes are inherited from `../GoBoard.SoundSamples/sources.json`. Run `generate.py` with NumPy and SoundFile to reproduce the 48 small WAVs (36 source contexts + 12 exact app baselines). It verifies the original recording hashes before extraction.

Fresh candidates use envelope groups and local attack windows, favoring quiet surroundings. Every core must have a peak at least −30 dBFS and a peak-to-RMS ratio at least 14 dB. These thresholds screen weak noise in these recordings; they do not prove a complete isolated keystroke or listening quality. Wider windows may retain a press/release pair or neighboring keys. The four previous locations are excluded from the eight fresh choices. `selections.json` freezes IDs and source times so regenerating never silently remaps favorites.

Revision 3 retires Clear C01, C07 and C08 (background noise without clear attacks), replacing them with C09–C11. The three rejected context WAVs remain as regression fixtures in addition to the 48 active WAVs. All catalog waveforms were inspected at a common scale; the replacements show attacks and decay. The UI now uses fixed full-scale amplitude and reports selected-source peak/RMS levels, instead of magnifying each context to its own peak. Weak selected ranges get a warning and are never boosted by energy matching. Retired or changed-source favorites remain exportable with loading disabled.

Context WAVs are mono PCM at 44.1 kHz with DC removed and preserved extra sound around each candidate. UI trimming applies a 1 ms attack fade, adjustable tail fade, optional energy matching toward the existing Cushioned wood energy, and a peak cap. Pitch shifts change playback duration as well. Normalization is approximate and does not ensure equal perceived loudness.

Use a consistent master volume for comparisons. Browser Web Audio allows overlapping voices, unlike GoBoard's current PlaySound path; this tool is for choosing source cuts and comparing behavior, not claiming native playback parity.

## Checks

`node check.mjs` verifies all 48 active WAVs, source-context hashes/ranges, attack screening, three rejected-noise regressions, no noise amplification, trimming/fades, A/B scheduling, interrupted bursts, typing isolation, stop/cancel, and favorite save/load/remove/export (including retired or changed sources) using a simulated DOM and audio backend. The local server also served the real page, catalog and initial G03 audio successfully. Browser automation was unavailable, so visual layout and real browser audio still need manual acceptance; these checks do not substitute for listening.

Revision 4 also checks waveform scaling, split ranges and shared gain, pair timing, separate-part previews, held keys/repeat suppression, keyup before audio initialization finishes, cancellation while initialization is pending, interleaved 24-event bursts and paired-favorite round trips.
