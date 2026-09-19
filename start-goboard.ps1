param([switch]$Desktop, [switch]$BuildOnly)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\GoBoard.App\GoBoard.App.csproj'
$runtime = Join-Path $PSScriptRoot '.runtime\app'
$stopFile = Join-Path $runtime 'stop'
$pidFile = Join-Path $runtime 'goboard.pid'

if (!$BuildOnly -and (Get-Process -Name 'GoBoard.Poc' -ErrorAction SilentlyContinue)) {
    throw 'The POC is running. Close it with .\stop-poc.ps1 before starting GoBoard.'
}

if (!$BuildOnly -and (Test-Path -LiteralPath $pidFile)) {
    $existingId = [int](Get-Content -LiteralPath $pidFile)
    $existing = Get-Process -Id $existingId -ErrorAction SilentlyContinue
    if ($existing -and $existing.ProcessName -eq 'GoBoard') {
        throw 'GoBoard is already running. Use .\stop-goboard.ps1 first.'
    }
}

# Keep both modes separate from normal builds and standalone Settings. Windows
# prevents overwriting DLLs loaded by an older window, even after typing stops.
$buildName = if ($Desktop) { 'desktop-build' } else { 'vr-build' }
$buildRoot = Join-Path $PSScriptRoot "artifacts\$buildName"
$relativeExe = 'bin\GoBoard.App\release\GoBoard.exe'
$exe = Join-Path $buildRoot $relativeExe
$loaded = Get-Process -Name GoBoard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($loaded) {
    # A directly launched keyboard/settings window may outlive the PID guard.
    # Build fresh binaries without terminating that process or reusing stale code.
    $buildRoot = Join-Path $PSScriptRoot ("artifacts\$buildName-" + [Guid]::NewGuid().ToString('N'))
    $exe = Join-Path $buildRoot $relativeExe
    Write-Output "Previous build is still loaded; building into $buildRoot"
}
dotnet build $project -c Release --artifacts-path $buildRoot --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($BuildOnly) {
    Write-Output "Built GoBoard: $exe"
    return
}

New-Item -ItemType Directory -Force -Path $runtime | Out-Null
if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile }
$launchArguments = @('--stop-file', ('"' + $stopFile + '"'))
if ($Desktop) { $launchArguments = @('--desktop') + $launchArguments }
$process = Start-Process -FilePath $exe -ArgumentList $launchArguments -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $runtime 'goboard.log') -RedirectStandardError (Join-Path $runtime 'goboard.error.log')
$process.Id | Set-Content -LiteralPath $pidFile
Start-Sleep -Seconds 2
$process.Refresh()
Get-Content -LiteralPath (Join-Path $runtime 'goboard.log') -ErrorAction SilentlyContinue
if ($process.HasExited) {
    Get-Content -LiteralPath (Join-Path $runtime 'goboard.error.log') -ErrorAction SilentlyContinue
    throw 'GoBoard exited. See .runtime\app logs.'
}
Write-Output "GoBoard is running (PID $($process.Id)). Run .\stop-goboard.ps1 to close it."
