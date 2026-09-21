# Versioning policy

GoBoard uses `MAJOR.MINOR.PATCH` versions, applying [Semantic Versioning](https://semver.org/) to supported user workflows and settings compatibility. Choose versions based on what changes for users, not the size of the code diff.

## Choosing a Stable version

| Increase | When | GoBoard examples |
| --- | --- | --- |
| **MAJOR** (`1.4.2 → 2.0.0`) | An intentional compatibility break requires users to adapt, or a supported capability is removed. | Removing desktop mode, dropping a supported Windows version, requiring manual settings migration, or deliberately changing established input semantics incompatibly. |
| **MINOR** (`1.4.2 → 1.5.0`) | New capabilities preserve existing supported workflows and settings. | New input features, controller support, user-facing settings, themes, or sound presets. |
| **PATCH** (`1.4.2 → 1.4.3`) | Fixes or improvements preserve the intended behavior without adding a substantial capability. | Input bugs, crashes, performance improvements, visual polish, or installer fixes. |

A bug fix remains a patch even when it changes behavior users experienced: correcting unintended behavior differs from deliberately changing the supported contract. Conversely, an incompatible change does not become a patch just because its implementation is small.

An automatic settings migration that preserves preferences and supported behavior does not inherently require a major version. Stable and Beta share settings, so compatibility when switching back to Stable must also be considered.

## Release decisions

- Choose the highest required bump across all changes since the last Stable release.
- Increasing major resets minor and patch to zero. Increasing minor resets patch to zero.
- Version releases, not commits. Refactors, tests, documentation, and CI changes do not automatically require a release. If an internal change produces a user-facing fix or capability, classify that outcome using the table above.
- Do not bump versions, create tags, or publish releases automatically for each change. Choose a version when preparing a release, and create or push a release tag only as part of an explicitly requested release.
- Never change a published version's source, tag, or distributed assets. Corrections require a new version. Keep the original MSI available for repair.

## Beta versions

Choose the intended final version before publishing a Beta. Use `MAJOR.MINOR.PATCH-beta.N`, starting at `beta.1`, and increase `N` for each new published Beta build of that target.

For example:

```text
1.0.0
1.1.0-beta.1   First preview of a new feature
1.1.0-beta.2   Another published preview
1.1.0         Final Stable release
1.1.1         Subsequent bug fix
```

A Beta names the upcoming release, not the previous Stable release. After `1.0.0`, a preview of the next patch is `1.0.1-beta.1`; `1.0.0-beta.1` sorts before `1.0.0`.

Finalizing removes the Beta suffix without incrementing the target version. If the scope changes enough to require a different target version, start that target at `beta.1`. CI run numbers and retries do not choose or increment release versions.

## Packaging and publication

Git tags add a `v` prefix: `v1.0.0` for Stable and `v1.0.1-beta.1` for Beta. Tagged releases are distinct from temporary PR or manual-build artifacts.

[MSI packaging and tagged releases](packaging.md) documents publishing commands, supported version bounds, and the numeric MSI mapping. The readable release version expresses this policy; the encoded installer version is an implementation detail and must not determine whether a change is major, minor, or patch.

## Development builds

PR installers belong to the **Development** channel. They do not predict the next release or consume Beta numbers. Their build identity is `0.0.0-dev.pr.<PR>.build.<run>.<attempt>.g<12-character-commit>`, for example `0.0.0-dev.pr.42.build.318.1.gabcdef0123ab`. Artifact filenames, setup titles, Installed apps names and packaged provenance identify the source. The commit is the checkout actually built (GitHub's PR merge commit), not necessarily the PR branch head.

The Windows setup workflow's run number orders Development builds across PRs; the attempt orders retries of the same run. Retrying an older run does not make it newer than a later run. Re-running only failed jobs still creates a new attempt identity. Do not reset/reuse the workflow counter or use another workflow's independent counter for distributable Development packages.

MSI major **0** is reserved for Development; release majors are **1–255**. This keeps Development below all published Beta versions. Development shares Beta's installer upgrade family for compatibility with already-published installers, while retaining its own visible channel and numeric range. Installing a Development build replaces Stable or Beta, and installing a release replaces Development. Only newer Development builds can replace an installed Development build (or the other setup variant at the same version). To try an older build, uninstall the current one first; settings are retained. See [packaging](packaging.md) for bounds and switching checks.
