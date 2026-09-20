param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$sessionId = (Get-Process -Id $PID).SessionId
function Get-LiveKeyboard {
    @(Get-CimInstance Win32_Process -Filter "Name='GoBoard.exe' AND SessionId=$sessionId" |
        Where-Object { $_.CommandLine -notmatch '--(settings|render|.*check|steamvr|stop)(\s|$)' })
}
$before = Get-LiveKeyboard
if ($before.Count -ne 1) { throw 'Start exactly one GoBoard keyboard before this check. The check deliberately does not close it before installing.' }
$desktop = $before[0].CommandLine -match '(?:^|\s)--desktop(?:\s|$)'
$old = Get-Process -Id $before[0].ProcessId
$oldStarted = $old.StartTime
$log = Join-Path $root ('.runtime\running-upgrade-' + [Guid]::NewGuid().ToString('N') + '.log')
Write-Output "Installing over running PID $($old.Id); desktop=$desktop; log=$log"
$installerProcess = Start-Process -FilePath msiexec.exe -ArgumentList @('/i', ('"' + $msi + '"'), '/qn', '/norestart', '/L*v', ('"' + $log + '"')) -WindowStyle Hidden -Wait -PassThru
if ($installerProcess.ExitCode -ne 0) { throw "MSI failed or requested a reboot: $($installerProcess.ExitCode). See $log" }
$old.Refresh()
if (!$old.HasExited) { throw "MSI succeeded but the original PID $($old.Id) is still running. See $log" }
$installFolder = (Get-ItemProperty HKCU:\Software\GoBoard).InstallFolder
$installedExe = Join-Path $installFolder 'GoBoard.exe'
$release = Get-Content -Raw -LiteralPath (Join-Path $installFolder 'release.json') | ConvertFrom-Json
if ($release.version -ne $ExpectedVersion) { throw "Expected $ExpectedVersion, installed $($release.version)." }
$live = Get-LiveKeyboard
if ($live.Count -ne 1 -or $live[0].ExecutablePath -ne $installedExe) {
    throw "MSI completed without exactly one keyboard running from the installed folder. See $log"
}
if (($live[0].CommandLine -match '(?:^|\s)--desktop(?:\s|$)') -ne $desktop) { throw 'Installer changed keyboard mode.' }
$new = Get-Process -Id $live[0].ProcessId
if ($new.StartTime -le $oldStarted -or $new.WaitForExit(10000)) { throw 'Relaunched keyboard did not survive startup.' }
$text = Get-Content -Raw -LiteralPath $log
if ($text -notmatch 'GoBoard: all captured instances exited before file replacement\.' -or
    $text -notmatch 'GoBoard: PID \d+ remained running after startup\.' -or
    $text -match 'GoBoard was installed, but could not restart') { throw "Lifecycle evidence missing or restart failed. See $log" }
Write-Output "PASS: PID $($old.Id) exited; $ExpectedVersion is running as new PID $($new.Id) in the same mode from $installedExe."
Write-Output "MSI log: $log"
