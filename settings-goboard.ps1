$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\GoBoard.App\GoBoard.App.csproj'
$settingsBuild = Join-Path $PSScriptRoot 'artifacts\settings-build'
$exe = Join-Path $settingsBuild 'bin\GoBoard.App\release\GoBoard.exe'
$productionExe = Join-Path $PSScriptRoot 'src\GoBoard.App\bin\Release\net10.0-windows\GoBoard.exe'

# The autostart button must register a production build, not this settings helper.
if (!(Test-Path -LiteralPath $productionExe)) {
    dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

# A separate output allows updated settings alongside a running keyboard.
# Reuse this output only when a settings window already has it loaded.
$settingsRunning = Get-Process -Name GoBoard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if (!(Test-Path -LiteralPath $exe) -or !$settingsRunning) {
    dotnet build $project -c Release --artifacts-path $settingsBuild --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
Start-Process -FilePath $exe -ArgumentList @('--settings', '--executable', ('"' + $productionExe + '"')) -WindowStyle Hidden
