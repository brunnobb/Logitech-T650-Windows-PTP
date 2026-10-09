# ==============================================================================
# Helper Script: Bump Version Across Entire T650 Project & Rebuild MSIs
# Usage: pwsh -File "Installer/Bump-Version.ps1" -NewVersion "1.1.0"
# ==============================================================================

param(
    [Parameter(Mandatory = $true)]
    [string]$NewVersion
)

$ErrorActionPreference = "Stop"

# 1. Validate Semantic Version (e.g. 1.0.1 or 1.1.0)
if ($NewVersion -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "[Error] Version must follow Semantic Versioning 'MAJOR.MINOR.PATCH' (e.g. 1.1.0)!" -ForegroundColor Red
    Exit 1
}

$fourPartVersion = "$NewVersion.0"
$todayDate = (Get-Date).ToString("MM/dd/yyyy")

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Bumping T650 Project Version -> $NewVersion ($fourPartVersion)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 2. Update T650Bridge.csproj
$csprojPath = Join-Path $repoRoot "T650-Windows-PTP-Driver\T650Bridge\T650Bridge.csproj"
if (Test-Path $csprojPath) {
    Write-Host "`n[1/4] Updating $csprojPath..." -ForegroundColor Yellow
    $csprojContent = Get-Content $csprojPath -Raw
    $csprojContent = [regex]::Replace($csprojContent, '<Version>[\d\.]+</Version>', "<Version>$NewVersion</Version>")
    $csprojContent = [regex]::Replace($csprojContent, '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$fourPartVersion</AssemblyVersion>")
    $csprojContent = [regex]::Replace($csprojContent, '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$fourPartVersion</FileVersion>")
    Set-Content -Path $csprojPath -Value $csprojContent -Encoding utf8
    Write-Host "  -> Updated to Version $NewVersion ($fourPartVersion)" -ForegroundColor Green
}

# 3. Update Installer\T650Bridge\T650Bridge.wxs
$bridgeWxsPath = Join-Path $repoRoot "Installer\T650Bridge\T650Bridge.wxs"
if (Test-Path $bridgeWxsPath) {
    Write-Host "`n[2/4] Updating $bridgeWxsPath..." -ForegroundColor Yellow
    $wxsContent = Get-Content $bridgeWxsPath -Raw
    $wxsContent = [regex]::Replace($wxsContent, 'Version="[\d\.]+"', "Version=""$fourPartVersion""")
    Set-Content -Path $bridgeWxsPath -Value $wxsContent -Encoding utf8
    Write-Host "  -> Updated to Version $fourPartVersion" -ForegroundColor Green
}

# 4. Update Installer\VirtualPtpDriver\VirtualPtpDriver.wxs
$driverWxsPath = Join-Path $repoRoot "Installer\VirtualPtpDriver\VirtualPtpDriver.wxs"
if (Test-Path $driverWxsPath) {
    Write-Host "`n[3/4] Updating $driverWxsPath..." -ForegroundColor Yellow
    $driverWxsContent = Get-Content $driverWxsPath -Raw
    $driverWxsContent = [regex]::Replace($driverWxsContent, 'Version="[\d\.]+"', "Version=""$fourPartVersion""")
    Set-Content -Path $driverWxsPath -Value $driverWxsContent -Encoding utf8
    Write-Host "  -> Updated to Version $fourPartVersion" -ForegroundColor Green
}

# 5. Update VirtualPtpDriver.inx
$inxPath = Join-Path $repoRoot "T650-Windows-PTP-Driver\VirtualPtpDriver\VirtualPtpDriver.inx"
if (Test-Path $inxPath) {
    Write-Host "`n[4/4] Updating $inxPath..." -ForegroundColor Yellow
    $inxContent = Get-Content $inxPath -Raw
    $inxContent = [regex]::Replace($inxContent, 'DriverVer\s*=\s*[\d\/]+,[\d\.]+', "DriverVer   = $todayDate,$fourPartVersion")
    Set-Content -Path $inxPath -Value $inxContent -Encoding utf8
    Write-Host "  -> Updated DriverVer to $todayDate,$fourPartVersion" -ForegroundColor Green
}

# 6. Rebuild All Installers
Write-Host "`n>>> Rebuilding all installers for version $NewVersion..." -ForegroundColor Cyan
$buildScript = Join-Path $scriptDir "Build-All-Installers.ps1"
& pwsh -File "$buildScript"

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " Version bump to $NewVersion complete!" -ForegroundColor Green
Write-Host " Git release instructions:" -ForegroundColor Yellow
Write-Host "   git add -u" -ForegroundColor White
Write-Host "   git commit -m `"chore(release): bump version to $NewVersion`"" -ForegroundColor White
Write-Host "   git tag -a v$NewVersion -m `"Release v$NewVersion`"" -ForegroundColor White
Write-Host "   git push origin main --tags" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
