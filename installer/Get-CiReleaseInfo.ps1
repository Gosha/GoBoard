param(
    [Parameter(Mandatory)][ValidateSet('pull_request', 'push', 'workflow_dispatch')][string]$EventName,
    [string]$RefType,
    [string]$RefName,
    [string]$Version,
    [string]$PullRequest,
    [string]$RunNumber,
    [string]$RunAttempt,
    [string]$SourceRevision
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$resolver = Join-Path $PSScriptRoot 'Get-ReleaseInfo.ps1'
$selected = $null
switch ($EventName) {
    'pull_request' {
        if ($SourceRevision -cnotmatch '^[0-9a-f]{40}$') { throw 'PR builds require the full checked-out commit SHA.' }
        $developmentVersion = "0.0.0-dev.pr.$PullRequest.build.$RunNumber.$RunAttempt.g$($SourceRevision.Substring(0, 12))"
        $packages = @(& $resolver -Version $developmentVersion)
    }
    'push' {
        if ($RefType -ne 'tag' -or $RefName -cnotmatch '^v(.+)$') {
            throw 'Release builds require a pushed vMAJOR.MINOR.PATCH[-beta.N] tag.'
        }
        $selected = & $resolver -Version $Matches[1]
        if ($selected.Channel -eq 'development') { throw 'Development builds cannot be tagged releases.' }
        $packages = @($selected)
    }
    'workflow_dispatch' {
        $selected = & $resolver -Version $Version
        if ($selected.Channel -ne 'beta') {
            throw 'Stable CI builds require a pushed vMAJOR.MINOR.PATCH tag. Use a beta version for manual builds.'
        }
        $packages = @($selected)
    }
}
[pscustomobject]@{
    Channel = if ($selected) { $selected.Channel } else { '' }
    Version = if ($selected) { $selected.Version } else { '' }
    ReleaseTag = if ($selected) { $selected.ReleaseTag } else { '' }
    Artifact = if ($selected) { "GoBoard-$($selected.Channel)-msi" } else { '' }
    Matrix = @{
        include = @($packages | ForEach-Object {
            @{
                channel = $_.Channel; version = $_.Version; artifact_base = $_.ArtifactBase
                artifact = if ($_.Channel -eq 'development') { $_.ArtifactBase + '-setup' } else { "GoBoard-$($_.Channel)-msi" }
            }
        })
    }
}
