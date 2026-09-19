param([Parameter(Mandatory)][AllowEmptyString()][string]$Version)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-beta\.([1-9][0-9]*))?$') {
    throw 'Version must be MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH-beta.N, without a v prefix.'
}
$major = [decimal]$Matches[1]
$minor = [decimal]$Matches[2]
$patch = [decimal]$Matches[3]
$isBeta = $Matches.ContainsKey(4)
$beta = if ($isBeta) { [decimal]$Matches[4] } else { 0 }
if ($major -gt 255 -or $minor -gt 255 -or $patch -gt 654 -or $beta -gt 99) {
    throw 'Supported versions: major/minor 0-255, patch 0-654, and beta number 1-99.'
}

# Reserve 100 numeric MSI slots per patch. Betas use 1-99; the final release
# uses 100. This preserves SemVer order without MSI's ignored fourth field.
$slot = if ($isBeta) { $beta } else { 100 }
$msiVersion = '{0}.{1}.{2}' -f $major, $minor, ($patch * 100 + $slot)
$stableCode = 'A8D6C499-CBD9-4DD9-9700-C5A8C9E23192'
$betaCode = '56804E91-13DF-4698-908A-604D44D7320A'
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
