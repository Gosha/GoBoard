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
try {
    $view = $modified.OpenView('SELECT `Value` FROM `Property` WHERE `Property` = ''UpgradeCode''')
    $view.Execute()
    $upgradeCode = $view.Fetch().StringData(1)
    $view.Close()
    $view = $modified.OpenView('SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action` = ''InstallExecute''')
    $view.Execute()
    $record = $view.Fetch()
    if ($record) { $executeSequence = $record.IntegerData(1) } else { $executeSequence = 6500 }
    $view.Close()
    $queries = @(
        ('UPDATE `Property` SET `Value` = ''{' + [Guid]::NewGuid().ToString().ToUpperInvariant() + '}'' WHERE `Property` = ''ProductCode'''),
        'DELETE FROM `Upgrade`',
        ('INSERT INTO `Upgrade` (`UpgradeCode`, `VersionMin`, `Attributes`, `ActionProperty`) VALUES (''' + $upgradeCode + ''', ''0.0.0'', 256, ''WIX_UPGRADE_DETECTED'')'),
        'INSERT INTO `CustomAction` (`Action`, `Type`, `Target`) VALUES (''GoBoardCheckFailure'', 19, ''Deliberate GoBoard installer recovery check.'')',
        ('INSERT INTO `InstallExecuteSequence` (`Action`, `Sequence`) VALUES (''GoBoardCheckFailure'', ' + ($executeSequence + 1) + ')')
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
$process = Start-Process msiexec.exe -ArgumentList @('/i', ('"' + $copy + '"'), '/qn', '/norestart', '/L*v', ('"' + $log + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 1603) { throw "Expected deliberate failure 1603, got $($process.ExitCode). See $log" }
$old.Refresh()
if (!$old.HasExited) { throw 'The check never shut down the original process.' }
$after = @(Get-CimInstance Win32_Process -Filter "Name='GoBoard.exe' AND SessionId=$sessionId")
if ($after.Count -ne 1 -or $after[0].ExecutablePath -ne $before[0].ExecutablePath -or
    (Get-Process -Id $after[0].ProcessId).WaitForExit(10000)) { throw "Recovery did not restore a running GoBoard process. See $log" }
$text = Get-Content -Raw -LiteralPath $log
if ($text -notmatch 'Action ended .*GoBoardCheckFailure\. Return value 3' -or
    $text -notmatch 'Action ended .*GoBoardRecoverError\. Return value 1' -or
    $text -notmatch 'GoBoard: PID \d+ remained running after startup\.') { throw "Missing deliberate failure/recovery evidence. See $log" }
Write-Output "PASS: installer failed after InstallExecute, rolled back, and restored GoBoard as PID $($after[0].ProcessId). Log: $log"
