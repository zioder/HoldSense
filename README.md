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

## Installation and Setup

### Prerequisites

- **Windows 10** (version 2004 or newer)
- **.NET 8 SDK** (or a compatible runtime)
- **Python 3.x**
- A Bluetooth-enabled PC and a phone/audio device already paired with Windows.

### Steps

1.  **Clone the Repository**
    ```bash
    git clone https://github.com/zioder/HoldSense.git
    cd HoldSense
    ```

2.  **Install Python Dependencies**
    A virtual environment is recommended.
    ```bash
    # Install the required packages
    pip install -r requirements.txt
    ```
    The script uses `onnxruntime-directml` for GPU acceleration. If you encounter issues, you can switch to the CPU version by running: `pip install onnxruntime`.

3.  **Build and Run the Application**
    The application can be run directly via the .NET CLI or by building the executable.
    ```bash
    # Navigate to the C# project directory
    cd HoldSense

    # Run the application
    dotnet run
    ```
    Alternatively, build the project (`dotnet build`) and run the executable from the `bin/` directory.

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