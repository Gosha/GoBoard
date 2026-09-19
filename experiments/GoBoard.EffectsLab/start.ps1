param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'GoBoard.EffectsLab.csproj'
if (-not $NoBuild) {
    dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Effects Lab build failed.' }
}
$executable = Join-Path $PSScriptRoot 'bin/Release/net10.0-windows/GoBoard.EffectsLab.exe'
Start-Process -FilePath $executable -WorkingDirectory $PSScriptRoot -WindowStyle Normal
