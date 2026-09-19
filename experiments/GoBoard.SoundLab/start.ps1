param([int]$Port = 8767)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$runtimeDir = Join-Path $repoRoot '.runtime/sound-lab'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null
$pythonCommand = Get-Command python -ErrorAction SilentlyContinue
$pythonExe = if ($pythonCommand) { $pythonCommand.Source } else {
    Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
}
if (!(Test-Path -LiteralPath $pythonExe)) { throw 'Python 3 is required to serve the sound lab.' }
$labArguments = @('-m', 'http.server', $Port, '--bind', '127.0.0.1', '--directory', ('"' + $PSScriptRoot + '"'))
$labProcess = Start-Process -FilePath $pythonExe -ArgumentList $labArguments -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $runtimeDir 'server.log') -RedirectStandardError (Join-Path $runtimeDir 'server.error.log')
$labProcess.Id | Set-Content -LiteralPath (Join-Path $runtimeDir 'server.pid')
Write-Output "Sound lab: http://127.0.0.1:$Port/ (server PID $($labProcess.Id))"
