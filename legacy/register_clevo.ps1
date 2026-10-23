# Clevo WMI registration + GATE test for Colorful X15 AT23 (Clevo NP50SNE / NP5x_6x_7x_SNx)
# Must run as administrator. ASCII-only script; $PSScriptRoot avoids non-ASCII path issues.
$ErrorActionPreference = "Continue"

$OutDir = Join-Path $env:TEMP "rgbsync"
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$LogFile = Join-Path $OutDir "gate_clevo.txt"

$log = New-Object System.Text.StringBuilder
function L($msg) { [void]$log.AppendLine($msg); Write-Host $msg }
function Flush { [System.IO.File]::WriteAllText($LogFile, $log.ToString(), [System.Text.Encoding]::UTF8) }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
L "STEP0 isAdmin=$isAdmin"
Flush
if (-not $isAdmin) { L "ABORT: not administrator"; Flush; exit 1 }

# STEP1: copy clevomof.dll to SysWOW64
$src = Join-Path $PSScriptRoot "CLEVOMOF.dll"
$dst = "C:\Windows\SysWOW64\clevomof.dll"
if (-not (Test-Path $src)) { L "STEP1 FAIL: source DLL missing at $src"; Flush; exit 1 }
try {
    Copy-Item -LiteralPath $src -Destination $dst -Force
    L "STEP1 PASS: DLL copied to SysWOW64 ($( (Get-Item $dst).Length ) bytes)"
} catch { L "STEP1 FAIL: $($_.Exception.Message)" }
Flush

# STEP2: registry MofImagePath
try {
    if (-not (Test-Path "HKLM:\SYSTEM\CurrentControlSet\Services\WmiAcpi")) { New-Item -Path "HKLM:\SYSTEM\CurrentControlSet\Services\WmiAcpi" -Force | Out-Null }
    Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\WmiAcpi" -Name "MofImagePath" -Value "syswow64\clevomof.dll" -Type ExpandString -Force
    $v = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\WmiAcpi").MofImagePath
    L "STEP2 PASS: MofImagePath=$v"
} catch { L "STEP2 FAIL: $($_.Exception.Message)" }
Flush

# STEP3: CLEVO_GET class present?
try {
    $o = Get-WmiObject -Query "select * from CLEVO_GET" -Namespace "root\WMI" -ErrorAction Stop
    L "STEP3 PASS: CLEVO_GET found ($($o.InstanceName))"
} catch { L "STEP3 FAIL: $($_.Exception.Message)"; Flush; exit 1 }

# NOTE: 0xE007F007 etc. exceed Int32, so PowerShell parses them as negative and
# the [uint32] cast throws. Use an explicit hex parse instead.
function HexU32([string]$h) { return [uint32]::Parse($h, [System.Globalization.NumberStyles]::HexNumber) }

# STEP4: GATE - lights on (0xE007F007)
try {
    $r = $o.SetKBLED((HexU32 "E007F007"))
    L "STEP4 SetKBLED(on) ReturnValue=$($r.ReturnValue)"
} catch { L "STEP4 FAIL: $($_.Exception.Message)" }
Start-Sleep -Milliseconds 600

# STEP5: GATE - LEFT zone RED: pack = (B<<16)+(R<<8)+G = 0x0000FF00 | 0xF0000000
try {
    $r = $o.SetKBLED((HexU32 "F000FF00"))
    L "STEP5 SetKBLED(left-red) ReturnValue=$($r.ReturnValue)"
} catch { L "STEP5 FAIL: $($_.Exception.Message)" }
Start-Sleep -Milliseconds 600

# STEP6: GATE - MID zone GREEN: pack = 0x000000FF | 0xF1000000
try {
    $r = $o.SetKBLED((HexU32 "F10000FF"))
    L "STEP6 SetKBLED(mid-green) ReturnValue=$($r.ReturnValue)"
} catch { L "STEP6 FAIL: $($_.Exception.Message)" }

L "GATE DONE - check keyboard physically"
Flush
