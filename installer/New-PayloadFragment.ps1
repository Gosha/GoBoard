param(
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$publish = (Resolve-Path -LiteralPath $PublishDir).Path.TrimEnd('\')

# WiX Files harvesting uses file key paths, which fail ICE38 for per-user
# installs. Give every payload component a stable HKCU key path instead.
# Relative-path hashes keep identities independent of staging path and channel.
function Get-PathId([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes('GoBoard.PerUser.Payload\' + $Path.Replace('/', '\').ToLowerInvariant())
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').Substring(0, 32)
    } finally { $sha.Dispose() }
}
function Escape-Xml([string]$Value) { [Security.SecurityElement]::Escape($Value) }

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment>')
$directories = @(Get-ChildItem -LiteralPath $publish -Directory -Recurse | Sort-Object FullName)
foreach ($directory in $directories) {
    $relative = $directory.FullName.Substring($publish.Length + 1)
    $id = Get-PathId $relative
    $parent = Split-Path $relative -Parent
    $parentId = if ($parent) { 'dir' + (Get-PathId $parent) } else { 'INSTALLFOLDER' }
    $lines.Add(('  <DirectoryRef Id="{0}"><Directory Id="dir{1}" Name="{2}" /></DirectoryRef>' -f $parentId, $id, (Escape-Xml $directory.Name)))
}
$lines.Add('  <ComponentGroup Id="PublishedFiles">')
foreach ($file in @(Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object Extension -ne '.pdb' | Sort-Object FullName)) {
    $relative = $file.FullName.Substring($publish.Length + 1)
    $id = Get-PathId $relative
    $parent = Split-Path $relative -Parent
    $directoryId = if ($parent) { 'dir' + (Get-PathId $parent) } else { 'INSTALLFOLDER' }
    $fileId = if ($relative -eq 'GoBoard.exe') { 'GoBoardExe' } else { 'file' + $id }
    $componentGuid = ([guid]::ParseExact($id, 'N')).ToString('D')
    $lines.Add(('    <Component Id="cmp{0}" Directory="{1}" Guid="{2}">' -f $id, $directoryId, $componentGuid))
    $lines.Add(('      <File Id="{0}" Source="{1}" />' -f $fileId, (Escape-Xml $file.FullName)))
    $lines.Add(('      <RegistryValue Root="HKCU" Key="Software\GoBoard\Installer\Files" Name="{0}" Type="integer" Value="1" KeyPath="yes" />' -f $id))
    $lines.Add('    </Component>')
}
foreach ($directory in $directories) {
    $id = Get-PathId $directory.FullName.Substring($publish.Length + 1)
    $lines.Add(('    <Component Id="cleanup{0}" Directory="dir{0}" Guid="*">' -f $id))
    $lines.Add(('      <RegistryValue Root="HKCU" Key="Software\GoBoard\Installer\Directories" Name="{0}" Type="integer" Value="1" KeyPath="yes" />' -f $id))
    $lines.Add(('      <RemoveFolder Id="remove{0}" On="uninstall" />' -f $id))
    $lines.Add('    </Component>')
}
$lines.Add('  </ComponentGroup>')
$lines.Add('</Fragment></Wix>')
$lines | Set-Content -LiteralPath $OutputPath -Encoding utf8
