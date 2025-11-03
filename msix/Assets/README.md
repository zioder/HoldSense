# MSIX Assets

This folder should contain the required image assets for the MSIX package in PNG format.

## Required Assets

Create the following PNG images from your app icon:

- **Square44x44Logo.png** - 44x44 pixels (App list icon)
- **Square150x150Logo.png** - 150x150 pixels (Start menu tile)
- **Wide310x150Logo.png** - 310x150 pixels (Wide start menu tile)
- **StoreLogo.png** - 50x50 pixels (Microsoft Store logo)
- **SplashScreen.png** - 620x300 pixels (Splash screen)

## Creating Assets

You can use online tools or PowerShell to convert your ICO file to PNG at different sizes.

### Using PowerShell with .NET:
```powershell
# This requires System.Drawing, which may not be available on all systems
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon("path\to\HoldSense.ico")
$bitmap = $icon.ToBitmap()
$bitmap.Save("Square150x150Logo.png", [System.Drawing.Imaging.ImageFormat]::Png)
```

### Using Online Tools:
- Convert ICO to PNG: https://convertio.co/ico-png/
- Resize images: https://www.iloveimg.com/resize-image

## Quick Setup

If you don't create these assets, the build script will attempt to use the .ico file as a placeholder, but proper PNG assets are recommended for a professional appearance.

