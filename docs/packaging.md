# MSI packaging and release channels

GoBoard has **Stable** and **Rolling** releases, sharing one installation and one settings file. Installing either channel replaces the other channel, even if the destination has a lower numeric version. Within the same channel, newer versions upgrade older ones and downgrades are blocked.

## Build locally

Use PowerShell on Windows x64 with the SDK selected by `global.json`:

```powershell
# Stable: explicitly choose a three-part release version.
.\build-msi.ps1 -Channel stable -Version 0.2.0

# Rolling: use an increasing CI-style run number; attempts distinguish rebuilds.
.\build-msi.ps1 -Channel rolling -RunNumber 256 -RunAttempt 1
```

These produce `artifacts/msi/GoBoard-Stable-0.2.0-win-x64.msi` and `GoBoard-Rolling-1.0.1-win-x64.msi`. Each has a `.msi.sha256` checksum and a `.release.json` provenance file. The same metadata is installed as `release.json` beside the app. It records the channel, numeric version, source commit, and whether the source had uncommitted changes. App informational versions also include the commit and Rolling marker.

The script restores pinned WiX dependencies from NuGet; no global WiX installation, Visual Studio, SteamVR, or headset is needed to build. Uncached dependencies need network access. Every run stages fresh files under `artifacts/msi/build-<id>`, separate from the live VR, desktop, and settings launcher outputs. Intermediates and extracted previews remain there for inspection and may be removed when no process is using them.

## Release workflow

| Event | Result |
| --- | --- |
| Push to `main` | Rolling GitHub prerelease from that exact commit |
| Push a tag such as `v0.2.0` | Stable GitHub release, version `0.2.0` |
| Pull request | Validated packages as workflow artifacts; no release |
| Manual workflow run | Selected channel as a workflow artifact; no release |

Both channels are built and checked on every run. Only the selected channel is published on a push. A Rolling build is kept as a workflow artifact without publication if `main` has already advanced by the publication check.

To ship Stable, tag the intended commit with a canonical `vMAJOR.MINOR.PATCH` tag and push that tag. Release tags and assets should never be moved or replaced. The workflow creates a draft, uploads the MSI, checksum, and provenance, then publishes it. If publishing fails, inspect the draft; the workflow deliberately refuses to overwrite an existing release. For a failed Stable draft, verify its complete assets and source before publishing it manually, or remove only that unpublished draft and rerun the release job.

Rolling releases use unique tags such as `rolling-1.0.1`, are marked as prereleases, and do not replace the Stable release's Latest designation. There is no mutable `rolling` tag. Prior Rolling releases remain available. The pipeline does not configure an in-app updater; users switch or update by installing the desired MSI.

See [GitHub's run variables](https://docs.github.com/en/actions/reference/workflows-and-actions/variables) and [release CLI](https://cli.github.com/manual/gh_release_create) for the underlying workflow behavior.

## Version rules

Stable uses three numeric fields, limited by MSI to `255.255.65535`. It has no default release version. A tag's version is applied to both the MSI and app assembly/file metadata; suffixes and a fourth field are rejected.

Rolling uses the workflow run number and attempt:

```text
major = floor(run_number / 256)
minor = run_number % 256
patch = run_attempt
```

For example, run 255 attempt 2 is `0.255.2`; run 256 attempt 1 is `1.0.1`. Retrying a run increases its version, and the next run sorts above every attempt of an earlier run. The resolver rejects run numbers above 65535 and attempts outside 1–65535 rather than wrapping. Keep the workflow's run-number sequence intact; replacing/resetting the workflow requires planning a new version range before publishing. An older rerun still sorts below a newer run and cannot downgrade it.

Numeric versions are compared **only within a channel**. Switching from Rolling `1.0.1` to Stable `0.2.0` is allowed. Installing an older Stable over a newer Stable, or an older Rolling over a newer Rolling, is blocked. A separately rebuilt package of the same channel and version is also blocked; retain the original MSI for repair.

The channels have separate permanent UpgradeCodes, with each MSI explicitly removing the other channel's product across all versions. Files, component identities, shortcuts, settings, and the install-location registry key remain shared. Removal runs inside the install transaction before new files are installed, so cross-channel replacement can replace higher-versioned files and roll back if installation fails. Keep both UpgradeCodes unchanged in `installer/Package.wxs` and `installer/Get-ReleaseInfo.ps1`; package validation checks they agree.

## Installed behavior

- Self-contained, untrimmed .NET 10 Windows x64 app, including SkiaSharp, GLFW, OpenVR, its license, and sound credits. The target does not need .NET preinstalled.
- One all-users installation, normally `%ProgramFiles%\GoBoard`, with a folder chooser and Windows Installer elevation. The chosen folder is remembered across channel changes.
- Shared Start menu entries **GoBoard VR**, **GoBoard Desktop**, and **GoBoard Settings**.
- Installed apps shows **GoBoard** for Stable or **GoBoard (Rolling)** for Rolling.
- Shared settings remain at `%LOCALAPPDATA%\GoBoard\settings.json`. Neither channel's MSI owns or deletes this file. Future Rolling settings migrations must preserve Stable compatibility.
- Installation does not start the app or enable autostart. Repository launchers and their `.runtime/app` logs and stop signals remain development facilities.

The installer accepts Windows 10 21H2 or later; use a Windows version supported by the bundled runtime. VR requires SteamVR and a headset. Desktop does not. Self-contained releases need rebuilding and redistribution when updating the SDK/runtime for security fixes.

## Dependencies and checks

The normal app locks remain separate from `packages.win-x64.lock.json` used for packaging. After intentionally changing dependencies, refresh the packaging locks and review the diff:

```powershell
dotnet restore src/GoBoard.App/GoBoard.App.csproj --runtime win-x64 --artifacts-path artifacts/msi/lock-refresh --force-evaluate -p:NuGetLockFilePath=packages.win-x64.lock.json -p:SelfContained=true
```

WiX SDK and UI extension are pinned together at 5.0.2. Review the [maintenance fee terms](https://docs.firegiant.com/wix/osmf/) before moving to WiX 6 or later. Refresh the installer lock after changing its dependency version:

```powershell
dotnet restore installer/GoBoard.Installer.wixproj --force-evaluate
```

CI runs locked solution restore, the Release build, regression tests, and `installer/Test-ReleaseInfo.ps1` for version limits and ordering. Every MSI build runs WiX ICE validation with warnings as errors, extracts the actual MSI without installing it, compares all bytes against publish output, checks runtime/native/credit files, shortcuts, channel/provenance, cross-channel removal and transaction ordering, then renders VR and desktop PNGs from the extracted app.

Before distribution, test in a disposable Windows x64 VM:

1. Install both channels individually using the wizard, including a machine without .NET. Check all shortcuts, installed metadata, Desktop, and Settings as a normal user.
2. Change settings and choose a non-default install folder. Switch Stable → Rolling → Stable, including a numerically lower destination. Verify one Installed apps entry, retained settings/folder, and that the destination files actually replaced the prior channel.
3. Upgrade within each channel. Try a same-channel downgrade and a separately rebuilt same-version MSI; both must be rejected. Repair with the original MSI.
4. Close GoBoard and uninstall. Installed files and shortcuts should disappear while settings remain. Check files-in-use and rollback behavior separately.
5. Validate VR with SteamVR and a headset, plus actual input focus, audio, and display latency.

Silent VM checks can use `msiexec /i <msi> /qn /norestart /L*v install.log` and `msiexec /x <msi> /qn /norestart /L*v uninstall.log` from an elevated terminal.

MSIs are unsigned. Sign the app before packaging and the MSI afterward once a certificate is available, then regenerate the checksum. See [WiX signing guidance](https://docs.firegiant.com/wix/tools/signing/). Local extraction and PNG checks do not establish successful installation or channel switching; CI publication also requires a real workflow run after these changes are pushed.
