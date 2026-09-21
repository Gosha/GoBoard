param([Parameter(Mandatory)][string]$MsiPath)

# Explicit integration check: replace the installed test MSI, fail after its
# execution script has run, and verify that rollback restores the live app.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$root = Split-Path $PSScriptRoot -Parent
$check = Join-Path $root ('.runtime\installer-recovery-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $check | Out-Null
$copy = Join-Path $check 'failure.msi'
$log = Join-Path $check 'msi.log'
Copy-Item -LiteralPath $msi -Destination $copy
$engine = New-Object -ComObject WindowsInstaller.Installer
$modified = $engine.OpenDatabase($copy, 1)
$testProductCode = '{' + [Guid]::NewGuid().ToString().ToUpperInvariant() + '}'
try {
    $view = $modified.OpenView('SELECT `Value` FROM `Property` WHERE `Property` = ''ProductCode''')
    $view.Execute()
    $originalProductCode = $view.Fetch().StringData(1)
    $view.Close()
    if ($engine.ProductState($originalProductCode) -ne 5) {
        throw 'The supplied MSI must be the original package of a fully installed product. An advertised or broken registration is not a valid rollback baseline.'
    }
    $originalCache = $engine.ProductInfo($originalProductCode, 'LocalPackage')
    if (!(Test-Path -LiteralPath $originalCache)) { throw 'The installed product has no cached MSI. Repair it before testing rollback.' }
    $installFolder = (Get-ItemProperty HKCU:\Software\GoBoard).InstallFolder
    $baselineFiles = @{}
    foreach ($file in Get-ChildItem -LiteralPath $installFolder -File -Recurse) {
        $baselineFiles[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash
    }
    if (!$baselineFiles.Count) { throw 'The installed payload is missing. Repair it before testing rollback.' }
    $view = $modified.OpenView('SELECT `Value` FROM `Property` WHERE `Property` = ''UpgradeCode''')
    $view.Execute()
    $upgradeCode = $view.Fetch().StringData(1)
    $view.Close()
    $relatedBefore = @($engine.RelatedProducts($upgradeCode) | Sort-Object)
    if ($relatedBefore.Count -ne 1 -or $relatedBefore[0] -ne $originalProductCode) {
        throw 'Remove conflicting or stale related MSI registrations before testing rollback.'
    }
    $view = $modified.OpenView('SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action` = ''InstallExecute''')
    $view.Execute()
    $record = $view.Fetch()
    if ($record) { $executeSequence = $record.IntegerData(1) } else { $executeSequence = 6500 }
    $view.Close()
    $queries = @(
        ('UPDATE `Property` SET `Value` = ''' + $testProductCode + ''' WHERE `Property` = ''ProductCode'''),
        'DELETE FROM `Upgrade`',
        ('INSERT INTO `Upgrade` (`UpgradeCode`, `VersionMin`, `Attributes`, `ActionProperty`) VALUES (''' + $upgradeCode + ''', ''0.0.0'', 256, ''WIX_UPGRADE_DETECTED'')'),
        'INSERT INTO `CustomAction` (`Action`, `Type`, `Target`) VALUES (''GoBoardCheckFailure'', 19, ''Deliberate GoBoard installer recovery check.'')',
        # A failed rollback must never strand a fixture that deliberately fails
        # its own uninstall as well. Trigger the failure only for this test run.
        ('INSERT INTO `InstallExecuteSequence` (`Action`, `Condition`, `Sequence`) VALUES (''GoBoardCheckFailure'', ''GOBOARD_TEST_FAILURE = "1" AND NOT REMOVE = "ALL"'', ' + ($executeSequence + 1) + ')')
    )
    if (!$record) { $queries += 'INSERT INTO `InstallExecuteSequence` (`Action`, `Sequence`) VALUES (''InstallExecute'', 6500)' }
    foreach ($query in $queries) {
        $view = $modified.OpenView($query)
        $view.Execute()
        $view.Close()
    }
    $summary = $modified.SummaryInformation(1)
    $summary.Property(9) = '{' + [Guid]::NewGuid().ToString().ToUpperInvariant() + '}'
    $summary.Persist()
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) | Out-Null
    $modified.Commit()
} finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($modified) | Out-Null
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine) | Out-Null
}
# Closed COM views/records can keep the writable database handle alive until
# their RCWs are collected; release it before msiexec opens the test package.
$view = $null
$record = $null
[GC]::Collect()
[GC]::WaitForPendingFinalizers()
$sessionId = (Get-Process -Id $PID).SessionId
$before = @(Get-CimInstance Win32_Process -Filter "Name='GoBoard.exe' AND SessionId=$sessionId")
if ($before.Count -ne 1) { throw 'Start exactly one GoBoard instance before this check.' }
$old = Get-Process -Id $before[0].ProcessId
$process = Start-Process msiexec.exe -ArgumentList @('/i', ('"' + $copy + '"'), 'GOBOARD_TEST_FAILURE=1', '/qn', '/norestart', '/L*v', ('"' + $log + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 1603) { throw "Expected deliberate failure 1603, got $($process.ExitCode). See $log" }
# A relaunched process alone does not prove rollback. A previous failed check
# left advertised registrations and component owners that retained files after
# later, otherwise successful uninstalls.
$engine = New-Object -ComObject WindowsInstaller.Installer
try {
    if ($engine.ProductState($originalProductCode) -ne 5 -or $engine.ProductState($testProductCode) -ne -1 -or
        @($engine.RelatedProducts($upgradeCode) | Where-Object { $_ -ne $originalProductCode }).Count -ne 0) {
        throw "Rollback did not restore MSI registration or left the test product registered. Original: $originalProductCode; fixture: $testProductCode. See $log"
    }
    if (!(Test-Path -LiteralPath ($engine.ProductInfo($originalProductCode, 'LocalPackage')))) {
        throw "Rollback lost the original cached MSI. See $log"
    }
} finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine) | Out-Null }
$restoredFiles = @(Get-ChildItem -LiteralPath $installFolder -File -Recurse)
if ($restoredFiles.Count -ne $baselineFiles.Count) { throw "Rollback changed the installed file set. See $log" }
foreach ($file in $restoredFiles) {
    if (!$baselineFiles.ContainsKey($file.FullName) -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $baselineFiles[$file.FullName]) {
        throw "Rollback did not restore $($file.FullName). See $log"
    }
}
$old.Refresh()
if (!$old.HasExited) { throw 'The check never shut down the original process.' }
$after = @(Get-CimInstance Win32_Process -Filter "Name='GoBoard.exe' AND SessionId=$sessionId")
if ($after.Count -ne 1 -or $after[0].ExecutablePath -ne $before[0].ExecutablePath -or
    (Get-Process -Id $after[0].ProcessId).WaitForExit(10000)) { throw "Recovery did not restore a running GoBoard process. See $log" }
$text = Get-Content -Raw -LiteralPath $log
if ($text -notmatch 'Action ended .*GoBoardCheckFailure\. Return value 3' -or
    $text -notmatch 'Action ended .*GoBoardRecoverError\. Return value 1' -or
    $text -notmatch 'GoBoard: PID \d+ remained running after startup\.') { throw "Missing deliberate failure/recovery evidence. See $log" }
Write-Output "PASS: installer failed after InstallExecute, restored the original MSI registration/cache and payload, left no fixture registration, and restored GoBoard as PID $($after[0].ProcessId). Log: $log"
