$ErrorActionPreference = 'Stop'
$runtime = Join-Path $PSScriptRoot '.runtime\app'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
Set-Content -LiteralPath (Join-Path $runtime 'stop') -Value 'stop'
Write-Output 'Requested graceful GoBoard shutdown.'
