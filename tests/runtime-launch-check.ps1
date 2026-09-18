param([string]$Executable = (Join-Path $PSScriptRoot '..\src\GoBoard.App\bin\Release\net10.0-windows\GoBoard.exe'))

$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
# This check briefly displays the desktop keyboard but never injects input.
if (Get-Process -Name GoBoard -ErrorAction SilentlyContinue) {
    throw 'Close existing GoBoard processes before running the launch check.'
}
$stopScript = Join-Path $PSScriptRoot '..\stop-goboard.ps1'
$process = $null
try {
    & $stopScript # An idle stop must not affect the next launch.
    $process = Start-Process -FilePath $Executable -ArgumentList '--desktop', '--seconds', '30' `
        -WorkingDirectory $env:TEMP -WindowStyle Hidden -PassThru
    $ready = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (![Threading.EventWaitHandle]::TryOpenExisting('Local\GoBoard.Runtime.Stop', [ref]$ready)) {
        if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Keyboard did not create its stop signal.' }
        Start-Sleep -Milliseconds 50
    }
    $ready.Dispose()

    # Default VR mode must exit at the common guard before initializing OpenVR.
    $duplicate = Start-Process -FilePath $Executable -ArgumentList '--seconds', '1' `
        -WorkingDirectory $env:TEMP -WindowStyle Hidden -PassThru
    if (!$duplicate.WaitForExit(5000) -or $duplicate.ExitCode -ne 0) { throw 'Duplicate launch failed.' }
    $duplicate.Dispose()
    if ($process.HasExited) { throw 'Duplicate launch disturbed the first process.' }

    & $stopScript
    if (!$process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'Graceful shutdown failed.' }
    $process.Dispose()
    $process = Start-Process -FilePath $Executable -ArgumentList '--desktop', '--seconds', '1' `
        -WorkingDirectory $env:TEMP -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'Restart after stop failed.' }
    $log = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GoBoard\runtime\goboard.log'
    if (!(Select-String -LiteralPath $log -SimpleMatch "PID $($process.Id)," -Quiet)) { throw 'Direct-launch log was not written.' }
    Write-Output 'PASS: direct launch from another directory, duplicate VR launch, script stop, restart, and logs.'
} finally {
    if ($process) {
        if (!$process.HasExited) {
            & $stopScript
            if (!$process.WaitForExit(10000)) { Write-Warning 'Test keyboard is still shutting down; its 30-second limit will close it.' }
        }
        $process.Dispose()
    }
}
