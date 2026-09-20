param(
    [Parameter(Mandatory)][ValidateSet('pull_request', 'push', 'workflow_dispatch')][string]$EventName,
    [string]$RefType,
    [string]$RefName,
    [string]$Version
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$resolver = Join-Path $PSScriptRoot 'Get-ReleaseInfo.ps1'
$selected = $null
switch ($EventName) {
    'pull_request' {
        # Validate the Beta package without creating a release.
        $packages = @(& $resolver -Version '1.0.1-beta.1')
    }
    'push' {
        if ($RefType -ne 'tag' -or $RefName -cnotmatch '^v(.+)$') {
            throw 'Release builds require a pushed vMAJOR.MINOR.PATCH[-beta.N] tag.'
        }
        $selected = & $resolver -Version $Matches[1]
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
            @{ channel = $_.Channel; version = $_.Version; artifact_base = $_.ArtifactBase }
        })
    }
}
