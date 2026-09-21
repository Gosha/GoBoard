# Writing release notes

Before tagging a release, commit its notes as `docs/releases/<version>.md`, without the tag's `v` prefix (for example, `2.0.1.md` or `2.1.0-beta.1.md`). The release workflow requires a nonempty file for that exact version and publishes its contents unchanged. PR and manual validation builds do not require notes for their temporary build versions.

Notes should answer what changed for users and briefly explain the technical work behind it:

- Start with **Changes since <previous version>**. Describe concrete changes in behavior and fixes, grouping related commits into one outcome. Omit unchanged features and routine development housekeeping.
- Add a short **Technical summary** with useful implementation details. Explain the mechanism without restating the user-facing bullets or dumping commit/PR titles. Mention validation only when it helps assess the change, and distinguish automated checks from hardware verification.
- Add **Upgrade notes** or **Known issues** only when this release introduces a relevant action, compatibility change, or limitation.
- Include a minimal **Download and install** section: one requirements sentence, then **Installation options:** with two bullets: **Standard (.exe): Installs .NET 10 if missing.** and **Offline (.msi): Bundles .NET 10.** Keep these facts current for the release. This small amount of repetition is intentional; keep launch steps, administrator prompts, unsigned-installer warnings, and other setup details in the linked installation documentation.
- End with a comparison link for the stated baseline and a link to the tagged README's installation section. Use absolute GitHub URLs: relative repository links do not resolve correctly in release bodies. The release title and attached provenance already identify the version and source.

For Stable, compare against the preceding Stable release so users see everything since their last Stable update. For Beta, compare against the preceding Beta of the same target, or the preceding Stable for the first Beta. Explicitly name that baseline. When finalizing Stable, consolidate the changes from its Betas; do not concatenate their notes. Repetition between a preview and its final Stable release is useful for people who skipped the preview.

Review the actual diff and relevant PRs for that range; do not infer user impact from commit titles alone. See [2.0.1.md](2.0.1.md) for a concise example. Do not invent or bump a version merely to add notes; follow the [versioning policy](../versioning.md).

Preview the exact body before creating a tag:

```powershell
.\installer\Get-ReleaseNotes.ps1 -Version 2.0.1
.\installer\Test-ReleaseNotes.ps1
```

Adding a notes file does not update an existing GitHub release. Published release text can be corrected separately when requested; never move its tag or replace its assets.
