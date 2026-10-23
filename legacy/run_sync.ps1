# RGB sync launcher - the laptop backlight needs Clevo WMI, which requires admin.
# Just run this script; it re-launches itself elevated (click "Yes" on the UAC prompt).
#
#   .\run_sync.ps1                     # flow, 30 fps, all three devices
#   .\run_sync.ps1 -Effect rainbow     # three devices, one cycling hue
#   .\run_sync.ps1 -Effect wave        # wave on the keyboard, laptop follows the head
#   .\run_sync.ps1 -NoLaptop           # peripherals only (no admin needed)
#   .\run_sync.ps1 -NoHeadset          # skip the MCHOSE headset
#
# Devices: laptop (Clevo WMI) + AULA HERO 68 HE (per-key HID) +
#          MCHOSE V9 Turbo+ headset (0x6008, report id 0xAA)

param(
    [ValidateSet("flow", "wave", "comet", "rainbow", "breathe", "hue", "static")]
    [string]$Effect = "flow",
    [int]$Fps = 30,
    [switch]$Uniform,
    [switch]$NoLaptop,
    [switch]$NoHeadset
)

$ErrorActionPreference = "Continue"
$root = $PSScriptRoot

function Is-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not $NoLaptop -and -not (Is-Admin)) {
    Write-Host "Requesting administrator rights (Clevo WMI needs them)..." -ForegroundColor Yellow
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"",
                 "-Effect", $Effect, "-Fps", $Fps)
    if ($Uniform) { $argList += "-Uniform" }
    if ($NoHeadset) { $argList += "-NoHeadset" }
    Start-Process powershell -Verb RunAs -ArgumentList $argList
    exit 0
}

$pyArgs = @("$root\rgbsync\main.py", "--effect", $Effect, "--fps", $Fps)
if ($Uniform) { $pyArgs += "--uniform" }
if ($NoLaptop) { $pyArgs += "--no-laptop" }
if ($NoHeadset) { $pyArgs += "--no-mouse" }

Write-Host ""
Write-Host "  RGB sync engine" -ForegroundColor Cyan
Write-Host ("  effect={0}  fps={1}  uniform={2}" -f $Effect, $Fps, [bool]$Uniform) -ForegroundColor DarkGray
Write-Host ("  laptop={0}  keyboard=True  headset={1}" -f (-not $NoLaptop), (-not $NoHeadset)) -ForegroundColor DarkGray
Write-Host "  Ctrl+C to stop" -ForegroundColor DarkGray
Write-Host ""

Set-Location $root
& python @pyArgs
