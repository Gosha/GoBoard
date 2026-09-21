$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$reader = Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1'
function RequireRejected([scriptblock]$action, [string]$expectedMessage) {
    try { & $action | Out-Null }
    catch {
        if ($_.Exception.Message -notlike $expectedMessage) { throw }
        return
    }
    throw "Expected release notes rejection: $expectedMessage"
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$directory = Join-Path $temporaryRoot "GoBoard.ReleaseNotes.Tests-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $directory | Out-Null
try {
    $stable = "## Changes since 1.0.0`n`n- Preserve UTF-8: Å and →.`n`n## Technical summary`n`nReviewed text with ``code`` and [a link](https://example.com).`n"
    $beta = "## Changes since 1.0.1`r`n`r`n- Beta-specific change.`r`n`r`n## Technical summary`r`n`r`nPreview implementation.`r`n"
    foreach ($case in @(@('1.0.1', $stable), @('1.1.0-beta.1', $beta))) {
        $path = Join-Path $directory "$($case[0]).md"
        [IO.File]::WriteAllText($path, $case[1], [Text.UTF8Encoding]::new($false))
        $actual = & $reader -Version $case[0] -NotesDirectory $directory
        if ($actual -cne $case[1]) { throw 'Release notes must preserve reviewed Markdown exactly.' }
        # Exercise the same write used by the publishing workflow.
        $output = Join-Path $directory 'release-notes.md'
        $actual | Set-Content -LiteralPath $output -Encoding utf8 -NoNewline
        if ((Get-Content -LiteralPath $output -Raw -Encoding utf8) -cne $case[1]) {
            throw 'Publication changed the release body.'
        }
    }
    RequireRejected { & $reader -Version '1.1.0' -NotesDirectory $directory } 'Missing release notes:*'
    RequireRejected { & $reader -Version '1.1.0-beta.2' -NotesDirectory $directory } 'Missing release notes:*'
    $empty = Join-Path $directory '1.0.2.md'
    foreach ($content in @('', " `r`n`t")) {
        [IO.File]::WriteAllText($empty, $content)
        RequireRejected { & $reader -Version '1.0.2' -NotesDirectory $directory } 'Release notes are empty:*'
    }
    foreach ($version in @('v1.0.1', '../1.0.1', '1.1.0-beta.0')) {
        RequireRejected { & $reader -Version $version -NotesDirectory $directory } 'Version must be*'
    }
}
finally {
    $resolved = [IO.Path]::GetFullPath($directory)
    $expectedParent = $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $expectedParent -or
        [IO.Path]::GetFileName($resolved) -notlike 'GoBoard.ReleaseNotes.Tests-*') {
        throw "Refusing cleanup outside the release notes test directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Output 'Release notes checks passed: exact Stable/Beta content, publication text, missing/empty notes, and invalid versions.'
