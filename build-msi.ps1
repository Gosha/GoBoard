param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$release = & (Join-Path $PSScriptRoot 'installer\Get-ReleaseInfo.ps1') @PSBoundParameters
$Version = $release.Version
$Channel = $release.Channel

# Keep every run isolated from live launchers and stale publish files.
$buildRoot = Join-Path $PSScriptRoot ('artifacts\msi\build-' + [Guid]::NewGuid().ToString('N'))
$publishDir = Join-Path $buildRoot 'publish'
$dotnetArtifacts = Join-Path $buildRoot 'dotnet'
$installerArtifacts = Join-Path $buildRoot 'installer'
$outputDir = Join-Path $PSScriptRoot 'artifacts\msi'
$app = Join-Path $PSScriptRoot 'src\GoBoard.App\GoBoard.App.csproj'
$installer = Join-Path $PSScriptRoot 'installer\GoBoard.Installer.wixproj'
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Push-Location $PSScriptRoot
try {
    $revision = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the source commit.' }
    $changes = git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine whether the source is clean.' }
    $dirty = [bool]$changes
    $informationalVersion = $Version
    $informationalVersion += "+$revision"
    if ($dirty) { $informationalVersion += '.dirty' }

    dotnet restore $app --runtime win-x64 --artifacts-path $dotnetArtifacts --locked-mode '-p:NuGetLockFilePath=packages.win-x64.lock.json' '-p:SelfContained=true'
    if ($LASTEXITCODE -ne 0) { throw 'Application restore failed.' }

    dotnet publish $app -c Release --runtime win-x64 --self-contained true --no-restore --artifacts-path $dotnetArtifacts --output $publishDir "-p:Version=$Version" "-p:AssemblyVersion=$($release.MsiVersion).0" "-p:FileVersion=$($release.MsiVersion).0" "-p:InformationalVersion=$informationalVersion" '-p:IncludeSourceRevisionInInformationalVersion=false' '-p:NuGetLockFilePath=packages.win-x64.lock.json' '-p:EnableDesktopDebug=false' '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' '-p:DebugType=None' '-p:DebugSymbols=false'
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }

    # Ship provenance with the app as well as alongside the downloadable MSI.
    $metadata = [ordered]@{
        channel = $Channel
        version = $Version
        msiVersion = $release.MsiVersion
        informationalVersion = $informationalVersion
        sourceRevision = $revision
        sourceDirty = $dirty
        releaseTag = $release.ReleaseTag
    }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publishDir 'release.json') -Encoding utf8

    $payloadFragment = Join-Path $buildRoot 'Payload.wxs'
    & (Join-Path $PSScriptRoot 'installer\New-PayloadFragment.ps1') -PublishDir $publishDir -OutputPath $payloadFragment
    $actionsProject = Join-Path $PSScriptRoot 'installer\GoBoard.Installer.Actions\GoBoard.Installer.Actions.csproj'
    $actionsProperties = @("-p:BaseIntermediateOutputPath=$buildRoot\actions\obj\", "-p:OutputPath=$buildRoot\actions\bin\")
    dotnet restore $actionsProject --locked-mode @actionsProperties
    if ($LASTEXITCODE -ne 0) { throw 'Installer lifecycle action restore failed.' }
    dotnet build $actionsProject -c Release --no-restore @actionsProperties
    if ($LASTEXITCODE -ne 0) { throw 'Installer lifecycle action build failed.' }
    $lifecycleActions = Join-Path $buildRoot 'actions\bin\GoBoard.Installer.Actions.CA.dll'
    $installerProperties = @("-p:BaseIntermediateOutputPath=$installerArtifacts\obj\", "-p:OutputPath=$installerArtifacts\bin\", "-p:PublishDir=$publishDir\", "-p:PayloadFragment=$payloadFragment", "-p:ProductVersion=$($release.MsiVersion)", "-p:ReleaseVersion=$Version", "-p:ReleaseChannel=$Channel", "-p:LifecycleActions=$lifecycleActions")
    dotnet restore $installer --locked-mode @installerProperties
    if ($LASTEXITCODE -ne 0) { throw 'Installer restore failed.' }
    dotnet build $installer -c Release --no-restore @installerProperties
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

    $msiName = $release.ArtifactBase + '.msi'
    $builtMsi = Join-Path $installerArtifacts "bin\$msiName"
    if (!(Test-Path -LiteralPath $builtMsi)) { throw "Installer output missing: $builtMsi" }
    & (Join-Path $PSScriptRoot 'installer\Test-Msi.ps1') -MsiPath $builtMsi -PublishDir $publishDir -Channel $Channel
    $msi = Join-Path $outputDir $msiName
    Copy-Item -LiteralPath $builtMsi -Destination $msi -Force
    Copy-Item -LiteralPath (Join-Path $publishDir 'release.json') -Destination (Join-Path $outputDir ($release.ArtifactBase + '.release.json')) -Force
    $hash = (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $msiName" | Set-Content -LiteralPath "$msi.sha256" -Encoding ascii
    Write-Output "Built $Channel MSI: $msi"
    Write-Output "SHA256: $hash"
}
finally {
    Pop-Location
}
