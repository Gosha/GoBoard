# MSI packaging and tagged releases

GoBoard releases are explicit Git tags. Stable tags such as `v1.0.0` publish normal releases; tags such as `v1.0.1-beta.1` publish Beta prereleases. Merging to `main` does not build or publish an MSI.

Stable and Beta share one installation and settings file. Installing either channel replaces the other, even when returning to an older Stable release. Within the same channel, downgrades and separately rebuilt packages of the same version are blocked.

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
| Relevant pull request | Stable and Beta validation MSIs as workflow artifacts; no release |
| Manual workflow run | One MSI of the supplied version as a workflow artifact; no release |
| Push/merge to `main` without a tag | No run of the MSI workflow |

PR packaging is limited to production source, regression tests, assets, vendor files, installer/build scripts, solution/SDK/NuGet/build configuration, and GitHub workflows. Documentation-only, launcher-only, POC-only, and experiment-only PRs skip it.

Version checks and build/regression tests start independently. Once version resolution succeeds, package jobs start without waiting for regression tests. PRs check sample versions `1.0.0` and `1.0.1-beta.1` in parallel with separate results and independent artifact uploads; one failure does not cancel the other. Tags and manual runs build only the requested version. Publication waits for all checks, including regression tests, and only runs for a pushed version tag.

Test results upload as the `msi-test-results` TRX artifact even when tests fail. Missing results after an earlier build failure do not cause an additional upload failure. Retrying a job keeps the explicit version unchanged.

For an untagged test build, use **Run workflow**, choose the branch/ref to build, and enter a version. To build an arbitrary commit locally, check it out and use the script below. Workflow artifacts are for temporary testing; use tagged releases for the versions you want to distribute and retain. No in-app updater is configured.

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
- One all-users installation, normally `%ProgramFiles%\GoBoard`, with a folder chooser and Windows Installer elevation. The folder is remembered across channel changes.
- Shared Start menu entries **GoBoard VR**, **GoBoard Desktop**, and **GoBoard Settings**. Installed apps names include the readable release version, such as **GoBoard 1.0.1-beta.1**; its numeric version field uses the MSI mapping above.
- Shared settings at `%LOCALAPPDATA%\GoBoard\settings.json` are never owned or removed by the MSI. Future Beta settings migrations must remain compatible with Stable.
- Installation does not launch the app or enable autostart. Repository launchers and their `.runtime/app` logs and stop signals remain development facilities.

The installer accepts Windows 10 21H2 or later; use a Windows version supported by the bundled runtime. VR requires SteamVR and a headset. Self-contained releases need rebuilding and redistribution when updating the SDK/runtime for security fixes.

## Dependencies and validation

Normal dependency locks remain separate from `packages.win-x64.lock.json` used for packaging. After intentionally changing dependencies, refresh packaging locks and review the diff:

```powershell
dotnet restore src/GoBoard.App/GoBoard.App.csproj --runtime win-x64 --artifacts-path artifacts/msi/lock-refresh --force-evaluate -p:NuGetLockFilePath=packages.win-x64.lock.json -p:SelfContained=true
```

WiX SDK and UI extension are pinned together at 5.0.2. Review the [maintenance fee terms](https://docs.firegiant.com/wix/osmf/) before moving to WiX 6 or later. After changing the installer dependency, refresh its lock with `dotnet restore installer/GoBoard.Installer.wixproj --force-evaluate`.

CI runs locked restore, the Release build, regression tests, and release tests covering numeric ordering, bounds, and event-to-package selection. Every MSI build runs WiX ICE validation with warnings as errors, extracts the actual MSI without installing it, compares every file to the publish output, checks runtime/native/credit files, shortcuts, versions/provenance, cross-channel removal and transaction order, then renders VR and desktop PNGs.

Before distribution, use a disposable Windows x64 VM to verify:

1. Wizard installation, including on a machine without .NET; all shortcuts, readable version names, Desktop, and Settings as a normal user.
2. Settings and a custom install folder survive Stable → Beta → Stable, including a numerically lower destination. Verify one installed entry and actual replacement of the previous files.
3. Beta.1 → Beta.2 and Stable patch upgrades work; same-channel downgrades and separately rebuilt same-version packages are rejected. Repair using the original MSI.
4. Uninstall removes app files/shortcuts and retains settings. Check files-in-use and rollback separately.
5. Real VR, input focus, sound routing, and presentation latency.

Silent VM checks can use `msiexec /i <msi> /qn /norestart /L*v install.log` and `msiexec /x <msi> /qn /norestart /L*v uninstall.log` from an elevated terminal.

Packages are unsigned. Sign the app before packaging and the MSI afterward once a certificate is available, then regenerate the checksum. See [WiX signing guidance](https://docs.firegiant.com/wix/tools/signing/). Extraction and PNG checks do not establish successful installation/channel switching, and release publication needs a real tagged workflow run.
