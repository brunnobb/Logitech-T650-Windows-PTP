# ==============================================================================
# Build Script: Logitech T650 Bridge MSI Installers (PTP and Admin Editions)
# Uses WiX Toolset v3.14 to compile and package:
#   1. T650Bridge-PTP-Setup-v<Version>.msi   (Precision Touchpad Driver Client)
#   2. T650Bridge-Admin-Setup-v<Version>.msi (Standalone Admin / UIPI Bypass)
# ==============================================================================

param(
    [string]$Version,
    [ValidateSet("Both", "Ptp", "Admin")]
    [string]$Flavor = "Both"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$bridgeProj = Join-Path $repoRoot "T650-Windows-PTP-Driver\T650Bridge\T650Bridge.csproj"
$objDir = Join-Path $scriptDir "obj"
$distDir = Join-Path $repoRoot "Dist"
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir -Force | Out-Null }
$distDir = Resolve-Path $distDir

# Auto-detect version if not specified
if (-not $Version) {
    $csprojContent = Get-Content $bridgeProj -Raw
    if ($csprojContent -match '<Version>([\d\.]+)</Version>') {
        $Version = $Matches[1]
    } else {
        $Version = "1.0.0"
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Building Logitech T650 Bridge Installers v$Version (Flavor: $Flavor)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Locate WiX Toolset Binaries
$wixBin = "C:\Program Files (x86)\WiX Toolset v3.14\bin"
$candle = Join-Path $wixBin "candle.exe"
$light = Join-Path $wixBin "light.exe"

if (-not (Test-Path $candle) -or -not (Test-Path $light)) {
    Write-Host "[Error] WiX Toolset v3.14 binaries not found at '$wixBin'!" -ForegroundColor Red
    Exit 1
}

$sourceIco = Join-Path $repoRoot "T650-Windows-PTP-Driver\T650Bridge\T650.ico"
$licenseRtf = Join-Path $scriptDir "..\License.rtf"
if (-not (Test-Path $objDir)) { New-Item -ItemType Directory -Path $objDir -Force | Out-Null }

function Build-MsiPackage {
    param(
        [string]$FlavorName,
        [string]$WxsFileName,
        [string]$OutputPrefix
    )

    Write-Host "`n>>> [Building $FlavorName Edition] <<<" -ForegroundColor Yellow

    $flavorPublishDir = Join-Path $scriptDir "bin\publish-$($FlavorName.ToLower())"
    if (Test-Path $flavorPublishDir) { Remove-Item $flavorPublishDir -Recurse -Force }
    New-Item -ItemType Directory -Path $flavorPublishDir -Force | Out-Null

    # 1. Publish .NET binary for this flavor
    Write-Host "1/4 Publishing T650Bridge ($FlavorName Flavor)..." -ForegroundColor Cyan
    dotnet publish $bridgeProj -c Release -r win-x64 --self-contained false -p:Flavor=$FlavorName -o $flavorPublishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[Error] dotnet publish failed for $FlavorName!" -ForegroundColor Red
        Exit 1
    }

    if (Test-Path $sourceIco) {
        Copy-Item $sourceIco -Destination $flavorPublishDir -Force
    }

    # Copy Test Certificate into publish directory so MSI can install it into Root/Publisher
    $sourceCert = Join-Path $repoRoot "T650-Windows-PTP-Driver\VirtualPtpDriver\Package\T650TestCert.cer"
    if (Test-Path $sourceCert) {
        Copy-Item $sourceCert -Destination $flavorPublishDir -Force
    }

    # Digitally sign T650Bridge.exe with Authenticode certificate (required by Windows for uiAccess="true")
    $signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
    $thumbprint = "737386A00937ED1A64A90628A02C8850D1FA5A40"
    $publishedExe = Join-Path $flavorPublishDir "T650Bridge.exe"
    if ((Test-Path $signtool) -and (Test-Path $publishedExe)) {
        Write-Host "Signing $publishedExe for Authenticode / UIAccess..." -ForegroundColor Cyan
        & $signtool sign /sha1 $thumbprint /fd SHA256 /v $publishedExe
    }

    # 2. Compile WiX Source
    Write-Host "2/4 Compiling WiX with candle.exe..." -ForegroundColor Cyan
    $wxsPath = Join-Path $scriptDir $WxsFileName
    $wixObj = Join-Path $objDir "$OutputPrefix.wixobj"

    & $candle -nologo -arch x64 -dSourceDir="$flavorPublishDir" -dLicenseRtf="$licenseRtf" -out "$wixObj" -ext WixUIExtension -ext WixUtilExtension "$wxsPath"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[Error] candle.exe failed for $FlavorName!" -ForegroundColor Red
        Exit 1
    }

    # 3. Link MSI
    Write-Host "3/4 Linking MSI with light.exe..." -ForegroundColor Cyan
    $msiFileName = "$OutputPrefix-Setup-v$Version.msi"
    $msiOut = Join-Path $distDir $msiFileName

    & $light -nologo -out "$msiOut" -ext WixUIExtension -ext WixUtilExtension -sice:ICE69 -sice:ICE91 "$wixObj"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[Error] light.exe linking failed for $FlavorName!" -ForegroundColor Red
        Exit 1
    }

    # Convenience copy without version
    $msiConvenience = Join-Path $distDir "$OutputPrefix-Setup.msi"
    Copy-Item -Path $msiOut -Destination $msiConvenience -Force

    # If PTP, also copy to legacy T650Bridge-Setup.msi name for compatibility
    if ($FlavorName -eq "Ptp") {
        Copy-Item -Path $msiOut -Destination (Join-Path $distDir "T650Bridge-Setup.msi") -Force
    }

    # 4. Compute SHA-256 Checksum
    Write-Host "4/4 Calculating SHA-256 Checksum..." -ForegroundColor Cyan
    $hashInfo = Get-FileHash -Path $msiOut -Algorithm SHA256
    $hashHex = $hashInfo.Hash.ToLower()
    $shaFile = "$msiOut.sha256"
    "$hashHex  $msiFileName" | Set-Content -Path $shaFile -Encoding ascii

    $msiItem = Get-Item $msiOut
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " $FlavorName Edition Built Successfully!" -ForegroundColor Green
    Write-Host " Output File: $($msiItem.FullName)" -ForegroundColor White
    Write-Host " Size:        $([math]::Round($msiItem.Length / 1KB, 2)) KB" -ForegroundColor Cyan
    Write-Host " SHA-256:     $hashHex" -ForegroundColor Yellow
    Write-Host " Hash File:   $shaFile" -ForegroundColor Gray
    Write-Host "==========================================================" -ForegroundColor Green
}

if ($Flavor -eq "Both" -or $Flavor -eq "Ptp") {
    Build-MsiPackage -FlavorName "Ptp" -WxsFileName "T650Bridge.wxs" -OutputPrefix "T650Bridge-PTP"
}

if ($Flavor -eq "Both" -or $Flavor -eq "Admin") {
    Build-MsiPackage -FlavorName "Admin" -WxsFileName "T650Bridge-Admin.wxs" -OutputPrefix "T650Bridge-Admin"
}
