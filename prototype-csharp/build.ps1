# Builds SmartReticle with the C# compiler that ships with Windows (.NET Framework 4.x). No installs needed.
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1        (app only)
#         powershell -ExecutionPolicy Bypass -File build.ps1 -Tests (app + test tools)
param([switch]$Tests)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }
$out = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
$src = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName

& $csc /nologo /target:winexe /optimize+ /platform:x64 "/win32manifest:$root\app.manifest" "/out:$out\SmartReticle.exe" @refs @src
if ($LASTEXITCODE -ne 0) { throw 'App build failed' }
Write-Host "Built $out\SmartReticle.exe"

if ($Tests) {
    $lib = $src | Where-Object { $_ -notmatch '\\(Program|SettingsForm)\.cs$' }
    & $csc /nologo /target:exe /platform:x64 "/win32manifest:$root\app.manifest" "/out:$out\ReticleProbe.exe" @refs @lib "$root\tests\ReticleProbe.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Probe build failed' }
    & $csc /nologo /target:winexe /platform:x64 "/out:$out\CursorLab.exe" @refs "$root\src\Native.cs" "$root\tests\CursorLab.cs"
    if ($LASTEXITCODE -ne 0) { throw 'CursorLab build failed' }
    Write-Host "Built test tools in $out"
}
