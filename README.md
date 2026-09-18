# GoBoard

A Windows keyboard overlay for SteamVR, inspired by [YuuBoard](https://yuuzami.itch.io/yuuboard), with its own interface and automatic Swedish / US English layout switching to match the focused application.

## Run

Requires Windows x64 and the .NET 10 SDK (see [global.json](global.json)). VR mode also requires SteamVR running with a connected headset.

From the repository root in PowerShell:

```powershell
.\start-goboard.ps1
```

Open the SteamVR dashboard, focus a text field in Desktop, then point at keys and pull the trigger to type. Drag the line beneath the keyboard to reposition it.

For a complete desktop application without SteamVR:

```powershell
.\start-goboard.ps1 -Desktop
```

Select a window, then click the floating keyboard to send keys. Select a text field when you want to type text; shortcuts also work without one, including while GoBoard Settings is focused. Click **Win** once to arm a shortcut, or twice to send a Windows-key tap. Drag the header to move the keyboard, click **Settings** for size and sound, or **×** to exit. It stays above other windows and preserves the target window's focus. The same layouts, modifiers, shortcuts, repeat, and saved settings work in both modes.

To stop either mode:

```powershell
.\stop-goboard.ps1
```

Open **GoBoard Settings** in the SteamVR dashboard to change keyboard size, theme, mute, volume, or sound preset. Changes apply live and are saved for the next launch. **Steam Soft**, the default theme, follows the supplied v13 mockup with shaded keys and blue accents; **Steam Flat** retains the earlier flat appearance. Both themes are also available from desktop Settings.

For the desktop settings window (works without SteamVR):

```powershell
.\settings-goboard.ps1
```

## Documentation

- [Development, builds, tests, and previews](docs/development.md)
- [Technical design](docs/technical-design.md)
- [Proof of concept: usage, implementation, and history](docs/proof-of-concept.md)
- [Stereo dashboard experiment](experiments/SteamVR.StereoDashboard/README.md)
