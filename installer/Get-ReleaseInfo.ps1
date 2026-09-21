param([Parameter(Mandatory)][AllowEmptyString()][string]$Version)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$stableCode = 'A8D6C499-CBD9-4DD9-9700-C5A8C9E23192'
$betaCode = '56804E91-13DF-4698-908A-604D44D7320A'
if ($Version -cmatch '^0\.0\.0-dev\.pr\.([1-9][0-9]*)\.build\.([1-9][0-9]*)\.([1-9][0-9]*)\.g([0-9a-f]{12})$') {
    $pr = [long]$Matches[1]
    $run = [long]$Matches[2]
    $attempt = [long]$Matches[3]
    $commit = $Matches[4]
    if ($run -gt 167771 -or $attempt -gt 99) {
        throw 'Development versions support CI runs 1-167771 and attempts 1-99; never wrap or reuse a build number.'
    }
    # Reserve MSI major 0 for Development. Keep the existing Beta upgrade
    # family so published Stable/Beta installers can replace these packages.
    $ordinal = ($run - 1) * 100 + $attempt
    return [pscustomobject]@{
        Channel = 'development'
        Version = $Version
        MsiVersion = '0.{0}.{1}' -f [Math]::Floor($ordinal / 65536), ($ordinal % 65536)
        ProductName = "GoBoard Development - PR $pr - build $run.$attempt - $commit"
        UpgradeCode = $betaCode
        OtherUpgradeCode = $stableCode
        ArtifactBase = "GoBoard-$Version-win-x64"
        ReleaseTag = ''
        PullRequest = $pr
        RunNumber = $run
        RunAttempt = $attempt
        Commit = $commit
    }
}

if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-beta\.([1-9][0-9]*))?$') {
    throw 'Version must be MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH-beta.N, without a v prefix.'
}
$major = [decimal]$Matches[1]
$minor = [decimal]$Matches[2]
$patch = [decimal]$Matches[3]
$isBeta = $Matches.ContainsKey(4)
$beta = if ($isBeta) { [decimal]$Matches[4] } else { 0 }
if ($major -lt 1 -or $major -gt 255 -or $minor -gt 255 -or $patch -gt 654 -or $beta -gt 99) {
    throw 'Supported release versions: major 1-255, minor 0-255, patch 0-654, and beta number 1-99. MSI major 0 is reserved for Development.'
}

# Reserve 100 numeric MSI slots per patch. Betas use 1-99; the final release
# uses 100. This preserves SemVer order without MSI's ignored fourth field.
$slot = if ($isBeta) { $beta } else { 100 }
$msiVersion = '{0}.{1}.{2}' -f $major, $minor, ($patch * 100 + $slot)
[pscustomobject]@{
    Channel = if ($isBeta) { 'beta' } else { 'stable' }
    Version = $Version
    MsiVersion = $msiVersion
    ProductName = "GoBoard $Version"
    UpgradeCode = if ($isBeta) { $betaCode } else { $stableCode }
    OtherUpgradeCode = if ($isBeta) { $stableCode } else { $betaCode }
    ArtifactBase = "GoBoard-$Version-win-x64"
    ReleaseTag = "v$Version"
}
