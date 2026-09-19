<p align="center">
  <img src="assets/branding/goboard-logo.png" alt="GoBoard logo" width="160">
</p>

# GoBoard

A keyboard for SteamVR and Windows desktop, inspired by [YuuBoard](https://yuuzami.itch.io/yuuboard). Labels follow your active Windows keyboard layout.

![GoBoard keyboard with Steam Soft on the left and Steam Flat on the right](docs/images/goboard-keyboard.png)

## Run

Requires Windows x64 and the [.NET 10 SDK](global.json). VR mode needs SteamVR and a connected headset.

From the repository root in PowerShell:

| Mode     | Command                        |
| -------- | ------------------------------ |
| SteamVR  | `.\start-goboard.ps1`          |
| Desktop  | `.\start-goboard.ps1 -Desktop` |
| Settings | `.\settings-goboard.ps1`       |
| Stop     | `.\stop-goboard.ps1`           |

In VR, open the dashboard, focus a text field in Desktop, then point and pull the trigger to type. Drag the line below the keyboard to move it or the corner grip to resize it.

On desktop, select a window and click the keys. Open **Settings** for size, themes, sounds, and effects.

![GoBoard settings with size, sound presets, volume, keyboard arrangement, and themes](docs/images/goboard-settings.png)

## More

- [Keyboard layouts and limitations](docs/automatic-keyboard-layouts.md)
- [Development and feature details](docs/development.md)
- [Contributing](AGENTS.md)
- [Sound credits](assets/audio/mechanical/CREDITS.md)

## Disclaimer

Essentially 100% vibe-coded.
