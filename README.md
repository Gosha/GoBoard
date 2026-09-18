# GoBoard

A Windows keyboard overlay for SteamVR, inspired by [YuuBoard](https://yuuzami.itch.io/yuuboard), with its own interface and automatic Swedish / US English layout switching to match the focused application.

## Run

Requires Windows x64, the .NET 10 SDK (see [global.json](global.json)), and SteamVR running with a connected headset.

From the repository root in PowerShell:

```powershell
.\start-goboard.ps1
```

Open the SteamVR dashboard, focus a text field in Desktop, then point at keys and pull the trigger to type. Drag the line beneath the keyboard to reposition it.

To stop:

```powershell
.\stop-goboard.ps1
```

## Documentation

- [Development, builds, tests, and previews](docs/development.md)
- [Technical design](docs/technical-design.md)
- [Proof of concept: usage, implementation, and history](docs/proof-of-concept.md)
- [Stereo dashboard experiment](experiments/SteamVR.StereoDashboard/README.md)
