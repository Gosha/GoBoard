$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$resolver = Join-Path $PSScriptRoot 'Get-ReleaseInfo.ps1'
function Require([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}
function RequireRejected([hashtable]$arguments) {
    $rejected = $false
    try { & $resolver @arguments | Out-Null } catch { $rejected = $true }
    Require $rejected "Unexpectedly accepted: $($arguments | ConvertTo-Json -Compress)"
}
$stable = & $resolver -Channel stable -Version 0.2.0
Require ($stable.Version -eq '0.2.0' -and $stable.ReleaseTag -eq 'v0.2.0') 'Stable version/tag changed.'
$previous = [version]'0.0.0'
foreach ($case in @(@(1, 1, '0.1.1'), @(1, 2, '0.1.2'), @(255, 65535, '0.255.65535'),
                     @(256, 1, '1.0.1'), @(65535, 65535, '255.255.65535'))) {
    $rolling = & $resolver -Channel rolling -RunNumber $case[0] -RunAttempt $case[1]
    Require ($rolling.Version -eq $case[2]) 'Rolling version mapping changed.'
    Require ([version]$rolling.Version -gt $previous) 'Rolling versions must increase across retries and run boundaries.'
    Require ($rolling.UpgradeCode -eq $stable.OtherUpgradeCode -and $rolling.OtherUpgradeCode -eq $stable.UpgradeCode) 'Channels do not replace each other.'
    $previous = [version]$rolling.Version
}
foreach ($version in @('', 'v1.2.3', '01.2.3', '1.2', '1.2.3.4', '1.2.3-beta', '256.0.0', '1.256.0', '1.2.65536')) {
    RequireRejected @{ Channel = 'stable'; Version = $version }
}
RequireRejected @{ Channel = 'rolling'; RunNumber = 0 }
RequireRejected @{ Channel = 'rolling'; RunNumber = 65536 }
RequireRejected @{ Channel = 'rolling'; RunNumber = 1; RunAttempt = 0 }
RequireRejected @{ Channel = 'rolling'; RunNumber = 1; RunAttempt = 65536 }
RequireRejected @{ Channel = 'rolling'; RunNumber = 1; Version = '1.2.3' }
Write-Output 'Release version checks passed: stable syntax, MSI limits, retry ordering, run rollover, and channel identities.'
