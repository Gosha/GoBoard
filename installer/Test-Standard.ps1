param([Parameter(Mandatory)][string]$SetupPath, [Parameter(Mandatory)][string]$MsiPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$wixBin = & dotnet msbuild (Join-Path $PSScriptRoot 'Standard\GoBoard.Standard.wixproj') -getProperty:WixBinDir
if ($LASTEXITCODE -ne 0) { throw 'Could not locate WiX.' }
$root = Join-Path (Split-Path $SetupPath) 'bundle-check'
& dotnet (Join-Path $wixBin 'wix.dll') burn extract $SetupPath -o "$root\payload" -oba "$root\ba"
if ($LASTEXITCODE -ne 0) { throw 'Bundle extraction failed.' }
$msis = @(Get-ChildItem "$root\payload" -Recurse -Filter '*.msi')
if ($msis.Count -ne 1 -or (Get-FileHash $msis[0].FullName).Hash -ne (Get-FileHash $MsiPath).Hash) { throw 'Bundle must embed the verified Standard MSI exactly.' }
[xml]$manifest = Get-Content "$root\ba\manifest.xml" -Raw
$ns = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
$ns.AddNamespace('b', 'http://wixtoolset.org/schemas/v4/2008/Burn')
$registration = $manifest.SelectSingleNode('//b:Registration', $ns)
if ($registration.PerMachine -ne 'no') { throw 'Bundle must stay in the original user context.' }
$engine = New-Object -ComObject WindowsInstaller.Installer
$db = $engine.OpenDatabase((Resolve-Path $MsiPath).Path, 0)
try {
    $view = $db.OpenView('SELECT `Value` FROM `Property` WHERE `Property` = ''ProductName''')
    try {
        $view.Execute()
        $record = $view.Fetch()
        try { $productName = $record.StringData(1) }
        finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) | Out-Null }
    } finally { $view.Close(); [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null }
} finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) | Out-Null
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine) | Out-Null
}
if ($registration.Arp.DisplayName -cne "$productName — Standard setup") { throw 'Bundle title must preserve the MSI build identity.' }
$packages = $manifest.SelectNodes('//b:Chain/*', $ns)
if ($packages.Count -ne 2 -or $packages[0].Id -ne 'RuntimePrerequisite' -or $packages[1].Id -ne 'GoBoard') { throw 'Runtime verification must precede GoBoard.' }
foreach ($package in $packages) {
    if ($package.PerMachine -ne 'no' -or $package.Permanent -ne 'yes' -or $package.Cache -ne 'remove' -or $package.Vital -ne 'yes') { throw 'Unexpected scope, registration, cache or failure behavior.' }
}
if ($packages[0].DetectCondition -ne '0' -or $packages[0].InstallArguments -ne '/ui:[WixBundleUILevel] /accept:[AcceptRuntimeDownload]') { throw 'Compatibility check and consent must run on every setup.' }
if (@($manifest.SelectNodes('//b:Payload', $ns) | Where-Object { $_.GetAttribute('Packaging') -eq 'external' }).Count) { throw 'App and runtime probe must be embedded.' }
& (Join-Path $PSScriptRoot 'Test-Prerequisite.ps1') -HelperDir (Get-ChildItem "$root\payload" -Recurse -Filter Prerequisite.exe | Select-Object -First 1).DirectoryName
Write-Output "Standard bundle extracted and embedded MSI verified: $root"
