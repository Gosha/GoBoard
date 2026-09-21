# MSI packaging and tagged releases

Choose release numbers using [the versioning policy](versioning.md). This document covers build mechanics, publishing, and the numeric MSI mapping.

GoBoard releases are explicit Git tags. Stable tags such as `v1.0.0` publish normal releases; tags such as `v1.0.1-beta.1` publish Beta prereleases. Merging to `main` does not build or publish an MSI.

Stable and Beta share one installation and settings file per Windows user. Installing either channel replaces the other for that user, even when returning to an older Stable release. Within the same channel, downgrades and separately rebuilt packages of the same version are blocked.

## Publish a release

Tag the intended commit and push that tag. For example:

```powershell
git tag v1.0.0 <commit>
git push origin v1.0.0

# Later, publish previews of the next patch:
git tag v1.0.1-beta.1 <commit>
git push origin v1.0.1-beta.1
# Follow with v1.0.1-beta.2, then v1.0.1 when ready.
```

Replace `<commit>` with the commit you intend to distribute. Betas name the upcoming release; `1.0.0-beta.1` precedes `1.0.0`. Use `1.1.0-beta.1` when preparing the next minor release.

The workflow verifies the pushed tag, builds only its version, and publishes its MSI, checksum, and provenance after all checks pass. Beta releases are marked as prereleases and do not replace the Stable release's Latest designation. Tags and release assets must not be moved or overwritten. CI run numbers, retries, and the closest prior tag do not affect release versions.

Assets are uploaded to a draft before publication. If publication fails, inspect the draft; rerunning deliberately refuses to overwrite an existing release. Verify the draft's assets and source before publishing it manually, or remove only that unpublished draft and rerun the release job. Retain the original published MSI for repair; a rebuild is not an identical maintenance package.

## PR and manual builds

| Event | Result |
| --- | --- |
| Push `vMAJOR.MINOR.PATCH` | One Stable MSI and a GitHub release |
| Push `vMAJOR.MINOR.PATCH-beta.N` | One Beta MSI and a GitHub prerelease |
| Relevant pull request | One Beta validation MSI as a workflow artifact; no release |
| Manual workflow run | One Beta MSI of the supplied version as a workflow artifact; no release; Stable versions are rejected |
| Push/merge to `main` without a tag | No run of the MSI workflow |

PR packaging is limited to production source, regression tests, assets, vendor files, installer/build scripts, solution/SDK/NuGet/build configuration, and GitHub workflows. Documentation-only, launcher-only, POC-only, and experiment-only PRs skip it.

Version checks and build/regression tests start independently. Once version resolution succeeds, package jobs start without waiting for regression tests. PRs check only the sample Beta version `1.0.1-beta.1`. Stable CI packages require a pushed Stable version tag. Tags build only the requested version; manual runs accept only Beta versions. Publication waits for all checks, including regression tests, and only runs for a pushed version tag.

Test results upload as the `msi-test-results` TRX artifact even when tests fail. Missing results after an earlier build failure do not cause an additional upload failure. Retrying a job keeps the explicit version unchanged.

For an untagged test build, use **Run workflow**, choose the branch/ref to build, and enter a Beta version such as `1.0.1-beta.1`. To build an arbitrary commit locally, check it out and use the script below. Workflow artifacts are for temporary testing; use tagged releases for the versions you want to distribute and retain. No in-app updater is configured.

## Build locally

Use PowerShell on Windows x64 with the SDK selected by `global.json`:

```powershell
.\build-msi.ps1 -Version 1.0.0
.\build-msi.ps1 -Version 1.0.1-beta.1
```

The version determines the channel; there is no separate channel switch. Output examples:

- `artifacts/msi/GoBoard-1.0.0-win-x64.msi`
- `artifacts/msi/GoBoard-1.0.1-beta.1-win-x64.msi`

Each MSI has a `.msi.sha256` checksum and a `.release.json` provenance file. Metadata records the readable version, numeric MSI version, channel, source commit, and whether the checkout has uncommitted changes. It is also installed as `release.json` beside the app. App informational versions include the readable version and commit.

The script restores pinned WiX dependencies from NuGet; no global WiX installation, Visual Studio, SteamVR, or headset is needed. Uncached dependencies need network access. Each run stages fresh files under `artifacts/msi/build-<id>`, separate from live launcher outputs. Intermediates and extracted previews remain for inspection and may be removed when no process is using them.

## MSI version mapping

Windows Installer compares only three numeric fields. The release's readable version stays in tags, filenames, Installed apps names, app informational versions, and metadata. MSI ProductVersion and assembly/file versions use a separate, deterministic mapping:

```text
MSI version = major.minor.(patch * 100 + slot)
slot        = beta number for beta.1 through beta.99
slot        = 100 for a Stable release
```

| Release version | Numeric MSI version |
| --- | --- |
| `1.0.0-beta.1` | `1.0.1` |
| `1.0.0-beta.2` | `1.0.2` |
| `1.0.0` | `1.0.100` |
| `1.0.1-beta.1` | `1.0.101` |
| `1.0.1-beta.2` | `1.0.102` |
| `1.0.1` | `1.0.200` |

This preserves ordering across beta iterations, final releases, and subsequent patches. Major/minor must be 0–255, patch 0–654, and beta number 1–99, keeping the encoded number within MSI's `255.255.65535` limits. Unsupported suffixes, leading zeroes, extra fields, and overflow are rejected; values never wrap. Only Stable and `beta.N` releases are supported.

Both channels have permanent UpgradeCodes and explicitly remove the other channel across all versions. The original Stable identity is preserved. Files, component identities, shortcuts, settings, and the install-location registry key remain shared. Removal runs inside the install transaction before new files are installed, allowing replacement of higher-versioned files when switching channels and rollback on installation failure. Keep both UpgradeCodes unchanged in `installer/Package.wxs` and `installer/Get-ReleaseInfo.ps1`; package validation checks agreement.

See [SemVer precedence](https://semver.org/#spec-item-11) and [MSI ProductVersion](https://learn.microsoft.com/en-us/windows/win32/msi/productversion).

## Installed behavior

- Self-contained, untrimmed .NET 10 Windows x64 app with native SkiaSharp, GLFW, OpenVR, its license, and sound credits. No preinstalled .NET is required.
- One per-user installation, normally `%LOCALAPPDATA%\Programs\GoBoard`, without requesting administrator access or UAC elevation. The folder chooser must point to a location writable by that user. The folder is remembered in `HKCU\Software\GoBoard` across channel changes; the MSI only writes current-user registry values.
- Current-user Start menu entries **GoBoard VR** and **GoBoard Settings**. GoBoard is a VR app; MSI builds exclude the desktop keyboard host and its debugging commands. Source builds retain that host for debugging. Upgrades remove the old **GoBoard Desktop** shortcut. Installed apps names include the readable release version, such as **GoBoard 1.0.1-beta.1**; its numeric version field uses the MSI mapping above.
- Both shortcuts launch a Windows GUI executable without opening a console. Diagnostics from launches without a terminal or redirected output go to `%LOCALAPPDATA%\GoBoard\logs\goboard-<timestamp>-<pid>.log`.
- Shared settings at `%LOCALAPPDATA%\GoBoard\settings.json` are never owned or removed by the MSI. Future Beta settings migrations must remain compatible with Stable.
- Installation leaves a closed app closed and does not enable autostart. If GoBoard is running, installation closes it gracefully and restarts the installed copy in VR (including when upgrading a legacy desktop session) after files are committed. Standalone Settings windows are also closed and reopened. This includes current-user repository launches outside the installation folder. Uninstall closes only copies using the installed executable and does not restart them.

### Running-app upgrades

The MSI embeds a small WiX DTF lifecycle action, built for the .NET Framework already included in supported Windows versions. It uses the running keyboard's existing session-local stop event, so this works when upgrading older releases. Windows Restart Manager alone cannot close the VR loop or relaunch those releases: the loop does not pump Windows shutdown messages, and the app did not register a restart command. MSI file-lock detection also misses repository launches outside the installation folder.

After the user starts installation, the execute sequence captures only the current user's live GoBoard modes in the current Windows session. It requests shutdown and waits up to 15 seconds per process before files-in-use validation. A failure stops installation without force-killing the app. Diagnostic/render processes are excluded. Nested removal of an older MSI skips these actions. After commit, the action starts the installed executable, mapping legacy desktop sessions to VR, preserving standalone Settings, and dropping old launcher stop files/time limits. A process that exits within five seconds produces a separate restart warning in interactive UI and the MSI log. Failed or cancelled installations attempt to restore exited instances from their original paths after rollback; instances still running are left alone.

Build and test an unpublished MSI against an already-running keyboard:

```powershell
.\installer\Test-RunningUpgrade.ps1 -MsiPath artifacts\msi\GoBoard-1.3.1-beta.1-win-x64.msi -ExpectedVersion 1.3.1-beta.1
```

The check deliberately does not stop GoBoard first. It runs MSI unattended, requires exit code zero without a reboot, checks the original process exited, verifies installed release metadata and the new process's path/mode, and watches it for ten more seconds after MSI's startup check. Logs go to `.runtime/running-upgrade-*.log`. Repeat with VR and a legacy or source-built desktop debugging session, including launches from repository output. Both must restart as installed VR; verify the old desktop shortcut is removed. Rollback must still restore the original mode. Headset visibility and controller behavior still need manual verification; a live process alone does not prove those.

`installer/Test-InstallerRecovery.ps1 -MsiPath <installed-test-msi>` makes a separate test package that deliberately fails after `InstallExecute`. It verifies rollback and restoration of the running app, retaining its test package and log under `.runtime/installer-recovery-*`. Use an unpublished test MSI; the original package is never modified. This is an explicit local integration check, not part of CI.

Validation of this fix: all 349 regression tests passed; locked restore, Release build, and compiled MSI/payload checks passed. An unattended live VR upgrade from 1.3.0 to the unpublished 1.3.1-beta.1 stopped the original process and started the installed replacement; a repair also restarted VR. Runtime logs confirmed overlay initialization and a connected headset, and the user confirmed the keyboard reappeared normally in VR. A deliberate failure after file operations rolled back and restored VR. Desktop/source-launch lifecycle handling and interactive wizard cancellation still require separate live acceptance checks.

The installer accepts Windows 10 21H2 or later; use a Windows version supported by the bundled runtime. VR requires SteamVR and a headset. Self-contained releases need rebuilding and redistribution when updating the SDK/runtime for security fixes.

Older all-users installations must be uninstalled from Windows Installed apps before installing this per-user package. That one-time removal may require administrator approval; it retains `%LOCALAPPDATA%\GoBoard\settings.json`. The installer detects the old machine install-location key and blocks installation with these instructions. Windows Installer [major upgrades cannot change installation context](https://learn.microsoft.com/en-us/windows/win32/msi/major-upgrades). Channel replacement applies within the new per-user context; it cannot silently remove another user's or an all-users installation. `ALLUSERS` overrides are rejected.

## Dependencies and validation

Normal dependency locks remain separate from `packages.win-x64.lock.json` used for packaging. After intentionally changing dependencies, refresh packaging locks and review the diff:

```powershell
dotnet restore src/GoBoard.App/GoBoard.App.csproj --runtime win-x64 --artifacts-path artifacts/msi/lock-refresh --force-evaluate -p:NuGetLockFilePath=packages.win-x64.lock.json -p:SelfContained=true
```

WiX SDK and UI extension are pinned together at 5.0.2. Review the [maintenance fee terms](https://docs.firegiant.com/wix/osmf/) before moving to WiX 6 or later. After changing the installer dependency, refresh its lock with `dotnet restore installer/GoBoard.Installer.wixproj --force-evaluate`.

CI runs locked restore, the Release build, regression tests, and release tests covering numeric ordering, bounds, and event-to-package selection. Every MSI build runs WiX ICE validation with warnings as errors, extracts the actual MSI without installing it, compares every file to the publish output, checks the no-elevation summary flag, per-user properties/path, HKCU registry key paths, legacy migration guard, runtime/native/credit files, shortcuts, versions/provenance, cross-channel removal and transaction order, rejects desktop keyboard/debugging entry points, verifies their implementation types are absent, then renders the VR keyboard and both Settings presentations.

`New-PayloadFragment.ps1` generates one component per published file with stable relative-path identities and HKCU registry key paths, plus empty-directory cleanup. WiX's standard `Files` harvesting uses file key paths that fail per-user ICE validation. Shortcuts use an HKCU-backed component and explicit executable targets. Only [ICE91](https://learn.microsoft.com/en-us/windows/win32/msi/ice91) is suppressed: it warns that profile files cannot support an all-users install, which this package explicitly rejects. All other ICE checks remain enabled with warnings as errors.

Before distribution, use a disposable Windows x64 VM to verify:

1. Wizard and silent installation from a non-elevated standard-user account, including on a machine without .NET: no UAC prompt, current-user files/registry/shortcuts, readable version names, VR, and Settings. Verify there is no desktop keyboard shortcut and `--desktop` exits with a source-build-only diagnostic. Verify another account has no GoBoard shortcuts or installed entry.
2. Settings and a custom install folder survive Stable → Beta → Stable, including a numerically lower destination. Verify one installed entry and actual replacement of the previous files.
3. Beta.1 → Beta.2 and Stable patch upgrades work; same-channel downgrades and separately rebuilt same-version packages are rejected. Repair using the original MSI.
4. Uninstall removes app files/shortcuts and retains settings. Check files-in-use and rollback separately.
5. Real VR, input focus, sound routing, and presentation latency.
6. An older all-users installation blocks the new installer with migration instructions; uninstall the old copy, then install per-user and verify settings survive. Verify per-user repair and uninstall also work without elevation.

Silent VM checks can use `msiexec /i <msi> /qn /norestart /L*v install.log` and `msiexec /x <msi> /qn /norestart /L*v uninstall.log` from a normal, non-elevated terminal. Do not pass `ALLUSERS`.

Packages are unsigned. Sign the app before packaging and the MSI afterward once a certificate is available, then regenerate the checksum. See [WiX signing guidance](https://docs.firegiant.com/wix/tools/signing/). Extraction and PNG checks do not establish successful installation/channel switching, and release publication needs a real tagged workflow run.
