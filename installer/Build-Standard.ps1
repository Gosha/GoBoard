param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$BuildRoot,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$ProductVersion,
    [Parameter(Mandatory)][string]$ProductName
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$helper = Join-Path $BuildRoot 'prerequisite'
$probe = Join-Path $helper 'probe'
$probeProject = Join-Path $PSScriptRoot 'RuntimeProbe\RuntimeProbe.csproj'
$helperProject = Join-Path $PSScriptRoot 'Prerequisite\Prerequisite.csproj'
dotnet restore $probeProject --runtime win-x64 --locked-mode --artifacts-path "$BuildRoot\probe-build" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Runtime probe restore failed.' }
dotnet publish $probeProject -c Release --runtime win-x64 --self-contained false --no-restore --artifacts-path "$BuildRoot\probe-build" --output $probe '-p:DebugType=None' | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Runtime probe publish failed.' }
Copy-Item (Join-Path $PublishDir 'GoBoard.runtimeconfig.json') (Join-Path $probe 'RuntimeProbe.runtimeconfig.json')
dotnet restore $helperProject --locked-mode --artifacts-path "$BuildRoot\helper-build" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Prerequisite helper restore failed.' }
dotnet publish $helperProject -c Release --no-restore --artifacts-path "$BuildRoot\helper-build" --output $helper '-p:DebugType=None' | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Prerequisite helper publish failed.' }
& (Join-Path $PSScriptRoot 'Test-Prerequisite.ps1') -HelperDir $helper | Out-Host
$bundle = Join-Path $PSScriptRoot 'Standard\GoBoard.Standard.wixproj'
$properties = @("-p:BaseIntermediateOutputPath=$BuildRoot\bundle\obj\", "-p:OutputPath=$BuildRoot\bundle\bin\", "-p:ReleaseVersion=$Version", "-p:ProductVersion=$ProductVersion", "-p:StandardMsi=$MsiPath", "-p:PrerequisiteDir=$helper\")
$properties += "-p:ProductName=$ProductName"
dotnet restore $bundle --locked-mode @properties | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Standard setup restore failed.' }
dotnet build $bundle -c Release --no-restore @properties | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Standard setup build failed.' }
$output = Join-Path $BuildRoot "bundle\bin\GoBoard-$Version-win-x64-standard.exe"
if (!(Test-Path $output)) { throw 'Standard setup output is missing.' }
& (Join-Path $PSScriptRoot 'Test-Standard.ps1') -SetupPath $output -MsiPath $MsiPath | Out-Host
Write-Output $output
