# HoldSense - Unified Application Guide

## Overview
The application has been unified - the Python webcam detection service now integrates seamlessly with the Avalonia C# GUI app. The app runs in the background with a system tray icon, providing a clean consumer experience.

## Key Features

### 🎯 New Features
1. **System Tray Integration**: App runs in the background with a tray icon
2. **Hide to Tray**: Closing the window hides it to tray instead of exiting
3. **Dual Keybinds**:
   - `Ctrl+Alt+W`: Toggle webcam detection on/off
   - `Ctrl+Alt+C`: Manually toggle Bluetooth audio connection
4. **Device Management**: Change devices or disconnect audio from Settings
5. **Background Webcam**: Camera runs without showing video feed
6. **Default Mode**: Keybind mode ON, Auto (camera) mode OFF by default to minimize CPU/GPU/RAM usage

## How It Works

### Running the App
There are two modes:

#### 1. Standalone Mode (Python Only)
Run `python main.py` directly - shows Bluetooth device selector, then runs with its own system tray icon.

#### 2. Unified Mode (Recommended)
Run the Avalonia app (`HoldSense.exe`) - it automatically:
- Shows the main GUI window
- Starts Python detection service in background
- Uses a single system tray icon
- Provides full control through the GUI

### System Tray Behavior
- **Right-click** the tray icon for menu
- **Click** the tray icon to show the main window
- **Close** the main window → hides to tray (doesn't exit)
- **Exit** from tray menu → fully closes the app

## Updated Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+Alt+W` | Disable/enable webcam capture and detection (camera released) |
| `Ctrl+Alt+C` | Manually toggle Bluetooth audio |

## Settings Window

### Bluetooth Device Section
- **Current Device**: Shows connected device name and MAC address
- **Change Device**: Opens device selector to choose a different phone
- **Disconnect Audio**: Immediately disconnect Bluetooth audio

### Preferences Section
  - **Auto mode (webcam)**: Enable/disable automatic detection
  - Can also toggle with `Ctrl+Alt+W` (releases camera entirely when off)
- **Keybind enabled**: Allow manual audio toggle with `Ctrl+Alt+C`
  
## Mode Interaction (Four Cases)

- Auto OFF + Keybind OFF: do nothing (no camera, no keybind actions).
- Auto OFF + Keybind ON: do not open the camera or run detection; only listen to `Ctrl+Alt+C`.
- Auto ON + Keybind OFF: rely only on automatic detection (camera open, model running).
- Auto ON + Keybind ON: both modes active with priority to keybind:
  - If turned ON via keybind, audio stays ON even if no phone is detected.
  - If turned OFF via keybind and a phone is detected, auto will turn audio ON.

## Architecture

### Communication Flow
```
Avalonia App (C#)
    ↓ Starts with --no-ui flag
Python Script
    ↓ Reads commands from stdin
    ↓ Sends status to stdout
Avalonia App receives updates
```

### Commands Supported
The C# app can send these commands to Python via stdin:
- `toggle_detection` - Toggle webcam detection
- `toggle_audio` - Toggle audio connection
- `disconnect_audio` - Disconnect audio immediately
- `get_status` - Request current status
- `exit` - Gracefully exit Python process

### Python Output Format
Status updates from Python:
```
STATUS:detection_enabled:true
STATUS:phone_detected:false
STATUS:audio_active:true
```

## Files Modified

### Python Files
  - **main.py**: 
  - Added `Ctrl+Alt+W` hotkey for detection toggle
  - Added stdin command handler for C# communication
  - System tray only shows in standalone mode
  - No webcam display (runs in background)
  - Detection can be toggled on/off
  
- **requirements.txt**: 
  - Added `pystray` and `Pillow` for system tray support

### C# Files
- **TrayIconService.cs** (NEW): 
  - Manages system tray icon for Avalonia app
  - Click to show window, exit from menu

- **App.axaml.cs**: 
  - Initializes system tray on startup
  
- **MainWindow.axaml.cs**: 
  - Intercepts close event to hide instead of close
  - Passes PythonProcessService to Settings

- **PythonProcessService.cs**: 
  - Added command sending methods:
    - `ToggleDetectionAsync()`
    - `ToggleAudioAsync()`
    - `DisconnectAudioAsync()`
    - `GetStatusAsync()`

- **SettingsViewModel.cs**: 
  - Added `DisconnectAudioCommand`
  - Takes optional PythonProcessService parameter

- **SettingsWindow.axaml**: 
  - Added Bluetooth Device section
  - Shows device name and MAC
  - Disconnect Audio button
  - Updated keybind descriptions

## Installation & Setup

### First Time Setup
1. **Install Python dependencies**:
   ```bash
   pip install -r requirements.txt
   ```

2. **Build the Avalonia app**:
   ```bash
   cd HoldSense
   dotnet build
   ```

3. **Run the unified app**:
   ```bash
   dotnet run
   ```

### Configuration
On first run:
1. App will show device selector
2. Choose your phone's Bluetooth device
3. Device is saved to `bt_config.json`
4. Main window appears, Python service starts automatically

## Usage Tips

### Consumer-Friendly Features
- ✅ No visible webcam window
- ✅ Runs silently in background
- ✅ Single system tray icon
- ✅ Clean, modern UI
- ✅ Easy device management
- ✅ Multiple control methods (auto, hotkeys, GUI)

### Troubleshooting
- **Python service won't start**: Check that `main.py` is in the parent directory of the exe
- **No Bluetooth devices**: Ensure device is paired in Windows Settings first
- **Hotkeys not working**: Run as administrator if needed
- **Webcam not detected**: Close other apps using the camera

## Development Notes

### Debug Mode
To run Python script in debug mode (with its own tray):
```bash
python main.py
```

### Viewing Python Output
The C# app shows Python output in the log panel of the main window.

### Testing Commands
You can manually send commands via the PythonProcessService:
```csharp
await pythonService.ToggleDetectionAsync();
await pythonService.DisconnectAudioAsync();
```

## Future Enhancements
Potential improvements:
- [ ] Add detection sensitivity slider
- [ ] Show live status in tray tooltip
- [ ] Add notification toasts for state changes
- [ ] Support multiple device profiles
- [ ] Add statistics/usage tracking

---

## Summary of Changes

### What Changed
✅ Added `Ctrl+Alt+W` keybind to toggle detection  
✅ Unified Python and C# apps with stdin/stdout communication  
✅ System tray for background operation  
✅ Hide to tray instead of close  
✅ Disconnect audio button in Settings  
✅ Device management from GUI  
✅ Webcam runs without display  

### What Stayed the Same
- YOLO phone detection algorithm
- Bluetooth audio control mechanism
- Configuration file structure
- Device selector functionality

Enjoy your unified, consumer-friendly HoldSense app! 🎧

