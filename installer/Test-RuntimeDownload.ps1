param([string]$OutputDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\runtime-download'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[xml]$runtime = Get-Content (Join-Path $PSScriptRoot 'runtime.xml')
$uri = [uri]$runtime.Runtime.Url
if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'builds.dotnet.microsoft.com') { throw 'Expected a pinned Microsoft HTTPS URL.' }
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$file = Join-Path $OutputDir 'windowsdesktop-runtime.exe'
Invoke-WebRequest $uri -OutFile $file
if ((Get-FileHash $file -Algorithm SHA512).Hash -ne $runtime.Runtime.Sha512) { throw 'Runtime download SHA-512 differs from the pin.' }
$signature = Get-AuthenticodeSignature $file
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|, )O=Microsoft Corporation(?:,|$)') { throw 'Runtime must have a valid Microsoft Authenticode signature.' }
Write-Output "Verified Microsoft .NET $($runtime.Runtime.Version) download: SHA-512 and Authenticode. The executable was not run."
