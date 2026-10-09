# ==============================================================================
# Build Script: Build All Logitech T650 Windows PTP Installers
# Compiles both the Bridge Daemon MSI and the Virtual PTP Driver MSI
# ==============================================================================

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$bridgeScript = Join-Path $scriptDir "T650Bridge\Build-Bridge-Installer.ps1"
$driverScript = Join-Path $scriptDir "VirtualPtpDriver\Build-Driver-Installer.ps1"
$distDir = Resolve-Path (Join-Path $scriptDir "..\Dist")

Write-Host "==========================================================" -ForegroundColor Magenta
Write-Host " Building All Logitech T650 Windows PTP MSI Installers" -ForegroundColor Magenta
Write-Host "==========================================================" -ForegroundColor Magenta

# 1. Build Bridge Daemon Installer
Write-Host "`n>>> Building Bridge Daemon Installer..." -ForegroundColor Cyan
& pwsh -File "$bridgeScript"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[Error] Bridge installer build failed!" -ForegroundColor Red
    Exit 1
}

# 2. Build Virtual PTP Driver Installer
Write-Host "`n>>> Building Virtual PTP Driver Installer..." -ForegroundColor Cyan
& pwsh -File "$driverScript"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[Error] Driver installer build failed!" -ForegroundColor Red
    Exit 1
}

# 3. Final Summary
Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " All Installers Built Successfully!" -ForegroundColor Green
Write-Host " Destination Folder: $distDir" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green

Get-ChildItem -Path "$distDir\*.msi" | Select-Object Name, @{Name="Size (KB)"; Expression={[math]::Round($_.Length / 1KB, 1)}}, LastWriteTime | Format-Table -AutoSize
