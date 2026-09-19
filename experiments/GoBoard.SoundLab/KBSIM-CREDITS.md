# Ready-made press/release samples

Source: [tplai/kbsim](https://github.com/tplai/kbsim), copyright (c) Thomas Lai.
Pinned revision: `ba103f3b0afa9dab80447aa2e7e2ed80b6bd80e4`.
The upstream repository's [MIT license](upstream/kbsim/LICENSE.md) is retained in full.

Imported packs: Gateron Black Ink (`blackink`), Holy Panda (`holypanda`), Topre (`topre`).
Each pack provides five ordinary-key press files sharing one generic release file,
plus separate press/release files for Space, Enter and Backspace. This creates eight
audition pairs from twelve unique source files per pack. Names follow the upstream
mapping; the ordinary files are not claimed to be five independently recorded keys.

Original MP3s and mapping files are retained under `upstream/kbsim`. Every downloaded
file is hashed in `kbsim-source-hashes.json`. `import-kbsim.ps1` downloads only those
assets and the license from the pinned commit; it never executes upstream code.
`prepare-kbsim.py` verifies the frozen hashes and converts offline with NumPy and
SoundFile. All original files decode as mono 44.1 kHz.

The lab's WAVs preserve every decoded frame without normalization, resampling or
transient trimming, quantized to PCM16. A synthetic 40 ms gap and sub-millisecond
zero padding join press and release for the existing waveform editor. This is an
assembled preview, not a continuous recording. Playback boundaries are preset to
the separate files; live holds trigger release on key-up regardless of the display
gap. Default processing uses a 1 ms edge fade, shared pair energy matching, 100%
release gain and overlapping tails. These can be adjusted in the lab.

Exports identify `sourceKind: separate-press-release-files` and contain both source
file hashes, assembly boundaries and editor settings. They deliberately omit absolute
recording timestamps. Existing continuous-source favorite exports are unchanged.
An application importer must understand this source kind before integrating a favorite.

Numerical checks and waveform inspection do not establish perceived sound quality.
These packs still need the user's listening comparison.

Validation: `node check.mjs` passes for the original catalog and all 24 added pairs,
including asset hashes, non-silent finite audio on both edges, full-file ranges,
hold/release timing, reset, saved previews and export provenance. The local server
served the page, supplemental catalog and new WAV successfully. All 24 assembled
waveforms were visually inspected in `kbsim-waveform-audit.png`. Black Ink Enter's
release is particularly quiet (peak −38.35 dBFS); Holy Panda's ordinary release is
much quieter than its press. Topre ordinary files have visibly longer, irregular
tails. These are preserved upstream characteristics, not listening-approved cuts.
Native browser layout and perceptual listening were not validated in this pass.
