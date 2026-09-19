$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$resolver = Join-Path $PSScriptRoot 'Get-ReleaseInfo.ps1'
$ciResolver = Join-Path $PSScriptRoot 'Get-CiReleaseInfo.ps1'
function Require([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}
function RequireRejected([scriptblock]$action) {
    $rejected = $false
    try { & $action | Out-Null } catch { $rejected = $true }
    Require $rejected 'Unexpectedly accepted invalid release input.'
}

$previous = [version]'0.0.0'
foreach ($case in @(
    @('0.0.0-beta.1', '0.0.1'),
    @('0.0.0-beta.99', '0.0.99'),
    @('0.0.0', '0.0.100'),
    @('0.0.1-beta.1', '0.0.101'),
    @('0.0.1-beta.2', '0.0.102'),
    @('0.0.1', '0.0.200'),
    @('0.0.654', '0.0.65500'),
    @('0.1.0-beta.1', '0.1.1'),
    @('1.0.0', '1.0.100'),
    @('1.0.1-beta.1', '1.0.101'),
    @('1.0.1-beta.2', '1.0.102'),
    @('1.0.1', '1.0.200'),
    @('255.255.654-beta.99', '255.255.65499'),
    @('255.255.654', '255.255.65500')
)) {
    $info = & $resolver -Version $case[0]
    Require ($info.Version -eq $case[0] -and $info.MsiVersion -eq $case[1]) 'Release/MSI mapping changed.'
    Require ([version]$info.MsiVersion -gt $previous) 'MSI ordering must follow beta, final, patch, minor, and major order.'
    Require ($info.ReleaseTag -ceq "v$($case[0])") 'Release tag must preserve the full version.'
    $previous = [version]$info.MsiVersion
}
$stable = & $resolver -Version '1.0.1'
$beta = & $resolver -Version '1.0.1-beta.2'
Require ($stable.Channel -eq 'stable' -and $beta.Channel -eq 'beta') 'Channel must be derived from version.'
Require ($beta.UpgradeCode -eq $stable.OtherUpgradeCode -and $beta.OtherUpgradeCode -eq $stable.UpgradeCode) 'Channels must replace each other.'
foreach ($version in @('', 'v1.2.3', '01.2.3', '1.02.3', '1.2.03', '1.2', '1.2.3.4',
                       '1.2.3-beta', '1.2.3-beta.0', '1.2.3-beta.01', '1.2.3-beta.100',
                       '1.2.3-Beta.1', '1.2.3-rc.1', '1.2.3+build', '256.0.0', '1.256.0', '1.2.655')) {
    RequireRejected { & $resolver -Version $version }
}
$pr = & $ciResolver -EventName pull_request
Require ($pr.Matrix.include.Count -eq 2 -and !$pr.ReleaseTag) 'PR must check both variants without selecting a release.'
foreach ($version in @('1.0.0', '1.0.1-beta.2')) {
    $tag = & $ciResolver -EventName push -RefType tag -RefName "v$version"
    Require ($tag.Matrix.include.Count -eq 1 -and $tag.Version -eq $version) 'Tag must build only its requested version.'
    $manual = & $ciResolver -EventName workflow_dispatch -RefType tag -RefName v9.0.0 -Version $version
    Require ($manual.Matrix.include.Count -eq 1 -and $manual.Version -eq $version) 'Manual input must win over the checkout ref.'
}
RequireRejected { & $ciResolver -EventName push -RefType branch -RefName main }
RequireRejected { & $ciResolver -EventName push -RefType tag -RefName 'v1.0.0-rc.1' }
RequireRejected { & $ciResolver -EventName workflow_dispatch -Version '' }
Write-Output 'Release checks passed: semantic/MSI ordering, bounds, channel identity, and PR/tag/manual build selection.'
