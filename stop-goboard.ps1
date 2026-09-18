$ErrorActionPreference = 'Stop'
$stop = $null
if ([Threading.EventWaitHandle]::TryOpenExisting('Local\GoBoard.Runtime.Stop', [ref]$stop)) {
    try { $stop.Set() | Out-Null } finally { $stop.Dispose() }
    Write-Output 'Requested graceful GoBoard shutdown.'
} else {
    Write-Output 'GoBoard is not running in this Windows session.'
}
