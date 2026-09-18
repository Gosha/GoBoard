$ErrorActionPreference = 'Stop'
$runtime = Join-Path $PSScriptRoot '..\..\.runtime\stereo-dashboard'
New-Item -ItemType Directory -Force $runtime | Out-Null
Set-Content -LiteralPath (Join-Path $runtime 'stop') -Value 'stop'
Write-Output 'Requested graceful stereo experiment shutdown.'
