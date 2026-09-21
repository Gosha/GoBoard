# Standard and Offline setup

Choose release numbers using [the versioning policy](versioning.md). Do not bump a version merely to build either setup variant.

| Download | Payload | Prerequisites |
| --- | --- | --- |
| `GoBoard-<version>-win-x64-standard.exe` | Framework-dependent app in a per-user MSI, wrapped by WiX Burn | Uses a compatible x64 .NET 10 Desktop Runtime; offers to download Microsoft's runtime when needed |
| `GoBoard-<version>-win-x64-offline.msi` | Self-contained, untrimmed app | No internet or administrator access required |

Both install GoBoard for the current Windows user, share settings, shortcuts and installation location, and replace each other. There is one GoBoard Installed Apps entry, owned by the MSI. Uninstall never removes the shared Microsoft runtime. Retain the original Offline MSI for repair, or use Standard setup again to check prerequisites and install/repair its original payload.

## Standard setup and runtime consent

Standard setup runs a small .NET Framework 4.7.2 helper (supported Windows versions already include .NET Framework). Its x64 .NET apphost probe uses an exact copy of the published GoBoard runtime configuration. The host must resolve **both** `Microsoft.NETCore.App` and `Microsoft.WindowsDesktop.App`, requested at `10.0.0` with `Minor` roll-forward. Compatible servicing releases work; x86-only, Core-only, older major versions and preview-only installations do not satisfy the default policy. Deliberate `DOTNET_ROOT_X64` or roll-forward environment overrides apply just as they do to GoBoard; a broken override can prevent setup even after the shared runtime is installed.

If the probe succeeds, no runtime download or installer runs. Otherwise interactive setup offers **Download and continue** and **Cancel**, explaining the Microsoft dependency and possible administrator prompt. Downloads use the immutable Microsoft HTTPS URL and SHA-512 pin in `installer/runtime.xml`. The helper verifies the entire file before executing it. Microsoft's runtime installer alone handles elevation; neither the GoBoard helper nor MSI requests elevation. Run setup as the intended user, not with Run as administrator or another account's credentials.

The helper waits for Microsoft's `/install /quiet /norestart` operation, checks its exit code, then runs a fresh compatibility probe. Download/verification errors, refusal/cancellation, installation errors, and a failed post-install check prevent the GoBoard MSI from starting. Runtime exits 3010/1641 stop setup with instructions to restart Windows and run setup again; setup does not automatically restart Windows or install GoBoard before that retry. Cancellation during runtime installation waits for that process to finish safely, then prevents GoBoard installation. A successfully installed shared runtime is intentionally retained even if GoBoard setup is cancelled later.

The Standard MSI also runs WiX's native `DotNetCompatibilityCheck` before launch conditions and before stopping/removing an existing app. Extracting and directly running this internal MSI cannot bypass the runtime guard. Uninstall remains possible after the runtime has been removed.

Burn logs go to `%TEMP%` by default (`/log <path>` overrides the setup log). Detailed prerequisite diagnostics are in `%TEMP%\GoBoard-runtime-*.log`; Microsoft installer logs have a `.microsoft.log` suffix. No typed text or settings are logged. The failure page points to both log locations.

For unattended setup, `/quiet /norestart` uses an existing runtime. If none resolves, it fails without downloading unless the caller explicitly supplies `AcceptRuntimeDownload=1`. That option consents to the runtime download/installation; it does not bypass Windows elevation or error checks. Use Offline setup when administrator access or internet is unavailable. Remove either variant through Windows Installed Apps or its MSI, not the transient Standard bootstrapper's `/uninstall` option.

## WiX 5.0.2 design

WiX SDK, UI, Netfx and Bal extensions remain pinned at **5.0.2** with dependency locks. WiX 5's `DotNetCoreSearch` enumerates the highest installed version in a major family; it is not a host compatibility check. The separate apphost probe avoids conflating that enumeration with GoBoard's roll-forward policy and works before .NET 10 is available. Newer configurable-scope bundle features are not used.

Both Burn chain packages are `PerMachine="no"`, `Permanent="yes"`, `Cache="remove"` and vital. Here permanent means *not owned by Burn*: the visible per-user MSI still fully owns GoBoard uninstall/repair. WiX 5's `CalculateKeepRegistration` excludes permanent packages, so the bootstrapper removes its own registration and cached payloads after completion. There is no second bundle entry to strand when Offline replaces Standard. MSI's normal cached package remains available for maintenance.

The two variants retain the existing stable/beta UpgradeCodes and component identities. `RemoveExistingProducts` runs inside the rollback transaction before installing new files; switching to Standard removes the previous private runtime files. Same-channel downgrades remain blocked. Equal-version major upgrades are permitted only when switching variants; same-variant rebuilt packages remain blocked and the original package supports maintenance. A missing legacy variant marker means Offline. ICE61 is suppressed specifically for intentional equal-version replacement, alongside the existing per-user ICE91 exception; package checks and isolated transactions cover the guard.

Reference implementations: [WiX 5 runtime enumeration](https://github.com/wixtoolset/wix/blob/v5.0.2/src/ext/NetFx/netcoresearch/netcoresearch.cpp), [registration calculation](https://github.com/wixtoolset/wix/blob/v5.0.2/src/burn/engine/apply.cpp), [permanent package registration](https://github.com/wixtoolset/wix/blob/v5.0.2/src/burn/engine/package.cpp), and [Microsoft framework resolution](https://github.com/dotnet/runtime/blob/main/docs/design/features/framework-version-resolution.md).

## Build and release

```powershell
.\build-msi.ps1 -Version 1.3.1-beta.1
# Optional: build just one variant
.\build-msi.ps1 -Version 1.3.1-beta.1 -SetupVariant standard
.\build-msi.ps1 -Version 1.3.1-beta.1 -SetupVariant offline
```

Each invocation stages fresh outputs under `artifacts/msi/build-<id>`, isolated from live launcher directories. The default builds both variants. Public artifacts in `artifacts/msi` are the Standard EXE, Offline MSI, and each one's SHA-256 checksum and `.release.json` provenance: **six files**. The framework-dependent MSI is an internal build artifact, not a third download option. Provenance records version, numeric MSI version, channel, commit, dirty state and payload variant. Standard's external provenance also records the pinned runtime URL/version/hash.

Application dependency locks stay separate from `packages.win-x64.lock.json` used by both packaging modes. A lock update is intentional and reviewed; builds use `--locked-mode`. To update Microsoft's downloadable runtime, choose a supported servicing release from [Microsoft's .NET 10 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json), update both URL and SHA-512 in `installer/runtime.xml`, and validate its Microsoft signature without executing it. No runtime binaries are checked into the repository or embedded in Standard setup.

Explicit `vMAJOR.MINOR.PATCH` tags publish Stable releases; `vMAJOR.MINOR.PATCH-beta.N` tags publish Beta prereleases without changing Latest. Merging to main does not publish a release. Relevant PRs and manual beta runs build validation artifacts only. CI uploads both installers/checksums/provenance to a draft before publication and refuses to overwrite an existing release. Release tagging is a separate explicit action.

Windows Installer compares three numeric version fields:

```text
MSI version = major.minor.(patch * 100 + slot)
slot = beta number (1–99), or 100 for Stable
```

Thus `1.0.0-beta.1` maps to `1.0.1`, `1.0.0` to `1.0.100`, and `1.0.1-beta.1` to `1.0.101`. Supported release bounds are major 1–255, minor 0–255, patch 0–654 and beta 1–99. Stable and Beta explicitly replace the other channel regardless of numeric version; same-channel ordering is retained. See `installer/Test-ReleaseInfo.ps1` and [the versioning policy](versioning.md).

### PR Development installers

PRs build **Development** artifacts, named `GoBoard-0.0.0-dev.pr.<PR>.build.<run>.<attempt>.g<commit>-win-x64-{standard.exe,offline.msi}`. Both setup titles and the Installed apps entry show `GoBoard Development - PR <PR> - build <run>.<attempt> - <commit>`. The `.release.json` records the full source commit, PR, run, attempt, variant and dirty state. The 12-character commit in the name identifies the checked-out PR merge commit. These builds have no release tag and cannot be published by the tagged-release path; manual Beta builds retain their explicit version input.

Development MSI versions use a separate range:

```text
ordinal = (run - 1) * 100 + attempt
MSI version = 0.floor(ordinal / 65536).(ordinal % 65536)
```

Runs 1–167771 and attempts 1–99 are supported; out-of-range values fail instead of wrapping. A retry sorts after earlier attempts of its run and before later runs. The packaging job resolves its identity again on retries, including when GitHub reuses the successful version job's matrix. Local builds must supply an identity whose commit matches HEAD; use the CI-produced packages for distribution because local callers do not allocate unique workflow run numbers.

Development retains Beta's UpgradeCode so existing release installers recognize it. It replaces Stable unconditionally and Beta versions at or above `1.0.0`, but rejects newer Development versions below `1.0.0`. Released Beta packages naturally upgrade Development; Stable packages remove the other family regardless of version. Equal-version variant switching and same-variant rebuild rejection remain in force. This retains one installation, shared settings and the existing shortcuts. Reserving major 0 does not change any published GoBoard release.

`Get-PublishedSetupFixtures.ps1` downloads checksum-pinned Stable 1.4.1 and Beta 2.0.0-beta.1 packages. Pass its `Stable` and `Beta` paths to `Test-SetupSwitching.ps1` as `-PublishedStableMsi` and `-PublishedBetaMsi` when checking Development. CI does this automatically. The test preserves published upgrade rules while cloning disposable identities, registry keys, folders and shortcuts, and disabling lifecycle actions. It verifies upgrades, downgrade/rebuild rejection, variant changes, switching both ways with those published releases, and removal of fixture registrations/files/shortcuts. It does not touch the live GoBoard installation.

## Existing installation and lifecycle

GoBoard normally installs in `%LOCALAPPDATA%\Programs\GoBoard`, remembers a previous custom location in HKCU, and provides GoBoard VR and GoBoard Settings Start menu entries. Both setup variants publish with `EnableDesktopDebug=false`, excluding the desktop keyboard host and its debugging commands. Source builds retain that host for debugging. Upgrades remove the old GoBoard Desktop shortcut. Its settings under `%LOCALAPPDATA%\GoBoard` are never owned or removed by either MSI. An old all-users installation must first be uninstalled explicitly; both variants retain that migration guard and reject `ALLUSERS`.

Both variants use the existing MSI lifecycle actions. Once installation starts, they capture current-user GoBoard modes, request graceful shutdown before files-in-use checks, replace files transactionally, then relaunch the installed keyboard in VR (including when upgrading a legacy desktop session) and reopen standalone Settings. Nested removal skips the outer lifecycle actions. Failed/cancelled MSI installation attempts to restore the previous processes in their original modes after rollback. Uninstall closes only installed copies and does not relaunch them. A closed app stays closed. Runtime failure occurs before these actions, leaving the old GoBoard running.

Explicit live checks are separate from package validation:

```powershell
.\installer\Test-RunningUpgrade.ps1 -MsiPath <test-msi> -ExpectedVersion <version>
.\installer\Test-InstallerRecovery.ps1 -MsiPath <installed-test-msi>
```

Keep the existing app running when starting a live upgrade check and verify the new installed process actually relaunches. MSI completion alone is not success. Repeat with VR and a legacy or source-built desktop debugging session; both must restart as installed VR, with no desktop shortcut remaining. The recovery check deliberately fails a disposable MSI after file operations and verifies rollback/relaunch in the original mode. It requires the original MSI to be fully installed, with its maintenance cache present and no conflicting related registrations. Success also requires the original registration/cache and installed file hashes to be restored, with no test-product registration left behind. The deliberate failure is enabled only for the test transaction and never during uninstall.

A relaunched app alone does not prove rollback: broken advertised registrations can retain component ownership and leave files/shortcuts behind after later uninstalls report success. Diagnose those records through Windows Installer and repair/remove them using their original packages; do not delete shared component registry records manually.

## Automated checks and remaining acceptance

Every build performs locked restores, WiX validation, byte-for-byte MSI extraction checks, scope/registry/shortcuts/version/lifecycle checks, desktop keyboard/debugging command rejection, absence of desktop host implementation types, and VR keyboard/Settings rendering from extracted payloads (including the standalone Windows Settings window). Standard additionally checks its actual embedded MSI and Burn manifest and tests its extracted runtime helper against the installed runtime and an empty process-local .NET directory. The latter cannot modify the machine's runtime.

`Test-SetupSwitching.ps1 -OfflineMsi <path> -StandardMsi <path>` clones packages with random product/upgrade/component identities, isolated registry and shortcut paths, and disabled app lifecycle actions. Real Windows Installer transactions cover equal-version switches in both directions, newer-version upgrade, same-variant rebuild rejection, same-channel downgrade rejection, cross-channel replacement in both directions, one installed product, private-runtime removal and standalone uninstall of each variant. Removal checks verify product registrations, files, Start menu shortcuts and installation registry markers, including after the switching chain. It does not stop the user's GoBoard or use their settings. Regression tests cover runtime installation errors, verification failure, cancellation, reboot-required results and post-install compatibility checks using injected operations.

Before distributing, verify on disposable Windows x64 machines:

1. Standard with a compatible runtime: no runtime network access or elevation; per-user files/shortcuts/entry. Check standard-user and administrator accounts, x86-only, Core-only, preview-only, .NET 9/11-only, and later .NET 10 servicing versions.
2. Standard without a runtime: consent, Cancel, network failure, corrupted download, UAC refusal and alternate-admin credentials, successful download/install, post-install verification, reboot-required retry and diagnostics. Do not remove the development machine's runtime to test these paths.
3. Offline with networking disabled and a non-admin account: install, VR/Settings launch, upgrade, repair and uninstall. Verify there is no desktop keyboard shortcut and `--desktop` exits with a source-build-only diagnostic.
4. Standard ↔ Offline and Stable ↔ Beta with existing settings/custom folder and running VR, legacy or source-built desktop debugging, and Settings instances; verify one Installed Apps entry, no orphaned private runtime or Burn cache, rollback/recovery, and correct relaunch. Test same-version variant switches from a legacy Offline package as well.
5. Uninstall preserves settings and Microsoft's shared runtime; another account has no GoBoard shortcuts or per-user registration.
6. Real headset/input/focus/sound behavior. Pure tests, process liveness and rendered PNGs do not establish hardware acceptance.

Packages remain unsigned. Once signing is available, sign executable payloads before packaging, sign the final MSI/EXE and regenerate checksums. See [WiX signing guidance](https://docs.firegiant.com/wix/tools/signing/).
