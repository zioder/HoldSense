# Clean build wrapper for HoldSense (.NET-only runtime).

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0",
    [switch]$Minimal = $false
)

$ErrorActionPreference = "Stop"

Write-Host "Running clean build..." -ForegroundColor Cyan
if (Test-Path "publish") { Remove-Item -Recurse -Force "publish" }
if (Test-Path "HoldSense/bin") { Remove-Item -Recurse -Force "HoldSense/bin" }
if (Test-Path "HoldSense/obj") { Remove-Item -Recurse -Force "HoldSense/obj" }

.\build.ps1 -Configuration $Configuration -Runtime $Runtime -Version $Version -Minimal:$Minimal
