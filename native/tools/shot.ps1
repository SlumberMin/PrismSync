# Launch the app, screenshot its window, optionally leave it running.
# Uses PrintWindow so the shot is correct even when the window is covered.
param(
    [string]$Out = "",
    [string]$Exe = "",
    [string]$RunArgs = "--no-elevate --no-devices",
    [int]$WaitMs = 2200,
    [switch]$Keep
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public struct RECT { public int Left, Top, Right, Bottom; }
public class Win32Shot {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
}
"@

$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $root "dist\棱镜同步.exe" }
if (-not $Out) { $Out = Join-Path $root ("shots\shot_" + (Get-Date -Format "HHmmss") + ".png") }
$outDir = Split-Path -Parent $Out
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$exeName = [System.IO.Path]::GetFileNameWithoutExtension($Exe)
$existing = Get-Process -Name $exeName -ErrorAction SilentlyContinue
if ($existing) { $existing | Stop-Process -Force; Start-Sleep -Milliseconds 400 }

$p = Start-Process -FilePath $Exe -ArgumentList $RunArgs -PassThru
Start-Sleep -Milliseconds $WaitMs

$proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
if (-not $proc) { throw "app exited early" }
for ($i = 0; $i -lt 20; $i++) {
    $proc.Refresh()
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 200
}
$h = $proc.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { throw "no main window" }
[Win32Shot]::ShowWindow($h, 5) | Out-Null
[Win32Shot]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 400

$r = New-Object RECT
[Win32Shot]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$hgt = $r.Bottom - $r.Top
if ($w -le 0 -or $hgt -le 0) { throw "bad window rect" }

$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Win32Shot]::PrintWindow($h, $hdc, 2)   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
$g.Dispose()
if (-not $ok) {
    # fallback: copy from the screen
    $bmp.Dispose()
    $bmp = New-Object System.Drawing.Bitmap $w, $hgt
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen((New-Object System.Drawing.Point $r.Left, $r.Top), [System.Drawing.Point]::Empty,
                      (New-Object System.Drawing.Size $w, $hgt))
    $g.Dispose()
}
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host ("shot: {0} ({1}x{2}) printwindow={3}" -f $Out, $w, $hgt, $ok)
if (-not $Keep) {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
