# Build script for HoldSense (.NET-only backend)

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0",
    [switch]$Minimal = $false
)

$ErrorActionPreference = "Stop"

$buildType = if ($Minimal) { "Minimal (optional model download)" } else { "Full (bundled ONNX model)" }

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Building HoldSense v$Version" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Runtime: $Runtime" -ForegroundColor Cyan
Write-Host "Build Type: $buildType" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

Write-Host "`nCleaning previous builds..." -ForegroundColor Yellow
if (Test-Path "publish") { Remove-Item -Recurse -Force "publish" }

Write-Host "`n[1/2] Publishing .NET application..." -ForegroundColor Green
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

Write-Host "`n[2/2] Copying resources..." -ForegroundColor Green

# Bundle ONNX model for full build only (prefer optimized model).
if (-not $Minimal) {
    if (Test-Path "yolo26n_416_int8.onnx") {
        Copy-Item "yolo26n_416_int8.onnx" "publish/HoldSense/yolo26n_416_int8.onnx" -Force
    } elseif (Test-Path "yolo26n.onnx") {
        Copy-Item "yolo26n.onnx" "publish/HoldSense/yolo26n.onnx" -Force
    } else {
        Write-Host "ONNX model not found locally. Full build will rely on in-app download." -ForegroundColor Yellow
    }
}

$configTemplate = @{
    phone_bt_address = ""
    detection_enabled = $false
    keybind_enabled = $true
    python_exe_path = ""
    backend_mode = "dotnet"
    enable_python_fallback = $false
    theme = "auto"
    webcam_index = 0
    auto_detection_downloaded = (-not $Minimal)
} | ConvertTo-Json

$configTemplate | Out-File "publish/HoldSense/bt_config.json" -Encoding UTF8

if (Test-Path "README.md") {
    Copy-Item "README.md" "publish/HoldSense/" -Force
}
if (Test-Path "LICENSE") {
    Copy-Item "LICENSE" "publish/HoldSense/" -Force
}

@"
Version: $Version
Build Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Configuration: $Configuration
Runtime: $Runtime
Build Type: $buildType
"@ | Out-File "publish/HoldSense/VERSION.txt" -Encoding UTF8

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "Output directory: publish/HoldSense" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$size = (Get-ChildItem "publish/HoldSense" -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "Total size: $([math]::Round($size, 2)) MB" -ForegroundColor Yellow
