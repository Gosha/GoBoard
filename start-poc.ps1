$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\GoBoard.Poc\GoBoard.Poc.csproj'
$runtime = Join-Path $PSScriptRoot '.runtime'
$stopFile = Join-Path $runtime 'stop'
$pidFile = Join-Path $runtime 'poc.pid'
if (Test-Path $pidFile) {
    $existing = Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue
    if ($existing -and $existing.ProcessName -eq 'GoBoard.Poc') { throw 'The POC is already running. Use stop-poc.ps1 first.' }
}
dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
New-Item -ItemType Directory -Force $runtime | Out-Null
if (Test-Path $stopFile) { Remove-Item -LiteralPath $stopFile }
$exe = Join-Path $PSScriptRoot 'src\GoBoard.Poc\bin\Release\net10.0-windows\GoBoard.Poc.exe'
$process = Start-Process -FilePath $exe -ArgumentList @('--stop-file', ('"' + $stopFile + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'poc.log') -RedirectStandardError (Join-Path $runtime 'poc.error.log')
$process.Id | Set-Content $pidFile
Start-Sleep -Seconds 2
$process.Refresh()
Get-Content (Join-Path $runtime 'poc.log')
if ($process.HasExited) {
    Get-Content (Join-Path $runtime 'poc.error.log')
    throw 'POC exited. See .runtime logs.'
}
Write-Output "GoBoard is running (PID $($process.Id)). Run .\stop-poc.ps1 to close it."
