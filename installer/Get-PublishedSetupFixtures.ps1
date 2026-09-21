# Immutable baselines: exercise published upgrade rules, not a reconstruction
# using today's authoring. Test-SetupSwitching clones and isolates these MSIs.
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\installer-baselines'
New-Item -ItemType Directory -Force $root | Out-Null
$paths = @{}
foreach ($fixture in @(
    @{ Channel = 'Stable'; Tag = 'v1.4.1'; Name = 'GoBoard-1.4.1-win-x64.msi'; Hash = '5629c5e7d5eda6b10c12edd1629ca30b37dfdfefb4934a00d3c1578e71246eb7' },
    @{ Channel = 'Beta'; Tag = 'v2.0.0-beta.1'; Name = 'GoBoard-2.0.0-beta.1-win-x64-offline.msi'; Hash = '8d61fb7c9642114c175da5b01bf9302bf7801f6777c516bf602f79bac78024c4' }
)) {
    $path = Join-Path $root $fixture.Name
    if (!(Test-Path -LiteralPath $path)) {
        Invoke-WebRequest "https://github.com/Gosha/GoBoard/releases/download/$($fixture.Tag)/$($fixture.Name)" -OutFile $path
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $fixture.Hash) { throw "Published fixture checksum mismatch: $path" }
    $paths[$fixture.Channel] = $path
}
[pscustomobject]$paths
