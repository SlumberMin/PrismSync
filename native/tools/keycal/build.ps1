# 编译键位校准工具（独立小 exe，不依赖主程序）
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Split-Path (Split-Path $here -Parent) -Parent   # native
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $here "键盘校准.exe"
$icon = Join-Path $root "assets\app.ico"

$src = @(
    (Join-Path $root "src\Devices\HidNative.cs"),
    (Join-Path $root "src\Core\UsageLayout.cs"),
    (Join-Path $here "KeyCal.cs")
)
$cscArgs = @("/nologo", "/target:winexe", "/platform:x64", "/optimize+", "/codepage:65001",
             "/out:$out", "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll",
             "/r:System.Windows.Forms.dll")
if (Test-Path -LiteralPath $icon) { $cscArgs += "/win32icon:$icon" }
$cscArgs += $src
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "compile failed" }
Write-Host ("OK -> {0}  ({1:N1} KB)" -f $out, ((Get-Item -LiteralPath $out).Length / 1KB))
