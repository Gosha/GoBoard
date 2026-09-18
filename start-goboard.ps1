param([switch]$Desktop)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\GoBoard.App\GoBoard.App.csproj'
$runtime = Join-Path $PSScriptRoot '.runtime\app'
$stopFile = Join-Path $runtime 'stop'
$pidFile = Join-Path $runtime 'goboard.pid'

if (Get-Process -Name 'GoBoard.Poc' -ErrorAction SilentlyContinue) {
    throw 'The POC is running. Close it with .\stop-poc.ps1 before starting GoBoard.'
}

if (Test-Path -LiteralPath $pidFile) {
    $existingId = [int](Get-Content -LiteralPath $pidFile)
    $existing = Get-Process -Id $existingId -ErrorAction SilentlyContinue
    if ($existing -and $existing.ProcessName -eq 'GoBoard') {
        throw 'GoBoard is already running. Use .\stop-goboard.ps1 first.'
    }
}

$exe = Join-Path $PSScriptRoot 'src\GoBoard.App\bin\Release\net10.0-windows\GoBoard.exe'
if ($Desktop) {
    $desktopBuild = Join-Path $PSScriptRoot 'artifacts\desktop-build'
    dotnet build $project -c Release --artifacts-path $desktopBuild --nologo
    $exe = Join-Path $desktopBuild 'bin\GoBoard.App\release\GoBoard.exe'
} else {
    dotnet build $project -c Release --nologo
}
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

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
