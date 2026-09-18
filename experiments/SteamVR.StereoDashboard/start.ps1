param([switch]$NoShow)
$ErrorActionPreference = 'Stop'
$runtime = Join-Path $PSScriptRoot '..\..\.runtime\stereo-dashboard'
$stopFile = Join-Path $runtime 'stop'
$pidFile = Join-Path $runtime 'experiment.pid'
if (Test-Path $pidFile) {
    $existing = Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue
    if ($existing -and $existing.ProcessName -eq 'SteamVR.StereoDashboard') { throw 'Stereo experiment is already running. Use stop.ps1 first.' }
}
dotnet build (Join-Path $PSScriptRoot 'SteamVR.StereoDashboard.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
New-Item -ItemType Directory -Force $runtime | Out-Null
if (Test-Path $stopFile) { Remove-Item -LiteralPath $stopFile }
$exe = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\SteamVR.StereoDashboard.exe'
$launchArgs = @('--stop-file', ('"' + $stopFile + '"'))
if (-not $NoShow) { $launchArgs += '--show' }
$process = Start-Process -FilePath $exe -ArgumentList $launchArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'experiment.log') -RedirectStandardError (Join-Path $runtime 'experiment.error.log')
$process.Id | Set-Content $pidFile
Start-Sleep -Seconds 3
$process.Refresh()
Get-Content (Join-Path $runtime 'experiment.log')
if ($process.HasExited) {
    Get-Content (Join-Path $runtime 'experiment.error.log')
    throw 'Stereo experiment exited. See .runtime/stereo-dashboard logs.'
}
Write-Output "Stereo Experiment is running (PID $($process.Id)). Run experiments\SteamVR.StereoDashboard\stop.ps1 to close it."
