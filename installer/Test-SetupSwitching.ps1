param([Parameter(Mandatory)][string]$OfflineMsi, [Parameter(Mandatory)][string]$StandardMsi,
      [string]$StandardSetup, [string]$PublishedStableMsi, [string]$PublishedBetaMsi)
# Real Windows Installer transactions, but with disposable identities, keys,
# components and shortcuts. Disable lifecycle actions so the live app is untouched.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$id = [Guid]::NewGuid().ToString('N')
$root = Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\switch-check-$id"
$install = Join-Path $root 'installed'
New-Item -ItemType Directory -Path $root | Out-Null
$engine = New-Object -ComObject WindowsInstaller.Installer
$families = @{}
$components = @{}
$products = [Collections.Generic.List[string]]::new()
function New-Code { '{' + [Guid]::NewGuid().ToString().ToUpperInvariant() + '}' }
function Query($db, [string]$sql) {
    $view = $db.OpenView($sql)
    try { [void]$view.Execute() } finally { [void]$view.Close(); [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null }
}
function Rows($db, [string]$table, [string]$columns) {
    $view = $db.OpenView("SELECT $columns FROM ``$table``")
    try {
        [void]$view.Execute()
        while ($record = $view.Fetch()) {
            $row = @()
            for ($i = 1; $i -le $columns.Split(',').Count; $i++) { $row += $record.StringData($i) }
            ,$row
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) | Out-Null
        }
    } finally { [void]$view.Close(); [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null }
}
function Clone([string]$Source, [string]$Name, [string]$Version = '', [bool]$OtherChannel = $false) {
    $destination = Join-Path $root "$Name.msi"
    Copy-Item -LiteralPath $Source -Destination $destination
    $db = $engine.OpenDatabase($destination, 1)
    try {
        $properties = @{}
        foreach ($row in (Rows $db 'Property' '`Property`, `Value`')) { $properties[$row[0]] = $row[1] }
        $oldVersion = $properties.ProductVersion
        if (!$Version) { $Version = $oldVersion }
        $family = $properties.UpgradeCode
        $upgrades = @(Rows $db 'Upgrade' '`UpgradeCode`, `VersionMin`, `VersionMax`, `ActionProperty`, `Language`, `Attributes`, `Remove`')
        foreach ($row in $upgrades) { if (!$families.ContainsKey($row[0])) { $families[$row[0]] = New-Code } }
        $other = @($upgrades | Where-Object { $_[3] -eq 'GOBOARD_OTHER_CHANNEL_FOUND' })[0][0]
        $code = New-Code
        $products.Add($code)
        Query $db "UPDATE ``Property`` SET ``Value`` = '$code' WHERE ``Property`` = 'ProductCode'"
        Query $db "UPDATE ``Property`` SET ``Value`` = 'GoBoard setup test $id' WHERE ``Property`` = 'ProductName'"
        Query $db "UPDATE ``Property`` SET ``Value`` = '$Version' WHERE ``Property`` = 'ProductVersion'"
        $targetFamily = if ($OtherChannel) { $families[$other] } else { $families[$family] }
        Query $db "UPDATE ``Property`` SET ``Value`` = '$targetFamily' WHERE ``Property`` = 'UpgradeCode'"
        Query $db 'DELETE FROM `Upgrade`'
        foreach ($row in $upgrades) {
            $mapped = $families[$row[0]]
            if ($OtherChannel) { $mapped = if ($row[0] -eq $family) { $families[$other] } else { $families[$family] } }
            $minimum = if ($row[1] -eq $oldVersion) { $Version } else { $row[1] }
            $maximum = if ($row[2] -eq $oldVersion) { $Version } else { $row[2] }
            Query $db "INSERT INTO ``Upgrade`` (``UpgradeCode``, ``VersionMin``, ``VersionMax``, ``ActionProperty``, ``Language``, ``Attributes``, ``Remove``) VALUES ('$mapped', '$minimum', '$maximum', '$($row[3])', '$($row[4])', $($row[5]), '$($row[6])')"
        }
        foreach ($row in (Rows $db 'Component' '`Component`, `ComponentId`')) {
            if (!$components.ContainsKey($row[1])) { $components[$row[1]] = New-Code }
            Query $db "UPDATE ``Component`` SET ``ComponentId`` = '$($components[$row[1]])' WHERE ``Component`` = '$($row[0])'"
        }
        foreach ($table in @('Registry', 'RegLocator')) {
            $keyColumn = if ($table -eq 'Registry') { 'Registry' } else { 'Signature_' }
            foreach ($row in (Rows $db $table ("``$keyColumn``, ``Key``"))) {
                if ($row[1].StartsWith('Software\GoBoard')) {
                    $key = $row[1].Replace('Software\GoBoard', "Software\GoBoard.SetupTest\$id")
                    Query $db "UPDATE ``$table`` SET ``Key`` = '$key' WHERE ``$keyColumn`` = '$($row[0])'"
                }
            }
        }
        Query $db "UPDATE ``Directory`` SET ``DefaultDir`` = 'GoBoard.SetupTest.$id' WHERE ``Directory`` = 'GoBoardMenuFolder'"
        Query $db "UPDATE ``Directory`` SET ``DefaultDir`` = 'GoBoard.SetupTest.$id' WHERE ``Directory`` = 'INSTALLFOLDER'"
        # Actions themselves are still validated in Test-Msi; fixture cannot stop
        # or restart a production GoBoard process or write its settings.
        foreach ($action in @('GoBoardStop', 'GoBoardRestart', 'GoBoardRecoverError', 'GoBoardRecoverCancel')) {
            Query $db "UPDATE ``InstallExecuteSequence`` SET ``Condition`` = '0' WHERE ``Action`` = '$action'"
        }
        $summary = $db.SummaryInformation(1)
        $summary.Property(9) = New-Code
        [void]$summary.Persist()
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) | Out-Null
        [void]$db.Commit()
        return [pscustomobject]@{ Path = $destination; Code = $code }
    } finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) | Out-Null }
}
function Install($Package, [string]$Variant, [int]$Expected = 0, [string]$Bundle = '') {
    $log = $Package.Path + '.log'
    if ($Bundle) {
        $process = Start-Process $Bundle -ArgumentList @('/quiet', '/norestart', '/log', ('"' + $log + '"')) -WindowStyle Hidden -Wait -PassThru
    } else {
        $process = Start-Process msiexec.exe -ArgumentList @('/i', ('"' + $Package.Path + '"'), '/qn', '/norestart', ('INSTALLFOLDER="' + $install + '"'), '/L*v', ('"' + $log + '"')) -WindowStyle Hidden -Wait -PassThru
    }
    if ($process.ExitCode -ne $Expected) { throw "Expected $Expected, got $($process.ExitCode). See $log" }
    $present = @($products | Where-Object { $engine.ProductState($_) -eq 5 })
    if ($present.Count -ne 1) { throw 'Switch must leave exactly one installed test product.' }
    if ($Expected -eq 0 -and $present[0] -ne $Package.Code) { throw 'Replacement did not install the destination product.' }
    $metadata = Get-Content (Join-Path $install 'release.json') -Raw | ConvertFrom-Json
    $payloadVariant = if ($metadata.PSObject.Properties.Name -contains 'setupVariant') { $metadata.setupVariant } else { 'offline' }
    if ($payloadVariant -ne $Variant) { throw 'Wrong payload after replacement.' }
    if ((Test-Path (Join-Path $install 'coreclr.dll')) -ne ($Variant -eq 'offline')) { throw 'Bundled runtime was lost or orphaned during the switch.' }
    if ($Expected -eq 0) {
        $db = $engine.OpenDatabase($Package.Path, 0)
        try { $expectedFiles = @(Rows $db 'File' '`File`').Count }
        finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) | Out-Null }
        if (@(Get-ChildItem $install -Recurse -File).Count -ne $expectedFiles) { throw 'Switch left missing or orphaned payload files.' }
    }
    $marker = Get-ItemProperty "HKCU:\Software\GoBoard.SetupTest\$id"
    $markerVariant = if ($marker.PSObject.Properties.Name -contains 'SetupVariant') { $marker.SetupVariant } else { 'offline' }
    if ($markerVariant -ne $Variant) { throw 'Variant registry marker disagrees with payload.' }
    Write-Output "PASS: $([IO.Path]::GetFileName($Package.Path)) => $Expected, one $Variant installation."
}
function Uninstall($Package) {
    $log = $Package.Path + '.uninstall.log'
    $process = Start-Process msiexec.exe -ArgumentList @('/x', $Package.Code, '/qn', '/norestart', '/L*v', ('"' + $log + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Fixture uninstall failed: $($Package.Code). See $log" }
    Assert-Removed
    Write-Output "PASS: $([IO.Path]::GetFileName($Package.Path)) uninstall removed its registration, payload, shortcuts and registry markers."
}
function Assert-Removed {
    # Exit code zero alone can hide leftover advertised registrations or shared
    # component clients. Verify the actual resources as well as product states.
    foreach ($code in $products) {
        if ($engine.ProductState($code) -ne -1) { throw "Uninstall left product $code registered. See $root" }
    }
    if ((Test-Path -LiteralPath $install) -and @(Get-ChildItem -LiteralPath $install -Recurse -File).Count) { throw 'Uninstall left payload files.' }
    $menu = Join-Path ([Environment]::GetFolderPath('Programs')) "GoBoard.SetupTest.$id"
    if (Test-Path -LiteralPath $menu) { throw 'Uninstall left the fixture Start menu folder.' }
    $marker = Get-ItemProperty "HKCU:\Software\GoBoard.SetupTest\$id" -ErrorAction SilentlyContinue
    if ($marker -and ($marker.PSObject.Properties.Name -contains 'InstallFolder' -or
                     $marker.PSObject.Properties.Name -contains 'Shortcuts' -or
                     $marker.PSObject.Properties.Name -contains 'SetupVariant')) { throw 'Uninstall left fixture registry markers.' }
}
try {
    $db = $engine.OpenDatabase((Resolve-Path $OfflineMsi).Path, 0)
    try { $development = @(Rows $db 'Upgrade' '`ActionProperty`' | Where-Object { $_[0] -eq 'GOBOARD_BETA_CHANNEL_FOUND' }).Count -gt 0 }
    finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) | Out-Null }
    if ($development -and (!$PublishedStableMsi -or !$PublishedBetaMsi)) { throw 'Development switching checks require published Stable and Beta MSI baselines.' }
    $offline = Clone $OfflineMsi 'offline'
    $standard = Clone $StandardMsi 'standard'
    $rebuilt = Clone $OfflineMsi 'offline-rebuild'
    # Deliberately use fixture-only high/low versions without changing source tags.
    $newerVersion = if ($development) { '0.255.65535' } else { '254.0.1' }
    $newer = Clone $StandardMsi 'newer-standard' $newerVersion
    if ($development) {
        $stable = Clone $PublishedStableMsi 'published-stable'
        $beta = Clone $PublishedBetaMsi 'published-beta'
    } else {
        $other = Clone $OfflineMsi 'other-channel-offline' '0.0.1' $true
    }
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    # Exercise removal of each variant directly, not just the final destination
    # of the switching chain. Both must remove shortcuts as well as files.
    Install $offline offline
    Uninstall $offline
    Install $standard standard
    Uninstall $standard
    Install $offline offline
    $bundlePath = ''
    if ($StandardSetup) {
        # Same production Burn authoring, replacing only bundle identity and MSI
        # with disposable fixtures. /quiet + default consent=0 cannot install .NET.
        $bundleRoot = Join-Path $root 'bundle'
        New-Item -ItemType Directory $bundleRoot | Out-Null
        Copy-Item "$PSScriptRoot\Standard\*" $bundleRoot -Exclude bin,obj
        $wxs = Join-Path $bundleRoot 'Bundle.wxs'
        (Get-Content $wxs -Raw).Replace('07257B87-EEA1-4E41-9B2B-7CA60B2B455F', [Guid]::NewGuid().ToString()) | Set-Content $wxs
        $project = Join-Path $bundleRoot 'GoBoard.Standard.wixproj'
        $wixBin = dotnet msbuild $project -getProperty:WixBinDir
        dotnet (Join-Path $wixBin 'wix.dll') burn extract $StandardSetup -o "$bundleRoot\payload" -oba "$bundleRoot\ba"
        if ($LASTEXITCODE -ne 0) { throw 'Could not extract fixture prerequisite.' }
        $props = @('-p:ReleaseVersion=0.0.1-beta.1', '-p:ProductVersion=0.0.1', "-p:StandardMsi=$($standard.Path)", "-p:PrerequisiteDir=$bundleRoot\payload\WixAttachedContainer\", "-p:OutputPath=$bundleRoot\output\")
        dotnet restore $project --locked-mode @props
        if ($LASTEXITCODE -ne 0) { throw 'Fixture bundle restore failed.' }
        dotnet build $project -c Release --no-restore @props
        if ($LASTEXITCODE -ne 0) { throw 'Fixture bundle build failed.' }
        $bundlePath = Join-Path $bundleRoot 'output\GoBoard-0.0.1-beta.1-win-x64-standard.exe'
    }
    Install $standard standard 0 $bundlePath
    if ($bundlePath) {
        $text = Get-Content ($standard.Path + '.log') -Raw
        if ($text -notmatch 'default registration: None, ba requested registration: None' -or $text -notmatch 'Removing cached bundle:') { throw 'Burn must unregister and remove its cache after installing its permanent packages.' }
        if ($text -match 'Launching elevated engine') { throw 'Present-runtime setup must not request elevation.' }
        Write-Output 'PASS: Standard Burn chain ran unelevated, installed the fixture MSI, and removed its own registration/cache.'
    }
    Install $offline offline
    Install $rebuilt offline 1603
    Install $newer standard
    Install $standard standard 1603
    if ($development) {
        # Preserve the published version and Upgrade/LaunchCondition tables.
        # Both directions must leave one registration and a complete payload.
        Install $stable offline
        Install $standard standard 0 $bundlePath
        Install $beta offline
        Install $offline offline
        Install $stable offline
        Install $offline offline
        Install $beta offline
        Install $standard standard 0 $bundlePath
        Install $newer standard
        Install $beta offline
        Write-Output 'PASS: Development upgrades/downgrade rejection, and both setup variants switching to/from published Stable and Beta.'
    } else {
        Install $other offline
        Install $standard standard
    }
} finally {
    foreach ($code in $products) {
        if ($engine.ProductState($code) -ne -1) {
            $process = Start-Process msiexec.exe -ArgumentList @('/x', $code, '/qn', '/norestart', '/L*v', ('"' + (Join-Path $root "uninstall-$code.log") + '"')) -WindowStyle Hidden -Wait -PassThru
            if ($process.ExitCode -ne 0) { Write-Error "Fixture uninstall failed: $code. See $root" }
        }
    }
    Assert-Removed
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine) | Out-Null
}
Write-Output "Isolated switching checks passed. Logs: $root. Production GoBoard and shared .NET were not modified."
