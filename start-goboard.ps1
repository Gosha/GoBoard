param([switch]$Desktop, [switch]$BuildOnly)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\GoBoard.App\GoBoard.App.csproj'
$runtime = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GoBoard\runtime'

$existing = $null
if (!$BuildOnly -and [Threading.Mutex]::TryOpenExisting('Local\GoBoard.Runtime', [ref]$existing)) {
    $existing.Dispose()
    throw 'GoBoard is already running. Use .\stop-goboard.ps1 first.'
}

# Keep both modes separate from normal builds and standalone Settings. Windows
# prevents overwriting DLLs loaded by an older window, even after typing stops.
$buildName = if ($Desktop) { 'desktop-build' } else { 'vr-build' }
$buildRoot = Join-Path $PSScriptRoot "artifacts\$buildName"
$relativeExe = 'bin\GoBoard.App\release\GoBoard.exe'
$exe = Join-Path $buildRoot $relativeExe
$loaded = Get-Process -Name GoBoard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($loaded) {
    # A settings window may still have this output loaded.
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

$launch = @{ FilePath = $exe; WindowStyle = 'Hidden'; PassThru = $true }
if ($Desktop) { $launch.ArgumentList = @('--desktop') }
$process = Start-Process @launch
Start-Sleep -Seconds 2
$process.Refresh()
Get-Content -LiteralPath (Join-Path $runtime 'goboard.log') -ErrorAction SilentlyContinue
if ($process.HasExited) {
    if ($process.ExitCode -eq 0) {
        Write-Output 'GoBoard launch completed; another copy may already be running.'
        return
    }
    Get-Content -LiteralPath (Join-Path $runtime 'goboard.error.log') -ErrorAction SilentlyContinue
    throw "GoBoard exited. See $runtime logs."
}
Write-Output "GoBoard is running (PID $($process.Id)). Run .\stop-goboard.ps1 to close it."
