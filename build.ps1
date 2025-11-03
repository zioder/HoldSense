# Build script for HoldSense
# This script packages the Python backend and .NET frontend into a distributable package

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Building HoldSense v$Version" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Runtime: $Runtime" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Clean previous builds
Write-Host "`nCleaning previous builds..." -ForegroundColor Yellow
if (Test-Path "dist") { Remove-Item -Recurse -Force "dist" }
if (Test-Path "build") { Remove-Item -Recurse -Force "build" }
if (Test-Path "publish") { Remove-Item -Recurse -Force "publish" }

# Check if ONNX model exists, download if missing
if (-not (Test-Path "yolov8n.onnx")) {
    Write-Host "`nYOLOv8 model not found. Downloading..." -ForegroundColor Yellow
    python download_model.py
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to download YOLOv8 model!"
        exit 1
    }
}

# Step 1: Build Python backend with PyInstaller
Write-Host "`n[1/4] Building Python backend with PyInstaller..." -ForegroundColor Green

# Check if PyInstaller is installed
$pyinstallerInstalled = python -m pip list 2>&1 | Select-String "pyinstaller"
if (-not $pyinstallerInstalled) {
    Write-Host "PyInstaller not found. Installing..." -ForegroundColor Yellow
    python -m pip install pyinstaller
}

# Use python -m PyInstaller to avoid PATH issues
python -m PyInstaller main.spec --clean --noconfirm
if ($LASTEXITCODE -ne 0) {
    Write-Error "PyInstaller build failed!"
    exit 1
}

# Step 2: Publish .NET app
Write-Host "`n[2/4] Publishing .NET application..." -ForegroundColor Green
dotnet publish HoldSense/HoldSense.csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "publish/HoldSense"

if ($LASTEXITCODE -ne 0) {
    Write-Error ".NET publish failed!"
    exit 1
}

# Step 3: Copy Python backend to publish folder
Write-Host "`n[3/4] Copying Python backend to publish folder..." -ForegroundColor Green
$backendSource = "dist/HoldSenseBackend"
$backendDest = "publish/HoldSense/backend"

if (-not (Test-Path $backendSource)) {
    Write-Error "Python backend build not found at $backendSource"
    exit 1
}

Copy-Item -Recurse -Force $backendSource $backendDest

# Copy main.py to the publish folder (for reference or fallback)
Copy-Item "main.py" "publish/HoldSense/backend/main.py" -Force

# Copy ONNX model if not already included
if (-not (Test-Path "$backendDest/yolov8n.onnx")) {
    Copy-Item "yolov8n.onnx" "$backendDest/yolov8n.onnx" -Force
}

# Step 4: Copy additional resources
Write-Host "`n[4/4] Copying additional resources..." -ForegroundColor Green

# Create a default config file template
$configTemplate = @{
    phone_bt_address = ""
    detection_enabled = false
    keybind_enabled = true
    python_exe_path = ""
    theme = "auto"
    webcam_index = 0
} | ConvertTo-Json

$configTemplate | Out-File "publish/HoldSense/bt_config.json" -Encoding UTF8

# Copy README and LICENSE if they exist
if (Test-Path "README.md") {
    Copy-Item "README.md" "publish/HoldSense/" -Force
}
if (Test-Path "LICENSE") {
    Copy-Item "LICENSE" "publish/HoldSense/" -Force
}

# Create version file
@"
Version: $Version
Build Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Configuration: $Configuration
Runtime: $Runtime
"@ | Out-File "publish/HoldSense/VERSION.txt" -Encoding UTF8

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "Output directory: publish/HoldSense" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Display folder size
$size = (Get-ChildItem "publish/HoldSense" -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "Total size: $([math]::Round($size, 2)) MB" -ForegroundColor Yellow

