# GoBoard keyboard sound credits

These mechanical presets contain modified excerpts of recordings offered under
[Creative Commons CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/).
CC0 permits copying, modification, and redistribution, including commercial use.
Source pages and license labels were checked on 2026-09-18.

| Preset | Recording | Creator |
| --- | --- | --- |
| Cherry MX Blue | [Typing on a keyboard, #489424](https://freesound.org/people/SamsterBirdies/sounds/489424/) | SamsterBirdies |
| Cherry MX Clear (offline archive only) | [Mechanical keyboard clicking. Different keys (1), #412923](https://freesound.org/people/humi74/sounds/412923/) | humi74 |
| Gateron Yellow | [Keyboard typing sounds: Unidentified Technics keyboard, #665075](https://freesound.org/people/zrrion/sounds/665075/) | zrrion |

Excerpts were converted to mono 44.1 kHz 16-bit PCM with DC removal,
boundary fades, and level adjustment. Cherry MX Blue uses the saved
BO3/B01/B03 pairs for normal keys and B07 for Space, Enter, Backspace and Shift,
with saved 6 ms tail fades, 70% release gain and 65% audition level.
`blue-paired-manifest.json` and `selected-blue-favorites.json` preserve the
selected export and exact provenance. No EQ was applied.
The Gateron recording uses lubricated Yellow bottoms with
MX top housings, rather than stock switches. The recordings also reflect their
keyboard cases, keycaps, microphones, and rooms.

The source hashes, exact cuts, output hashes, and reproducible offline extraction
script are in `experiments/GoBoard.SoundSamples` in the source repository.
The paired preset adds eight WAVs from the user's G02, GO1, GO3 and GO4 favorites:
four presses and their matching releases, with the saved fades, shared energy
matching, 70% release gain and 65% audition volume. Its manifest and reproducible
exporter are `paired-manifest.json` and `export-pairs.mjs` in that directory.
G05 from the same export is retained from the earlier large-key trial,
with its saved 37 ms fades and levels. The current paired preset instead uses
the four regular pairs with key-size pitch offsets on Space, Shift and Enter,
plus slight per-press pitch variation. Press and release share the same offset.
Only these eighteen short WAVs are embedded in the application. This credit file is
distributed alongside GoBoard as `SOUND-CREDITS.md`.

Cushioned wood and Soft low thud are GoBoard's synthesized presets.
