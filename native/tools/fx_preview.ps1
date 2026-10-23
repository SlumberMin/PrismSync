# Build + run the effect preview renderer (visual self-test for the effect library).
#
#   pwsh -NoProfile -ExecutionPolicy Bypass -File native\tools\fx_preview.ps1
#
# Compiles ONLY:  native/src/Core/{Rgb,Frame,IEffect,EffectRegistry,Effects,KeyLayout}.cs
#                 native/tools/fx_preview_stubs.cs   (fake screen + mic, deterministic)
#                 native/tools/fx_preview.cs         (renderer / assertions)
# The real Capture or UI code is not needed, so this works while other files are
# being edited, and it never opens HID / capture devices.
param(
    [string]$OutDir = "",
    [string]$Csc = ""
)

$ErrorActionPreference = "Stop"
$tools = $PSScriptRoot
$root = Split-Path -Parent $tools
$src = Join-Path $root "src"

if (-not $Csc) { $Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
if (-not (Test-Path -LiteralPath $Csc)) { throw "csc.exe not found: $Csc" }

$bin = Join-Path $tools "preview_bin"
if (-not (Test-Path -LiteralPath $bin)) { New-Item -ItemType Directory -Path $bin | Out-Null }
$exe = Join-Path $bin "fx_preview.exe"

$files = @(
    (Join-Path $src "Core\Rgb.cs"),
    (Join-Path $src "Core\Frame.cs"),
    (Join-Path $src "Core\IEffect.cs"),
    (Join-Path $src "Core\EffectRegistry.cs"),
    (Join-Path $src "Core\Effects.cs"),
    (Join-Path $src "Core\KeyLayout.cs"),
    (Join-Path $tools "fx_preview_stubs.cs"),
    (Join-Path $tools "fx_preview.cs")
)
foreach ($f in $files) { if (-not (Test-Path -LiteralPath $f)) { throw "missing source: $f" } }

if (-not $OutDir) { $OutDir = Join-Path $tools "fx_previews" }

$cscArgs = @(
    "/nologo", "/target:exe", "/optimize+", "/codepage:65001",
    "/out:$exe",
    "/r:System.dll", "/r:System.Drawing.dll"
)
$cscArgs += $files

Write-Host ("[fx_preview] csc -> {0}" -f $exe)
$output = & $Csc @cscArgs 2>&1
$code = $LASTEXITCODE
if ($output) { $output | ForEach-Object { Write-Host $_ } }
if ($code -ne 0) { throw "fx_preview compile failed (exit $code)" }

Write-Host ("[fx_preview] rendering -> {0}" -f $OutDir)
& $exe $OutDir
if ($LASTEXITCODE -ne 0) { throw "fx_preview run failed (exit $LASTEXITCODE)" }
exit 0
