param(
    [ValidateSet('Register', 'Enable', 'Disable', 'Unregister', 'Status')]
    [string]$Action = 'Status',
    [string]$Executable
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\GoBoard.App\GoBoard.App.csproj'
$productionExe = Join-Path $PSScriptRoot 'src\GoBoard.App\bin\Release\net10.0-windows\GoBoard.exe'

if ($Executable -and $Action -notin @('Register', 'Enable')) {
    throw '-Executable is only valid with Register or Enable.'
}
if ($Action -in @('Register', 'Enable')) {
    if (!$Executable) {
        # Avoid rebuilding a binary already loaded by a running keyboard.
        if (!(Test-Path -LiteralPath $productionExe)) {
            dotnet build $project -c Release --nologo
            if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
        }
        $Executable = $productionExe
    }
    $Executable = (Resolve-Path -LiteralPath $Executable).Path
}

# A separate helper build can configure SteamVR while the registered app runs.
# Its path is never registered as the production executable.
$helperBuild = Join-Path $PSScriptRoot 'artifacts\steamvr-settings-build'
dotnet build $project -c Release --artifacts-path $helperBuild --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$helperExe = Join-Path $helperBuild 'bin\GoBoard.App\release\GoBoard.exe'
$commandArguments = @('--steamvr', $Action.ToLowerInvariant())
if ($Executable) { $commandArguments += @('--executable', $Executable) }
& $helperExe @commandArguments
if ($LASTEXITCODE -ne 0) { throw 'SteamVR configuration failed. See the error above.' }
