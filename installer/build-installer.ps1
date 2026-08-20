# Builds the self-contained GpxManager.exe and packages it into a
# Windows installer (GpxManager-Setup-<version>.exe) using Inno Setup.
#
# Usage:
#   .\installer\build-installer.ps1 [-Version 1.1.0]
#
# Requires: .NET 8 SDK, Inno Setup 6 (ISCC.exe).

param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish"

Write-Host "Publishing self-contained build (win-x64)..." -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
dotnet publish (Join-Path $root "GpxManager.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidate = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
    if (Test-Path $candidate) {
        $iscc = $candidate
    } else {
        throw "ISCC.exe (Inno Setup compiler) not found. Install Inno Setup 6: winget install JRSoftware.InnoSetup"
    }
} else {
    $iscc = $iscc.Source
}

Write-Host "Building installer (version $Version)..." -ForegroundColor Cyan
$env:GPXMANAGER_VERSION = $Version
& $iscc (Join-Path $PSScriptRoot "GpxManager.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC.exe failed" }

Write-Host "Done. Installer is in publish-installer\GpxManager-Setup-$Version.exe" -ForegroundColor Green
