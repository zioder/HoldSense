# HoldSense
[!["Buy Me A Coffee"](https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png)](https://www.buymeacoffee.com/zioder)


HoldSense is a smart Windows utility that uses your webcam to monitor your phone's presence, delivering a seamless "pick up and listen" Bluetooth audio experience.

It automatically connects and disconnects your phone's Bluetooth audio to the headphones/earbuds connected to your PC . When HoldSense detects your phone nearby, it instantly switches your audio to the headphones and mixes both your computer and phone audio together. When you move the phone away, the connection drops, and you hear only your PC audio—perfect for transitioning between work and a call.



The application runs quietly in the system tray, offering a modern GUI for configuration and manual control via global hotkeys.

## Key Features

- **Automatic Audio Switching**: Uses a YOLOv8 model to detect your phone via webcam and automatically manages your Bluetooth A2DP audio connection.
- **Unified Application**: A C# Avalonia frontend provides a clean user interface, while a Python backend handles detection and audio control.
- **System Tray Integration**: Hides neatly in the system tray with a menu for quick actions, ensuring it stays out of your way.
- **Global Hotkeys**:
    - `Ctrl+Alt+W`: Toggle the webcam detection on or off to save resources.
    - `Ctrl+Alt+C`: Manually connect or disconnect the Bluetooth audio link.
- **Settings UI**: An intuitive settings panel to change your Bluetooth device, select a webcam, enable/disable modes, and customize the theme.
- **Efficient & Modern**:
    - Detection is powered by an ONNX-exported YOLOv8 model, running on the GPU via `onnxruntime-directml` for high performance.
    - Audio control is handled natively using the Windows SDK (`winsdk`), eliminating the need for external executables.

## How It Works

HoldSense is a hybrid application combining a C# GUI with a Python backend for core functionality.

1.  **GUI (C# Avalonia)**: The main executable you run. It provides the setup wizard, main window, settings panel, and the unified system tray icon.
2.  **Backend (Python)**: The C# application launches the `main.py` script in the background. This script is responsible for:
    - Accessing the webcam feed via OpenCV.
    - Running the YOLOv8 phone detection model.
    - Managing the Bluetooth A2DP connection using the Windows SDK.
    - Listening for global hotkeys.
3.  **Communication**: The C# GUI and Python backend communicate through the standard input/output streams. The GUI sends commands (e.g., `toggle_detection`) to Python's `stdin`, and Python reports its status (e.g., `STATUS:audio_active:true`) back to the GUI via `stdout`.

This architecture allows for a responsive and modern user interface while leveraging the powerful libraries available in the Python ecosystem for machine learning and hardware interaction.

## Installation

### Option 1: Download Pre-built Release (Recommended)

The easiest way to get HoldSense is to download a pre-built release. No Python or .NET installation required!

1. **Go to [Releases](https://github.com/zioder/HoldSense/releases)**
2. **Download one of these packages:**
   - **`HoldSense-Setup-vX.X.X.exe`** - Full installer (recommended)
   - **`HoldSense_X_X_X_X.msix`** - Modern Windows package
   - **`HoldSense-Portable-vX.X.X.zip`** - Portable version (no installation)

3. **Install and run:**
   - **EXE**: Double-click to install. If Windows shows a security warning, click "More info" → "Run anyway"
   - **MSIX**: Double-click to install. Enable Developer Mode if prompted
   - **Portable**: Extract the ZIP and run `HoldSense.exe`

**System Requirements:**
- Windows 10 version 2004 (build 19041) or newer
- Bluetooth-enabled PC with paired audio device
- Webcam for automatic phone detection

### Option 2: Build from Source (Development)

If you want to build from source or contribute to development:

#### Prerequisites

- **Windows 10** (version 2004 or newer)
- **.NET 8 SDK**
- **Python 3.11+**
- A Bluetooth-enabled PC and a phone/audio device already paired with Windows

#### Steps

1.  **Clone the Repository**
    ```bash
    git clone https://github.com/zioder/HoldSense.git
    cd HoldSense
    ```

2.  **Install Python Dependencies**
    A virtual environment is recommended.
    ```bash
    # Create and activate virtual environment (optional but recommended)
    python -m venv .venv
    .venv\Scripts\activate

    # Install the required packages
    pip install -r requirements.txt
    ```
    The script uses `onnxruntime-directml` for GPU acceleration. If you encounter issues, you can switch to the CPU version by running: `pip install onnxruntime`.

3.  **Run the Application**
    The application can be run directly via the .NET CLI.
    ```bash
    # Navigate to the C# project directory
    cd HoldSense

    # Run the application
    dotnet run
    ```

4.  **Build Distributable Package (Optional)**
    To create your own installer:
    ```powershell
    # Install PyInstaller
    pip install pyinstaller

    # Build everything (at repository root)
    .\build.ps1 -Version "1.0.0"
    ```
    
    See `QUICKSTART.md` for detailed build and release instructions.

## Usage Guide

### First-Time Configuration
On the first launch, HoldSense will present a device selector. Choose your phone or Bluetooth audio device from the list of paired A2DP devices. This selection is saved in `bt_config.json` for future sessions.

### Main Window
The main window provides a simple interface to start and stop the detection service and view the current status.
- **Start/Stop Detection**: Manages the Python background process.
- **Settings Button**: Opens the detailed settings window.
- **Status Panel**: Shows the configured device and whether detection is running.
- Closing the window minimizes it to the system tray; it does not exit the application.

### System Tray
HoldSense lives in your system tray. A **right-click** on the icon opens a context menu with essential actions:
- **Status Display**: Shows the current connection status.
- **Connect/Disconnect Audio**: Manually toggles the audio connection.
- **Auto Detection**: Enables or disables the webcam detection feature.
- **Open Settings**: Opens the full settings window.
- **Exit**: Shuts down the application completely.

A **left-click** on the icon will show the main window.

### Settings Window
The settings window provides comprehensive control over the application's behavior:
- **Bluetooth Device**: View the current device, change to a different one, or manually disconnect the audio.
- **Preferences**:
    - **Auto mode (webcam)**: Enable or disable automatic detection. When disabled, the camera is released to save resources.
    - **Keybind enabled**: Enable or disable the manual `Ctrl+Alt+C` hotkey.
    - **Theme**: Choose between Auto (follows system), Light, and Dark modes.
    - **Webcam**: Select which camera to use for detection if you have multiple devices.

## Configuration File

The application stores your preferences in a `bt_config.json` file in the same directory as the executable.

```json
{
  "phone_bt_address": "XX:XX:XX:XX:XX:XX",
  "detection_enabled": false,
  "keybind_enabled": true,
  "python_exe_path": "",
  "theme": "auto",
  "webcam_index": 0
}
```

## Building and Releasing

### For Developers

If you're contributing to HoldSense or want to create your own builds:

- **Quick Start**: See [`QUICKSTART.md`](QUICKSTART.md) for a fast guide to creating releases
- **Detailed Guide**: See [`RELEASE_GUIDE.md`](RELEASE_GUIDE.md) for comprehensive build and release documentation
- **Technical Details**: See [`BUILD_AND_RELEASE_SUMMARY.md`](BUILD_AND_RELEASE_SUMMARY.md) for architecture overview

### Creating a Release

The easiest way to create a new release is via GitHub Actions:

```bash
# Commit your changes
git add .
git commit -m "Release v1.0.0"
git push origin main

# Create and push a version tag
git tag v1.0.0
git push origin v1.0.0
```

GitHub Actions will automatically build:
- 📦 Windows EXE installer
- 📦 MSIX package  
- 📦 Portable ZIP version

All with Python and dependencies fully bundled - no external installations required!

## Contributing

Contributions are welcome! Whether it's bug fixes, new features, or documentation improvements:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [`LICENSE`](LICENSE) file for details.

## Support

- 🐛 **Bug Reports**: [Open an issue](https://github.com/zioder/HoldSense/issues)
- 💡 **Feature Requests**: [Start a discussion](https://github.com/zioder/HoldSense/discussions)
- ☕ **Support Development**: [Buy me a coffee](https://www.buymeacoffee.com/zioder)

## Acknowledgments

- YOLOv8 for phone detection
- Avalonia UI for the cross-platform UI framework
- The Python and .NET communities for excellent tools and libraries
