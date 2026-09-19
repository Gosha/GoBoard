$ErrorActionPreference = 'Stop'
# Download only assets, mapping text and the upstream license; execute no upstream code.
$revision = 'ba103f3b0afa9dab80447aa2e7e2ed80b6bd80e4'
$base = "https://raw.githubusercontent.com/tplai/kbsim/$revision"
$destination = Join-Path $PSScriptRoot 'upstream/kbsim'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Invoke-WebRequest "$base/LICENSE.md" -OutFile (Join-Path $destination 'LICENSE.md')
foreach ($pack in @('blackink','holypanda','topre')) {
    $module = if ($pack -eq 'blackink') { 'inkblack' } else { $pack }
    Invoke-WebRequest "$base/src/features/audioModules/$module.js" -OutFile (Join-Path $destination "$module.js")
    foreach ($edge in @('press','release')) {
        $folder = Join-Path $destination "$pack/$edge"
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        $names = @('SPACE','ENTER','BACKSPACE')
        $names += if ($edge -eq 'press') { @('GENERIC_R0','GENERIC_R1','GENERIC_R2','GENERIC_R3','GENERIC_R4') } else { @('GENERIC') }
        foreach ($name in $names) {
            Invoke-WebRequest "$base/src/assets/audio/$pack/$edge/$name.mp3" -OutFile (Join-Path $folder "$name.mp3")
        }
    }
}
Write-Output "Downloaded 36 original MP3s, three mappings and MIT license from $revision."
