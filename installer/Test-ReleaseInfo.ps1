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
    @('1.0.0-beta.1', '1.0.1'),
    @('1.0.0-beta.99', '1.0.99'),
    @('1.0.0', '1.0.100'),
    @('1.0.1-beta.1', '1.0.101'),
    @('1.0.1-beta.2', '1.0.102'),
    @('1.0.1', '1.0.200'),
    @('1.0.654', '1.0.65500'),
    @('1.1.0-beta.1', '1.1.1'),
    @('2.0.0', '2.0.100'),
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
                       '1.2.3-Beta.1', '1.2.3-rc.1', '1.2.3+build', '256.0.0', '1.256.0', '1.2.655', '0.1.0', '0.1.0-beta.1')) {
    RequireRejected { & $resolver -Version $version }
}
$prInput = @{ EventName = 'pull_request'; PullRequest = '42'; RunNumber = '318'; RunAttempt = '1'; SourceRevision = ('abcdef0123' * 4) }
$pr = & $ciResolver @prInput
$devVersion = '0.0.0-dev.pr.42.build.318.1.gabcdef0123ab'
Require ($pr.Matrix.include.Count -eq 1 -and $pr.Matrix.include[0].channel -eq 'development' -and $pr.Matrix.include[0].version -ceq $devVersion) 'PR must build an identifiable Development package.'
Require ($pr.Matrix.include[0].artifact -ceq "GoBoard-$devVersion-win-x64-setup") 'PR artifact must retain build provenance.'
Require (!$pr.ReleaseTag -and !$pr.Channel -and !$pr.Version -and !$pr.Artifact) 'PR must not select a release.'
$dev = & $resolver -Version $devVersion
Require ($dev.ProductName -ceq 'GoBoard Development - PR 42 - build 318.1 - abcdef0123ab') 'Installed product identity must identify the PR, run, attempt and commit.'
Require ($dev.UpgradeCode -eq $beta.UpgradeCode -and $dev.OtherUpgradeCode -eq $stable.UpgradeCode -and !$dev.ReleaseTag) 'Development must remain replaceable by published installers without becoming a release.'
$retryInput = $prInput.Clone()
$retryInput.RunAttempt = '2'
$retryInput.Version = $devVersion # The matrix from a previous successful versions job.
$retry = & $ciResolver @retryInput
$retryInfo = & $resolver -Version $retry.Matrix.include[0].version
Require ($retryInfo.RunAttempt -eq 2 -and [version]$retryInfo.MsiVersion -gt [version]$dev.MsiVersion -and
    $retry.Matrix.include[0].artifact -ne $pr.Matrix.include[0].artifact) 'A failed-jobs-only rerun must replace stale matrix provenance with the current attempt.'
$previous = [version]'0.0.0'
foreach ($case in @(@(1, 1, '0.0.1'), @(1, 99, '0.0.99'), @(2, 1, '0.0.101'),
                    @(656, 35, '0.0.65535'), @(656, 36, '0.1.0'), @(167771, 99, '0.255.65419'))) {
    $info = & $resolver -Version "0.0.0-dev.pr.42.build.$($case[0]).$($case[1]).gabcdef0123ab"
    Require ($info.MsiVersion -eq $case[2] -and [version]$info.MsiVersion -gt $previous -and [version]$info.MsiVersion -lt [version]'1.0.0') 'Development ordering must survive retries and MSI field rollover, and stay below released Beta versions.'
    $previous = [version]$info.MsiVersion
}
foreach ($field in @('PullRequest', 'RunNumber', 'RunAttempt', 'SourceRevision')) {
    foreach ($bad in @('', '0', '01', '-1', 'abc', '1;bad')) {
        $invalid = $prInput.Clone()
        $invalid[$field] = $bad
        RequireRejected { & $ciResolver @invalid }
    }
}
foreach ($version in @('0.0.0-dev.pr.42.build.167772.1.gabcdef0123ab', '0.0.0-dev.pr.42.build.1.100.gabcdef0123ab',
                       '0.0.0-dev.pr.42.build.1.1.gABCDEF0123AB', '0.0.0-dev.pr.42.build.1.1.gabcdef0')) {
    RequireRejected { & $resolver -Version $version }
}
RequireRejected { & $ciResolver -EventName pull_request }
RequireRejected { & $ciResolver -EventName push -RefType tag -RefName "v$devVersion" }
RequireRejected { & $ciResolver -EventName workflow_dispatch -Version $devVersion }
foreach ($version in @('1.0.0', '1.0.1-beta.2')) {
    $tag = & $ciResolver -EventName push -RefType tag -RefName "v$version"
    Require ($tag.Matrix.include.Count -eq 1 -and $tag.Version -eq $version) 'Tag must build only its requested version.'
    $expectedChannel = if ($version -like '*-beta.*') { 'beta' } else { 'stable' }
    Require ($tag.Matrix.include[0].channel -eq $expectedChannel -and $tag.Channel -eq $expectedChannel -and $tag.ReleaseTag -eq "v$version" -and $tag.Artifact -eq "GoBoard-$expectedChannel-msi") 'Tag must select the matching release channel and artifact.'
}
$manual = & $ciResolver -EventName workflow_dispatch -RefType tag -RefName v9.0.0 -Version '1.0.1-beta.2'
Require ($manual.Matrix.include.Count -eq 1 -and $manual.Matrix.include[0].channel -eq 'beta' -and $manual.Version -eq '1.0.1-beta.2') 'Manual Beta input must win over the checkout ref.'
RequireRejected { & $ciResolver -EventName workflow_dispatch -Version '1.0.0' }
RequireRejected { & $ciResolver -EventName workflow_dispatch -RefType tag -RefName v1.0.0 -Version '1.0.0' }
RequireRejected { & $ciResolver -EventName push -RefType branch -RefName main }
RequireRejected { & $ciResolver -EventName push -RefType tag -RefName 'v1.0.0-rc.1' }
RequireRejected { & $ciResolver -EventName workflow_dispatch -Version '' }
Write-Output 'Release checks passed: semantic/MSI ordering, bounds, channel identity, and PR/tag/manual build selection.'
