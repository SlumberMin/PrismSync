# Build the native 棱镜同步 app with the .NET Framework compiler that ships with Windows.
# No SDK, no NuGet, no runtime install: one self-contained exe.
param(
    [string]$Out = "",
    [string]$Csc = "",
    [switch]$Run,
    [switch]$NoRun,
    [string]$RunArgs = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$src = Join-Path $root "src"
if (-not $Csc) { $Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
if (-not (Test-Path -LiteralPath $Csc)) { throw "csc.exe not found: $Csc" }

if (-not $Out) {
    $dist = Join-Path $root "dist"
    if (-not (Test-Path -LiteralPath $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
    $Out = Join-Path $dist "PrismSync.exe"
    $defaultOut = $true
}
$outDir = Split-Path -Parent $Out
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# icon (generated once, kept in assets)
$icon = Join-Path $root "assets\app.ico"
if (-not (Test-Path -LiteralPath $icon)) {
    $maker = Join-Path $root "tools\make_icon.py"
    if (Test-Path -LiteralPath $maker) {
        try { & python $maker 2>&1 | Out-Null } catch { Write-Warning "icon generation failed: $_" }
    }
}

$files = @(Get-ChildItem -LiteralPath $src -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
if ($files.Count -eq 0) { throw "no sources under $src" }

$cscArgs = @(
    "/nologo", "/target:winexe", "/platform:x64", "/optimize+", "/codepage:65001",
    "/out:$Out",
    "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll", "/r:System.Management.dll"
)
$manifest = Join-Path $root "app.manifest"
if (Test-Path -LiteralPath $manifest) { $cscArgs += "/win32manifest:$manifest" }
if (Test-Path -LiteralPath $icon) { $cscArgs += "/win32icon:$icon" }

$keymap = Join-Path $root "keymap\keymap.json"
if (Test-Path -LiteralPath $keymap) { $cscArgs += "/resource:$keymap,keymap.json" }

$cscArgs += $files
Write-Host ("[build] csc -> {0} ({1} sources)" -f $Out, $files.Count)
$output = & $Csc @cscArgs 2>&1
$code = $LASTEXITCODE
if ($output) { $output | ForEach-Object { Write-Host $_ } }
if ($code -ne 0) { throw "compile failed (exit $code)" }
if (-not (Test-Path -LiteralPath $Out)) { throw "compiler produced no output" }

$size = (Get-Item -LiteralPath $Out).Length
Write-Host ("[build] OK  {0:N1} KB  {1}" -f ($size / 1KB), $Out)

# A normal build also refreshes the double-clickable exe in the project root.
if ($defaultOut) {
    $rootExe = Join-Path (Split-Path -Parent $root) "PrismSync.exe"
    try {
        Copy-Item -LiteralPath $Out -Destination $rootExe -Force
        Write-Host ("[build] root exe updated: {0}" -f $rootExe)
    } catch { Write-Warning ("could not update root exe: {0}" -f $_) }
}

if ($Run -and -not $NoRun) {
    Write-Host "[build] launching..."
    $p = Start-Process -FilePath $Out -ArgumentList $RunArgs -PassThru
    Write-Host ("[build] pid {0}" -f $p.Id)
}
