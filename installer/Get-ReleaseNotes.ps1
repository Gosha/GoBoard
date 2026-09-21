param(
    [Parameter(Mandatory)][string]$Version,
    [string]$NotesDirectory = (Join-Path $PSScriptRoot '../docs/releases')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Use the same version rules as packaging before constructing a file path.
$release = & (Join-Path $PSScriptRoot 'Get-ReleaseInfo.ps1') -Version $Version
$path = Join-Path $NotesDirectory "$($release.Version).md"
if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "Missing release notes: $path. Write and commit version-specific notes before tagging; see docs/releases/README.md."
}
$notes = Get-Content -LiteralPath $path -Raw -Encoding utf8
if ([string]::IsNullOrWhiteSpace($notes)) {
    throw "Release notes are empty: $path. Write the changes and a short technical summary before tagging."
}

# Publish the reviewed text as written, without boilerplate or generated PR lists.
$notes
