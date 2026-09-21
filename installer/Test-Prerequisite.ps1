param([Parameter(Mandatory)][string]$HelperDir)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$helper = Join-Path $HelperDir 'Prerequisite.exe'
function Invoke-Probe([bool]$Missing, [string]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($helper, $Arguments)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    if ($Missing) {
        # Apphost honors this process-local override. Never change shared .NET.
        $start.Environment['DOTNET_ROOT_X64'] = Join-Path $HelperDir 'empty-dotnet'
        $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    }
    $process = [Diagnostics.Process]::Start($start)
    if (!$process.WaitForExit(30000)) { throw 'Runtime check timed out.' }
    return $process.ExitCode
}
if ((Invoke-Probe $false '/check') -ne 0) { throw 'Installed runtime compatibility check failed.' }
New-Item -ItemType Directory -Force (Join-Path $HelperDir 'empty-dotnet') | Out-Null
if ((Invoke-Probe $true '/check') -ne 1) { throw 'Missing runtime was not detected.' }
if ((Invoke-Probe $true '/ui:2 /accept:0') -ne 1603) { throw 'Unattended missing runtime must fail without explicit download consent.' }
Write-Output 'Prerequisite: actual app runtime resolves; isolated missing-runtime probe and unattended consent guard passed. No runtime was installed.'
