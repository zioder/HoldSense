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
$makeappx = $null

# Try to find makeappx in common locations
$sdkPaths = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\makeappx.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22000.0\x64\makeappx.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\makeappx.exe"
)

# Try to find any SDK version dynamically
$sdkBasePath = "C:\Program Files (x86)\Windows Kits\10\bin"
if (Test-Path $sdkBasePath) {
    $latestSdk = Get-ChildItem $sdkBasePath -Directory | 
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } | 
        Sort-Object Name -Descending | 
        Select-Object -First 1
    
    if ($latestSdk) {
        $sdkPaths += Join-Path $latestSdk.FullName "x64\makeappx.exe"
    }
}

# Check each path
foreach ($path in $sdkPaths) {
    if (Test-Path $path) {
        $makeappx = $path
        break
    }
}

# If not found in specific paths, try to find it in PATH
if (-not $makeappx) {
    $makeappxCmd = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    if ($makeappxCmd) {
        $makeappx = $makeappxCmd.Source
    }
}

# If still not found, fail with helpful message
if (-not $makeappx) {
    Write-Error "makeappx.exe not found. Please install Windows SDK."
    Write-Host "Download from: https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/" -ForegroundColor Yellow
    Write-Host "`nSearched locations:" -ForegroundColor Gray
    foreach ($path in $sdkPaths) {
        Write-Host "  - $path" -ForegroundColor Gray
    }
    exit 1
}

Write-Host "Using makeappx: $makeappx" -ForegroundColor Gray

# Resolve script root and normalize paths to be robust regardless of current directory
$ScriptRoot = $PSScriptRoot
if (-not $ScriptRoot) {
    $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}

# Resolve SourceDir to absolute path
try {
    $SourceDir = (Resolve-Path -LiteralPath $SourceDir).Path
} catch {
    Write-Error "Source directory not found: $SourceDir"
    exit 1
}

# Ensure OutputDir exists and resolve to absolute path
if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path

# Create output directory
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

# Create staging directory under the script folder to avoid cwd issues
$stagingDir = Join-Path $ScriptRoot "staging"
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
$manifestPath = Join-Path $ScriptRoot "AppxManifest.xml"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    Write-Error "AppxManifest.xml not found at $manifestPath"
    exit 1
}
$manifestContent = Get-Content $manifestPath -Raw
$manifestContent = $manifestContent -replace 'Version="[^"]*"', "Version=`"$Version`""
# Use UTF8 without BOM to preserve XML declaration
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $stagingDir "AppxManifest.xml"), $manifestContent, $utf8NoBom)

# Create Assets folder and copy icons
Write-Host "Preparing assets..."
if (-not (Test-Path (Join-Path $stagingDir "Assets"))) {
    New-Item -ItemType Directory -Path (Join-Path $stagingDir "Assets") | Out-Null
}

# Check if assets exist, if not, copy from app icon
$appIcon = Join-Path $SourceDir "HoldSense.ico"
if (Test-Path $appIcon) {
    # For now, copy the .ico as placeholders
    # In production, you should create proper PNG assets at the required sizes
    Copy-Item $appIcon (Join-Path $stagingDir "Assets\StoreLogo.png") -Force -ErrorAction SilentlyContinue
    Copy-Item $appIcon (Join-Path $stagingDir "Assets\Square44x44Logo.png") -Force -ErrorAction SilentlyContinue
    Copy-Item $appIcon (Join-Path $stagingDir "Assets\Square150x150Logo.png") -Force -ErrorAction SilentlyContinue
    Copy-Item $appIcon (Join-Path $stagingDir "Assets\Wide310x150Logo.png") -Force -ErrorAction SilentlyContinue
    Copy-Item $appIcon (Join-Path $stagingDir "Assets\SplashScreen.png") -Force -ErrorAction SilentlyContinue
}

# Copy custom assets if they exist in msix/Assets folder
if (Test-Path (Join-Path $ScriptRoot "Assets")) {
    Copy-Item (Join-Path $ScriptRoot "Assets\*") (Join-Path $stagingDir "Assets\") -Force -ErrorAction SilentlyContinue
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

