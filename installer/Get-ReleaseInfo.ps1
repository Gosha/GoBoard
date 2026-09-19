param(
    [ValidateSet('stable', 'rolling')][string]$Channel = 'stable',
    [string]$Version,
    [ValidateRange(0, 65535)][long]$RunNumber = 0,
    [ValidateRange(1, 65535)][int]$RunAttempt = 1
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Channel = $Channel.ToLowerInvariant()

if ($Channel -eq 'rolling') {
    if ($Version) { throw 'Rolling versions come from RunNumber and RunAttempt; do not pass Version.' }
    if ($RunNumber -lt 1) { throw 'Rolling builds require a positive RunNumber.' }
    # Both MSI's first fields are limited to 255. Preserve ordering across retries
    # and the 255 -> 256 run boundary without using MSI's ignored fourth field.
    $Version = '{0}.{1}.{2}' -f [long][Math]::Floor($RunNumber / 256), ($RunNumber % 256), $RunAttempt
}
if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Stable builds require Version in major.minor.patch form, without a prefix or suffix.'
}
$parts = $Version.Split('.')
if ([decimal]$parts[0] -gt 255 -or [decimal]$parts[1] -gt 255 -or [decimal]$parts[2] -gt 65535) {
    throw 'MSI version must fit 255.255.65535.'
}

# Preserve the original stable identity. Cross-channel Upgrade rows remove the
# other product, while each channel retains its own downgrade protection.
$stableCode = 'A8D6C499-CBD9-4DD9-9700-C5A8C9E23192'
$rollingCode = '56804E91-13DF-4698-908A-604D44D7320A'
$isRolling = $Channel -eq 'rolling'
$label = if ($isRolling) { 'Rolling' } else { 'Stable' }
[pscustomobject]@{
    Channel = $Channel
    Version = $Version
    ProductName = if ($isRolling) { 'GoBoard (Rolling)' } else { 'GoBoard' }
    UpgradeCode = if ($isRolling) { $rollingCode } else { $stableCode }
    OtherUpgradeCode = if ($isRolling) { $stableCode } else { $rollingCode }
    ArtifactBase = "GoBoard-$label-$Version-win-x64"
    ReleaseTag = if ($isRolling) { "rolling-$Version" } else { "v$Version" }
}
