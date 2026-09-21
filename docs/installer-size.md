# Installer size investigation

Measurements from 2026-09-21, using Windows x64 and the locked .NET SDK 10.0.101. The production installer remains self-contained and untrimmed, with all runtime libraries, translations, diagnostics, and native dependencies retained.

## Implemented changes

1. Embed a dedicated 256 x 256 dashboard PNG instead of the 1254 x 1254 branding master. The original renderer already reduced the master to 256 pixels before uploading it to SteamVR. The new asset is that exact output encoded as PNG; decoded RGBA pixels match byte for byte. Resource size falls from 1,398,238 to 64,646 bytes (95.4%). `SettingsPanel.Icon()` now decodes it directly. Keep the master in the repository. See [branding asset regeneration](../assets/branding/README.md).
2. Feed the existing branding ICO to the MSI `Icon` table instead of `GoBoard.exe`. WiX embeds the entire icon source separately from the compressed payload; an executable source therefore duplicates the apphost. The stream falls from 242,688 to 79,651 bytes. Keep the `GoBoard.exe` icon identifier because the shortcut target is an executable. All three shortcuts and `ARPPRODUCTICON` retain that identifier. See [WiX icon packaging](https://docs.firegiant.com/wix3/tutorial/getting-started/the-files-inside/).

## Measured package sizes

Both packages were built in the same isolated worktree from `4b38b6e`, using `build-msi.ps1 -Version 1.3.1-beta.1`, before and after the changes. These are unpublished comparison artifacts; no release version, tag, or distributed package was changed. Local package sizes may differ slightly from previous release builds due to source/provenance differences.

| Measurement | Before | After | Reduction |
|---|---:|---:|---:|
| MSI bytes | 48,652,248 | 47,161,304 | 1,490,944 |
| MSI MiB | 46.398 | 44.977 | 1.422 (3.1%) |
| Compressed cabinet bytes | 47,599,262 | 46,270,806 | 1,328,456 |
| Installed payload bytes | 146,400,243 | 145,066,488 | 1,333,755 |
| Installed file count | 288 | 288 | 0 |

MiB means 1,048,576 bytes. The installed payload total is file content, not filesystem allocation or Windows Installer cache usage. Asset savings and package savings differ because of compression, assembly alignment, metadata, and MSI container allocation.

## Remaining options

The compressed contributions below are estimates from the earlier MSI inventory, allocated from actual CAB blocks and sharing deduplicated content. They are not measured removal savings. Designer resources overlap the translations row, so these rows must not simply be added together.

| Option | Current compressed contribution | Finding and decision |
|---|---:|---|
| Windows desktop translations | About 0.96 MiB | 52 satellite DLLs: four for each of 13 languages. These contain framework control, accessibility, error, dialog, and designer resources. They do not implement keyboard layouts or GoBoard settings translations. Retained to preserve localized framework text. |
| Designer assemblies and their translations | About 1.89 MiB | 17 files, 9,675,416 bytes installed. No direct GoBoard assembly reference to these candidates was found, but `System.Drawing.dll`, `System.Design.dll`, `System.Drawing.Design.dll`, and `System.Windows.Forms.Design.Editors.dll` forward public types to `System.Windows.Forms.Design.dll`. Retained; absence of direct app references does not prove reflection or type-forwarding paths are unused. |
| Runtime diagnostics / symbol reader | About 1.56 MiB | Four files, 6,247,464 bytes installed: `Microsoft.DiaSymReader.Native.amd64.dll`, `mscordbi.dll`, and both `mscordaccore` filenames. The two DAC filenames share cabinet data already. Retained to preserve diagnostic capabilities; static managed reference inspection cannot establish that native debugging tooling does not need them. |
| OpenTK / GLFW | About 0.93 MiB | The production renderer uses them to create its hidden OpenGL context and manage persistent textures. Replacing them would require graphics lifecycle work and headset validation for a relatively small download saving. |
| SkiaSharp | About 4.48 MiB | This is the actual keyboard/settings renderer. Reducing its native binary requires a maintained custom build and feature/ABI validation; ordinary managed trimming will not shrink that native DLL. |
| Sounds | Only 170,752 raw asset bytes | The 18 embedded WAVs are small; retained without changing audio quality or credits. |

### Translation filtering

`SatelliteResourceLanguages` is a supported SDK publish option and is passed to `ResolveRuntimePackAssets` by our installed SDK. An English-only publish can retain the neutral resources and omit these language satellites; affected framework text then falls back to the neutral language. Native Windows-owned dialog localization is separate. Do not use invariant globalization as a substitute for filtering resources: keyboard/layout and culture behavior must remain intact.

The shipped languages are Czech, German, Spanish, French, Italian, Japanese, Korean, Polish, Brazilian Portuguese, Russian, Turkish, Simplified Chinese, and Traditional Chinese. Each has resources for `System.Windows.Forms`, `System.Windows.Forms.Primitives`, `System.Windows.Forms.Design`, and `Microsoft.VisualBasic.Forms`. Swedish is not among the supplied framework satellite locales; Swedish typing works independently.

Sources: [SDK language filtering](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#satelliteresourcelanguages), [resource fallback](https://learn.microsoft.com/en-us/dotnet/core/extensions/resources).

### Automatic trimming and runtime prerequisites

The app sets `UseWindowsForms=true`. Our SDK's `Microsoft.NET.RuntimeIdentifierInference.targets` explicitly errors when that is combined with `PublishTrimmed=true`, unless an internal suppression property is used. Microsoft documents WinForms trimming as unsupported because of its COM dependencies. Bypassing the error would not establish compatibility. The settings store also uses reflection-based `JsonSerializer.Serialize`/`Deserialize`; an eventual trimming experiment needs to address serialization and dependency warnings rather than suppress them.

A framework-dependent variant would move approximately 38 MiB of the current compressed runtime cost outside the app package. Users would need a compatible **x64 .NET 10 Desktop Runtime**; older .NET Framework, .NET 8/9, or the basic .NET runtime alone do not meet that requirement. A prerequisite downloader would add installation/network handling and normally elevation for system-wide runtime installation. A private runtime downloaded per user avoids that elevation but still needs lifecycle, offline, update, and repair handling. Retain the self-contained default to preserve offline installation without a separate prerequisite or administrator access.

Sources: [WinForms trimming incompatibility](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities), [deployment models](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [Windows runtime choices](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

## Verification and limits

- Locked solution restore and Release build passed with no warnings or errors; all 349 regression tests passed for the measured builds. After rebasing onto `5334e03`, locked restore and Release build passed again, and all 359 current regression tests passed.
- Original and optimized dashboard render outputs were encoded, decoded, and compared: all RGBA bytes are identical. The 256-pixel icon and desktop/VR settings previews were visually inspected. Appearance is unchanged, so existing README screenshots are unaffected.
- Both MSI builds passed WiX ICE validation and the existing extraction/provenance/registry/shortcut/runtime checks. All 288 extracted files matched their publish outputs by hash. Extracted VR and desktop keyboard render checks passed.
- The package validator now checks that the embedded icon matches the branding ICO exactly and that all shortcuts reference it. Windows `ExtractIconEx` successfully extracted large and small icons from the actual packaged stream under its `.exe` identifier.
- No MSI was installed, and no running GoBoard instance was stopped. Live install/upgrade icon presentation and the dashboard icon in a headset were not exercised. PNGs and unit tests do not establish headset legibility, controller feel, or end-to-end display latency.

The retained designer/runtime candidates were investigated through source, SDK targets, and managed PE metadata, not by deleting them and inferring safety from a successful launch. A future exclusion experiment should use an isolated publish and cover desktop/VR startup, all settings pages, input/layout/IME checks, errors, accessibility, and diagnostic workflows before changing the production payload.
