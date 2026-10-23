# Bridge process for Clevo WMI keyboard lighting (laptop, e.g. Colorful X15 AT23 / NP50SNE).
# Reads hex uint32 values on stdin, one per line, and issues CLEVO_GET.SetKBLED for each.
# ASCII-only on purpose: PowerShell 5.1 mis-decodes non-ASCII UTF-8 scripts.
$ErrorActionPreference = "Stop"
# PowerShell 5.1 defaults to the ANSI code page; force UTF-8 so the Python side
# can decode the handshake reliably.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$OutDir = Join-Path $env:TEMP "rgbsync"
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$LogFile = Join-Path $OutDir "clevo_bridge.log"

function L($m) { [System.IO.File]::AppendAllText($LogFile, "$m`r`n", [System.Text.Encoding]::UTF8) }

try {
    $o = Get-WmiObject -Query "select * from CLEVO_GET" -Namespace "root\WMI" -ErrorAction Stop
    L "bridge ready: CLEVO_GET acquired"
    [Console]::Out.WriteLine("READY")
    [Console]::Out.Flush()
    while ($true) {
        $line = [Console]::In.ReadLine()
        if ($null -eq $line) { break }
        $line = $line.Trim()
        if ($line -eq "") { continue }
        if ($line -eq "QUIT") { break }
        try {
            $val = [uint32][System.Convert]::ToUInt32($line, 16)
            $o.SetKBLED($val) | Out-Null
        } catch {
            L "err on $line : $($_.Exception.Message)"
        }
    }
    L "bridge exit"
} catch {
    L "FATAL: $($_.Exception.Message)"
    [Console]::Out.WriteLine("ERROR " + $_.Exception.Message)
    [Console]::Out.Flush()
    exit 1
}
