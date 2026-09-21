param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$PublishDir,
    [ValidateSet('stable', 'beta', 'development')][string]$Channel = 'stable',
    [ValidateSet('standard', 'offline')][string]$SetupVariant = 'offline'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
$publish = (Resolve-Path -LiteralPath $PublishDir).Path
$checkRoot = Join-Path (Split-Path $msi) ('check-' + [Guid]::NewGuid().ToString('N'))
$extracted = Join-Path $checkRoot 'extracted'
$payload = Join-Path $checkRoot 'payload'
$xmlPath = Join-Path $checkRoot 'package.wxs'
New-Item -ItemType Directory -Force -Path $payload | Out-Null

# Use the restored SDK, honoring NuGet's configured package cache.
$wixBin = & dotnet msbuild (Join-Path $PSScriptRoot 'GoBoard.Installer.wixproj') -getProperty:WixBinDir
if ($LASTEXITCODE -ne 0) { throw 'Could not locate the WiX SDK.' }
& dotnet (Join-Path $wixBin 'wix.dll') msi decompile $msi -x $extracted -o $xmlPath
if ($LASTEXITCODE -ne 0) { throw 'MSI extraction failed.' }

[xml]$document = Get-Content -Raw -LiteralPath $xmlPath
$ns = New-Object System.Xml.XmlNamespaceManager($document.NameTable)
$ns.AddNamespace('w', 'http://wixtoolset.org/schemas/v4/wxs')
$files = $document.SelectNodes('//w:File', $ns)
foreach ($file in $files) {
    $relative = $file.GetAttribute('Name')
    if (!$relative) { throw 'Extracted file has no target name.' }
    $directory = $file.ParentNode.ParentNode
    while ($directory.GetAttribute('Id') -ne 'INSTALLFOLDER') {
        if ($directory.LocalName -ne 'Directory') { throw "Unexpected install location for $relative" }
        $relative = Join-Path $directory.GetAttribute('Name') $relative
        $directory = $directory.ParentNode
    }
    $source = Join-Path $extracted ('File\' + $file.GetAttribute('Id'))
    $original = Join-Path $publish $relative
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $original).Hash) {
        throw "Packaged bytes differ from publish output: $relative"
    }
    $target = Join-Path $payload $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
$publishedFiles = @(Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object Extension -ne '.pdb')
if ($files.Count -ne $publishedFiles.Count) { throw 'MSI file count differs from the publish output.' }
foreach ($required in @('GoBoard.exe', 'GoBoard.dll', 'GoBoard.runtimeconfig.json',
                        'openvr_api.dll', 'libSkiaSharp.dll', 'glfw3.dll',
                        'OpenVR-LICENSE.txt', 'SOUND-CREDITS.md')) {
    if (!(Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Missing payload: $required" }
}
if (Get-ChildItem -LiteralPath $payload -Recurse -Filter '*Poc*') { throw 'POC files must not ship in the production MSI.' }
# Inspect the actual packaged apphost: a console subsystem opens a window before
# managed startup runs, even when the application only displays graphical UI.
$appStream = [IO.File]::OpenRead((Join-Path $payload 'GoBoard.exe'))
try {
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($appStream)
    try {
        if ($pe.PEHeaders.PEHeader.Subsystem -ne [System.Reflection.PortableExecutable.Subsystem]::WindowsGui) {
            throw 'GoBoard.exe must use the Windows GUI subsystem to launch without a console.'
        }
    } finally { $pe.Dispose() }
} finally { $appStream.Dispose() }
$config = Get-Content -Raw -LiteralPath (Join-Path $payload 'GoBoard.runtimeconfig.json') | ConvertFrom-Json
if ($SetupVariant -eq 'offline') {
    if (@($config.runtimeOptions.includedFrameworks).Count -ne 2) { throw 'Expected a self-contained desktop runtime.' }
    foreach ($file in @('hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'System.Windows.Forms.dll')) {
        if (!(Test-Path (Join-Path $payload $file))) { throw "Offline runtime file missing: $file" }
    }
} else {
    if (@($config.runtimeOptions.frameworks).Count -ne 2 -or $config.runtimeOptions.rollForward -ne 'Minor') { throw 'Expected framework-dependent Desktop/Core runtime with Minor roll-forward.' }
    foreach ($framework in $config.runtimeOptions.frameworks) {
        if ($framework.version -ne '10.0.0' -or $framework.name -notin @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) { throw 'Update setup detection for the new app runtime requirements.' }
    }
    if (Test-Path (Join-Path $payload 'hostfxr.dll')) { throw 'Standard setup must not bundle .NET.' }
}

# The shortcut/Installed apps icon must be the compact branding ICO, not a
# second copy of the executable stored outside the compressed payload.
$icon = $document.SelectSingleNode("//w:Icon[@Id='GoBoard.exe']", $ns)
$iconSource = Join-Path $extracted 'Icon\GoBoard.exe'
$brandingIcon = Join-Path $PSScriptRoot '..\assets\branding\goboard.ico'
if (!$icon -or !(Test-Path -LiteralPath $iconSource) -or
    (Get-FileHash -LiteralPath $iconSource).Hash -ne (Get-FileHash -LiteralPath $brandingIcon).Hash) {
    throw 'MSI must embed the branding ICO for its shortcut and Installed apps icon.'
}
$shortcuts = $document.SelectNodes('//w:Shortcut', $ns)
if ($shortcuts.Count -ne 2) { throw 'Expected only VR and Settings Start menu shortcuts.' }
foreach ($entry in @(@('GoBoard VR', ''), @('GoBoard Settings', '--settings'))) {
    $shortcut = @($shortcuts | Where-Object { $_.GetAttribute('Name') -eq $entry[0] })
    if ($shortcut.Count -ne 1 -or $shortcut[0].GetAttribute('Arguments') -ne $entry[1] -or
        $shortcut[0].GetAttribute('Advertise') -eq 'yes' -or
        $shortcut[0].GetAttribute('Target') -ne '[INSTALLFOLDER]GoBoard.exe' -or
        $shortcut[0].GetAttribute('Icon') -ne 'GoBoard.exe' -or
        !$shortcut[0].ParentNode.SelectSingleNode("w:RegistryValue[@Root='HKCU' and @KeyPath='yes']", $ns)) {
        throw "Incorrect shortcut target/arguments: $($entry[0])"
    }
}
$package = $document.SelectSingleNode('/w:Wix/w:Package', $ns)
$appVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'GoBoard.exe')).FileVersion
if ($appVersion -ne ($package.GetAttribute('Version') + '.0')) { throw 'App and MSI versions differ.' }

$metadata = Get-Content -Raw -LiteralPath (Join-Path $payload 'release.json') | ConvertFrom-Json
if ($metadata.channel -ne $Channel -or $metadata.msiVersion -ne $package.GetAttribute('Version')) {
    throw 'Packaged release metadata does not match the requested channel/version.'
}
if ($metadata.setupVariant -ne $SetupVariant) { throw 'Incorrect payload variant provenance.' }
$info = & (Join-Path $PSScriptRoot 'Get-ReleaseInfo.ps1') -Version $metadata.version
if ($info.MsiVersion -ne $metadata.msiVersion -or $info.Channel -ne $Channel) { throw 'Release version mapping or channel does not match MSI metadata.' }
if ($Channel -eq 'development' -and ($metadata.releaseTag -or
    $metadata.pullRequest -ne $info.PullRequest -or $metadata.runNumber -ne $info.RunNumber -or
    $metadata.runAttempt -ne $info.RunAttempt -or !$metadata.sourceRevision.StartsWith($info.Commit))) {
    throw 'Development provenance differs from its visible build identity.'
}
if ([guid]$package.GetAttribute('UpgradeCode') -ne [guid]$info.UpgradeCode -or
    $package.GetAttribute('Name') -ne $info.ProductName) { throw 'Incorrect MSI channel identity.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'GoBoard.dll')).ProductVersion -ne $metadata.informationalVersion) {
    throw 'App informational version does not match release provenance.'
}

# Inspect actual MSI tables: decompilation can misreport action scheduling.
$engine = New-Object -ComObject WindowsInstaller.Installer
$db = $engine.OpenDatabase($msi, 0)
try {
    # Test the compiled privilege bit and properties, not just source authoring.
    $summary = $db.SummaryInformation(0)
    try {
        if (($summary.Property(15) -band 8) -eq 0) { throw 'MSI requests elevated privileges.' }
    } finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) | Out-Null }
    $view = $db.OpenView('SELECT `Property`, `Value` FROM `Property`')
    $view.Execute()
    $properties = @{}
    while ($record = $view.Fetch()) { $properties[$record.StringData(1)] = $record.StringData(2) }
    $view.Close()
    if ($properties['ALLUSERS'] -or $properties['MSIINSTALLPERUSER']) { throw 'MSI must be strictly per-user.' }
    if (!$document.SelectSingleNode("//w:Launch[@Condition='NOT ALLUSERS']", $ns)) { throw 'MSI must reject an all-users override.' }
    if (!$document.SelectSingleNode("//w:StandardDirectory[@Id='LocalAppDataFolder']/w:Directory[@Name='Programs']/w:Directory[@Id='INSTALLFOLDER' and @Name='GoBoard']", $ns)) {
        throw 'Default install folder must be LocalAppData\\Programs\\GoBoard.'
    }
    if (!$document.SelectSingleNode("//w:Property[@Id='INSTALLFOLDER']/w:RegistrySearch[@Root='HKCU' and @Key='Software\GoBoard' and @Name='InstallFolder']", $ns)) {
        throw 'Previous install location must come from HKCU.'
    }
    if (!$document.SelectSingleNode("//w:Property[@Id='GOBOARD_MACHINE_INSTALL']/w:RegistrySearch[@Root='HKLM' and @Key='Software\GoBoard' and @Name='InstallFolder']", $ns) -or
        !$document.SelectSingleNode("//w:Launch[@Condition='Installed OR NOT GOBOARD_MACHINE_INSTALL']", $ns)) {
        throw 'Missing migration guard for an old all-users installation.'
    }
    foreach ($registry in $document.SelectNodes('//w:RegistryValue', $ns)) {
        if ($registry.GetAttribute('Root') -ne 'HKCU') { throw 'MSI must only write current-user registry values.' }
    }
    foreach ($component in $document.SelectNodes('//w:Component', $ns)) {
        if (!$component.SelectSingleNode("w:RegistryValue[@Root='HKCU' and @KeyPath='yes']", $ns)) {
            throw "Per-user component has no HKCU key path: $($component.GetAttribute('Id'))"
        }
    }
    $view = $db.OpenView('SELECT `UpgradeCode`, `VersionMin`, `VersionMax`, `Attributes`, `Remove`, `ActionProperty` FROM `Upgrade`')
    $view.Execute()
    $crossChannelFound = $false
    $equalVersionRemoval = $false
    $betaRemoval = $false
    $developmentDowngradeGuard = $false
    while ($record = $view.Fetch()) {
        if ($record.StringData(6) -eq 'WIX_UPGRADE_DETECTED') {
            $equalVersionRemoval = $record.StringData(3) -eq $metadata.msiVersion -and ($record.IntegerData(4) -band 512) -ne 0 -and ($record.IntegerData(4) -band 2) -eq 0
        }
        if ($record.StringData(6) -eq 'GOBOARD_BETA_CHANNEL_FOUND') {
            $betaRemoval = [guid]$record.StringData(1) -eq [guid]$info.UpgradeCode -and
                $record.StringData(2) -eq '1.0.0' -and $record.StringData(3) -eq '' -and
                ($record.IntegerData(4) -band 2) -eq 0 -and ($record.IntegerData(4) -band 256) -ne 0 -and $record.StringData(5) -eq ''
        }
        if ($record.StringData(6) -eq 'WIX_DOWNGRADE_DETECTED' -and $Channel -eq 'development') {
            $developmentDowngradeGuard = [guid]$record.StringData(1) -eq [guid]$info.UpgradeCode -and
                $record.StringData(2) -eq $metadata.msiVersion -and $record.StringData(3) -eq '1.0.0' -and
                ($record.IntegerData(4) -band 2) -ne 0 -and ($record.IntegerData(4) -band 768) -eq 0
        }
        if ($record.StringData(6) -ne 'GOBOARD_OTHER_CHANNEL_FOUND') { continue }
        # OnlyDetect (2) must be absent, VersionMinInclusive (256) present;
        # an empty Remove column removes all features.
        if ([guid]$record.StringData(1) -ne [guid]$info.OtherUpgradeCode -or
            $record.StringData(2) -ne '0.0.0' -or $record.StringData(3) -ne '' -or
            ($record.IntegerData(4) -band 2) -ne 0 -or ($record.IntegerData(4) -band 256) -eq 0 -or
            $record.StringData(5) -ne '') { throw 'Cross-channel replacement is not unconditional.' }
        $crossChannelFound = $true
    }
    $view.Close()
    if (!$crossChannelFound) { throw 'MSI does not replace the other release channel.' }
    if (!$equalVersionRemoval) { throw 'MSI must permit transactional equal-version replacement for variant switches.' }
    if ($Channel -eq 'development' -and (!$betaRemoval -or !$developmentDowngradeGuard)) {
        throw 'Development must replace released Betas while blocking Development downgrades.'
    }
    $view = $db.OpenView('SELECT `Condition` FROM `LaunchCondition`')
    $view.Execute()
    $conditions = @()
    while ($record = $view.Fetch()) { $conditions += $record.StringData(1) }
    $view.Close()
    if ($conditions -notcontains ('Installed OR NOT GOBOARD_SAME_VERSION_FOUND OR GOBOARD_PREVIOUS_VARIANT <> "' + $SetupVariant + '"')) { throw 'Missing same-variant rebuild guard.' }
    if ($Channel -eq 'development' -and $conditions -notcontains 'Installed OR NOT WIX_DOWNGRADE_DETECTED') { throw 'Missing Development downgrade launch condition.' }
    if ($SetupVariant -eq 'standard' -and $conditions -notcontains 'REMOVE = "ALL" OR GOBOARD_RUNTIME_RESULT = "0"') { throw 'Missing runtime prerequisite launch condition.' }
    if ($properties['GOBOARD_PREVIOUS_VARIANT'] -ne 'offline') { throw 'Legacy MSI packages must be recognized as Offline.' }
    $view = $db.OpenView('SELECT `Action`, `Sequence` FROM `InstallExecuteSequence`')
    $view.Execute()
    $sequence = @{}
    while ($record = $view.Fetch()) { $sequence[$record.StringData(1)] = $record.IntegerData(2) }
    $view.Close()
    if ($sequence.RemoveExistingProducts -le $sequence.InstallInitialize -or
        $sequence.RemoveExistingProducts -ge $sequence.InstallFiles) {
        throw 'Replacement must remove old files inside the rollback transaction before installing new files.'
    }
    if ($properties['MSIRESTARTMANAGERCONTROL'] -ne 'Disable' -or
        $sequence.GoBoardStop -le $sequence.CostFinalize -or
        $sequence.GoBoardStop -ge $sequence.InstallValidate -or
        $sequence.GoBoardRestart -le $sequence.InstallFinalize -or
        $sequence.GoBoardRecoverError -ne -3 -or $sequence.GoBoardRecoverCancel -ne -2) {
        throw 'Installer must stop before files-in-use validation, restart after commit, and recover on failure/cancellation.'
    }
    $view = $db.OpenView('SELECT `Action`, `Condition` FROM `InstallExecuteSequence`')
    $view.Execute()
    while ($record = $view.Fetch()) {
        if ($record.StringData(1) -like 'GoBoard*' -and $record.StringData(2) -ne 'NOT UPGRADINGPRODUCTCODE') {
            throw 'Nested upgrade removal must not run the outer installer lifecycle actions.'
        }
    }
    $view.Close()
    $view = $db.OpenView('SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`')
    $view.Execute()
    $lifecycleCount = 0
    while ($record = $view.Fetch()) {
        if ($record.StringData(1) -notin @('GoBoardStop', 'GoBoardRestart', 'GoBoardRecoverError', 'GoBoardRecoverCancel')) { continue }
        $lifecycleCount++
        if ($record.StringData(3) -ne 'GoBoardLifecycle' -or ($record.IntegerData(2) -band 63) -ne 1 -or
            ($record.IntegerData(2) -band 3072) -ne 0) { throw 'Lifecycle actions must use the embedded DLL and current-user immediate execution.' }
    }
    $view.Close()
    if ($lifecycleCount -ne 4) { throw 'Missing embedded lifecycle actions.' }
} finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) | Out-Null
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine) | Out-Null
}

# Run only rendering commands: no SteamVR initialization, typing, or saved-settings edits.
foreach ($mode in @('render', 'render-settings', 'render-desktop-settings')) {
    $png = Join-Path $checkRoot "$mode.png"
    $stdout = Join-Path $checkRoot "$mode.stdout.log"
    $stderr = Join-Path $checkRoot "$mode.stderr.log"
    $process = Start-Process -FilePath (Join-Path $payload 'GoBoard.exe') -ArgumentList @("--$mode", ('"' + $png + '"')) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $png) -or (Get-Item -LiteralPath $png).Length -lt 1000) {
        throw "Extracted MSI payload failed --$mode."
    }
    if ($mode -ne 'render-desktop-settings' -and (Get-Content -Raw -LiteralPath $stdout) -notmatch 'Rendered') {
        throw "Extracted MSI payload lost redirected stdout for --$mode."
    }
}
# Inspect managed types without loading the app or its dependencies into this process.
$assemblyStream = [IO.File]::OpenRead((Join-Path $payload 'GoBoard.dll'))
try {
    $reader = [System.Reflection.PortableExecutable.PEReader]::new($assemblyStream)
    try {
        $metadataReader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
        foreach ($handle in $metadataReader.TypeDefinitions) {
            $type = $metadataReader.GetTypeDefinition($handle)
            $name = $metadataReader.GetString($type.Name)
            if ($name -like 'Desktop*' -or $name -eq 'SettingsInputCheck') {
                throw "Desktop debugging implementation must not ship: $name"
            }
        }
    } finally { $reader.Dispose() }
} finally { $assemblyStream.Dispose() }

# Removed entry points must fail before opening a keyboard or touching SteamVR.
foreach ($command in @('--desktop', '--render-desktop', '--desktop-effects-benchmark',
                       '--desktop-input-check', '--desktop-shell-check', '--desktop-launch-check', '--settings-input-check')) {
    $stderr = Join-Path $checkRoot "$command.stderr.log"
    $process = Start-Process -FilePath (Join-Path $payload 'GoBoard.exe') -ArgumentList $command -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $checkRoot "$command.stdout.log") -RedirectStandardError $stderr
    if ($process.ExitCode -ne 1 -or (Get-Content -Raw -LiteralPath $stderr) -notmatch 'available only in source builds') {
        throw "Packaged app did not reject desktop debugging command: $command"
    }
}

# Argument validation exits before showing UI or initializing SteamVR.
$stderr = Join-Path $checkRoot 'invalid-option.stderr.log'
$process = Start-Process -FilePath (Join-Path $payload 'GoBoard.exe') -ArgumentList @('--invalid-option') -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $checkRoot 'invalid-option.stdout.log') -RedirectStandardError $stderr
if ($process.ExitCode -ne 1 -or (Get-Content -Raw -LiteralPath $stderr) -notmatch 'Usage:') {
    throw 'Extracted MSI payload lost its failure exit code or redirected stderr.'
}
Write-Output "MSI verified: $($files.Count) files match publish output; per-user scope/privileges, registry, migration guard, GUI subsystem, redirected diagnostics, runtime, native libraries, credits, shortcuts, versions, VR-only entry points, and keyboard/Settings renders passed."
Write-Output "Inspection artifacts: $checkRoot"
