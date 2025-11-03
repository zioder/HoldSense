# Build MSIX package for HoldSense
param(
    [string]$Version = "1.0.0.0",
    [string]$SourceDir = "..\publish\HoldSense",
    [string]$OutputDir = "..\msix_output"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Building HoldSense MSIX Package v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Check if makeappx is available
$makeappx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\makeappx.exe"
if (-not (Test-Path $makeappx)) {
    # Try to find it in the path
    $makeappx = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    if (-not $makeappx) {
        Write-Error "makeappx.exe not found. Please install Windows SDK."
        Write-Host "Download from: https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/" -ForegroundColor Yellow
        exit 1
    }
    $makeappx = $makeappx.Source
}

Write-Host "Using makeappx: $makeappx" -ForegroundColor Gray

# Create output directory
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

# Create staging directory
$stagingDir = "staging"
if (Test-Path $stagingDir) {
    Remove-Item -Recurse -Force $stagingDir
}
New-Item -ItemType Directory -Path $stagingDir | Out-Null

Write-Host "`nPreparing MSIX package contents..." -ForegroundColor Green

# Copy application files
Write-Host "Copying application files..."
Copy-Item -Recurse "$SourceDir\*" "$stagingDir\" -Force

# Copy and update manifest
Write-Host "Updating manifest version to $Version..."
$manifestContent = Get-Content "AppxManifest.xml" -Raw
$manifestContent = $manifestContent -replace 'Version="[^"]*"', "Version=`"$Version`""
$manifestContent | Out-File "$stagingDir\AppxManifest.xml" -Encoding UTF8

# Create Assets folder and copy icons
Write-Host "Preparing assets..."
if (-not (Test-Path "$stagingDir\Assets")) {
    New-Item -ItemType Directory -Path "$stagingDir\Assets" | Out-Null
}

# Check if assets exist, if not, copy from app icon
$appIcon = "$SourceDir\HoldSense.ico"
if (Test-Path $appIcon) {
    # For now, copy the .ico as placeholders
    # In production, you should create proper PNG assets at the required sizes
    Copy-Item $appIcon "$stagingDir\Assets\StoreLogo.png" -Force -ErrorAction SilentlyContinue
    Copy-Item $appIcon "$stagingDir\Assets\Square44x44Logo.png" -Force -ErrorAction SilentlyContinue
    Copy-Item $appIcon "$stagingDir\Assets\Square150x150Logo.png" -Force -ErrorAction SilentlyContinue
    Copy-Item $appIcon "$stagingDir\Assets\Wide310x150Logo.png" -Force -ErrorAction SilentlyContinue
    Copy-Item $appIcon "$stagingDir\Assets\SplashScreen.png" -Force -ErrorAction SilentlyContinue
}

# Copy custom assets if they exist in msix/Assets folder
if (Test-Path "Assets") {
    Copy-Item "Assets\*" "$stagingDir\Assets\" -Force -ErrorAction SilentlyContinue
}

# Create the MSIX package
Write-Host "`nCreating MSIX package..." -ForegroundColor Green
$outputMsix = Join-Path $OutputDir "HoldSense_$($Version.Replace('.', '_')).msix"

& $makeappx pack /d $stagingDir /p $outputMsix /o

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create MSIX package!"
    exit 1
}

# Clean up staging directory
Remove-Item -Recurse -Force $stagingDir

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "MSIX package created successfully!" -ForegroundColor Green
Write-Host "Output: $outputMsix" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$size = (Get-Item $outputMsix).Length / 1MB
Write-Host "Package size: $([math]::Round($size, 2)) MB" -ForegroundColor Yellow

Write-Host "`nNote: This MSIX is unsigned. For distribution, you need to sign it with:" -ForegroundColor Yellow
Write-Host "  signtool sign /fd SHA256 /a /f <certificate.pfx> /p <password> $outputMsix" -ForegroundColor Gray
Write-Host "`nOr use GitHub Actions with a certificate secret for automatic signing." -ForegroundColor Gray

