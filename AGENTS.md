# HoldSense - AI Agent Documentation

This document provides essential information for AI coding agents working on the HoldSense project.

## Project Overview

HoldSense is a **Windows desktop utility** that creates a seamless "pick up and listen" Bluetooth audio experience. It uses computer vision to detect when you pick up your phone and automatically routes your phone's audio through your PC's headphones.

### Key Capabilities
- **Automatic Phone Detection**: Uses YOLOv26 model via webcam to detect phone presence
- **Bluetooth Audio Control**: Connects/disconnects phone's A2DP audio to PC headphones
- **Global Hotkeys**: Ctrl+Alt+C toggles audio, Ctrl+Alt+W toggles detection
- **System Tray Integration**: Runs quietly in background with tray icon menu

## Architecture

### Hybrid Application Structure

HoldSense is a **hybrid C# + Python application**:

```
┌─────────────────────────────────────────────────────────────┐
│  FRONTEND (C# WinUI 3)                                      │
│  - HoldSense/HoldSense.csproj                               │
│  - Main executable: HoldSense.exe                           │
│  - System tray, Settings UI, Device selection               │
│  - Launches and manages Python backend                      │
└──────────────────────────────┬──────────────────────────────┘
                               │ stdin/stdout protocol
                               ▼
┌─────────────────────────────────────────────────────────────┐
│  BACKEND (Python 3.11+)                                     │
│  - main.py (1,199 lines)                                    │
│  - Webcam capture (OpenCV)                                  │
│  - Phone detection (YOLOv26 ONNX)                            │
│  - Bluetooth control (Windows SDK via winsdk)               │
│  - Global hotkey listener                                   │
└─────────────────────────────────────────────────────────────┘
```

### Communication Protocol

The C# frontend and Python backend communicate via **stdin/stdout**:

**C# → Python Commands:**
- `toggle_detection` - Toggle webcam detection on/off
- `toggle_audio` - Manually toggle audio connection
- `disconnect_audio` - Force disconnect
- `get_status` - Request current status
- `set_keybind_enabled:1` - Enable/disable hotkeys
- `set_auto_enabled:1` - Enable/disable auto-detection
- `set_webcam_index:0` - Change webcam device
- `exit` - Shutdown backend

**Python → C# Status Messages:**
- `STATUS:audio_active:true` - Audio connection state
- `STATUS:detection_enabled:true` - Detection state
- `STATUS:phone_detected:true` - Phone detection state
- `STATUS:auto_detection_available:true` - Capability flag
- `ERROR:...` - Error messages

## Technology Stack

### Frontend (C#)
- **Framework**: .NET 8 with WinUI 3 / Windows App SDK 1.5
- **UI Framework**: WinUI 3 (not Avalonia - files are present but excluded from build)
- **Target Platform**: Windows 10 version 2004 (build 19041) or newer
- **Runtime Identifiers**: win-x64, win-x86, win-arm64
- **Key Dependencies**:
  - `Microsoft.WindowsAppSDK` 1.5.*
  - `CommunityToolkit.Mvvm` 8.2.2 (MVVM pattern)

### Backend (Python)
- **Version**: Python 3.11+
- **Core Dependencies**:
  - `winsdk` - Windows Runtime APIs for Bluetooth
  - `pystray` + `Pillow` - System tray icon
- **Auto-Detection Dependencies** (optional, ~150MB):
  - `opencv-python-headless` - Camera capture
  - `onnxruntime-directml` - GPU-accelerated inference
  - `numpy` - Numerical operations

### ML Model
- **Model**: YOLOv26n (nano variant)
- **Format**: ONNX 416×416 + INT8 (~3–5 MB) or FP32 640×640 (~10 MB)
- **Execution**: ONNX Runtime with DirectML (GPU) or CPU fallback
- **Detection Target**: Cell phones (COCO class ID 67)
- **Confidence Threshold**: 0.45

## Project Structure

```
HoldSense/
├── HoldSense/                     # C# Frontend Project
│   ├── HoldSense.csproj          # Project file (WinUI 3)
│   ├── MainWindowWinUI.cs        # Main window (WinUI implementation)
│   ├── App.axaml.cs              # Avalonia files (EXCLUDED from build)
│   ├── Models/
│   │   ├── AppConfig.cs          # Configuration model
│   │   └── BluetoothDevice.cs    # Bluetooth device model
│   ├── Services/
│   │   ├── PythonProcessService.cs    # Manages Python backend
│   │   ├── BluetoothService.cs        # Bluetooth device enumeration
│   │   ├── ConfigService.cs           # Config file I/O
│   │   ├── TrayIconServiceWinUI.cs    # System tray (WinUI)
│   │   └── WebcamService.cs           # Webcam enumeration
│   └── assets/
│       └── HoldSense.ico         # Application icon
│
├── main.py                        # Python Backend (1,199 lines)
├── download_model.py              # YOLOv26 model downloader
├── yolo26n.onnx                   # ONNX model file (not in repo)
│
├── requirements.txt               # Full Python dependencies
├── requirements-base.txt          # Minimal dependencies (keybind only)
├── requirements-auto.txt          # Auto-detection dependencies
│
├── main.spec                      # PyInstaller spec (full build)
├── main-minimal.spec              # PyInstaller spec (keybind only)
│
├── build.ps1                      # Main build orchestrator
├── build-clean.ps1                # Clean build script
├── installer.iss                  # Inno Setup installer script
│
├── msix/                          # MSIX packaging
│   ├── AppxManifest.xml
│   └── build-msix.ps1
│
├── .github/workflows/
│   └── release.yml                # GitHub Actions CI/CD
│
└── bt_config.json                 # User configuration file
```

## Configuration

### bt_config.json
```json
{
  "phone_bt_address": "XX:XX:XX:XX:XX:XX",
  "detection_enabled": false,
  "keybind_enabled": true,
  "python_exe_path": "",
  "theme": "auto",
  "webcam_index": 0,
  "auto_detection_downloaded": false
}
```

## Build Process

### Prerequisites
- Windows 10/11
- .NET 8 SDK
- Python 3.11+
- Inno Setup (for EXE installer)
- Windows SDK (for MSIX)

### Build Commands

```powershell
# Development - Run C# app (launches Python automatically)
cd HoldSense
dotnet run

# Download ML model (required before build)
python download_model.py

# Full build (creates publish/HoldSense/)
.\build.ps1 -Version "1.0.0"

# Minimal build (keybind-only, no auto-detection)
.\build.ps1 -Version "1.0.0" -Minimal

# Create EXE installer (requires Inno Setup)
iscc installer.iss

# Create MSIX package
cd msix
.\build-msix.ps1 -Version "1.0.0.0"
```

### Build Outputs
- `publish/HoldSense/` - Complete application bundle
- `dist/HoldSenseBackend/` - Python backend executable
- `installer_output/HoldSense-Setup-v{version}.exe` - EXE installer
- `msix_output/HoldSense_{version}.msix` - MSIX package

### Automated Releases
Push a version tag to trigger GitHub Actions:
```bash
git tag v1.0.0
git push origin v1.0.0
```

## Code Organization

### Python Backend (main.py)
| Section | Lines | Purpose |
|---------|-------|---------|
| Imports & Constants | 1-58 | Dependencies, thresholds |
| Bluetooth Registry | 61-143 | Windows registry device enumeration |
| Config Management | 146-214 | JSON config I/O |
| Device Selection | 217-307 | Console-based device selector |
| Audio Control | 310-515 | `BluetoothAudioConnector` class using winsdk |
| System Tray | 518-688 | pystray icon and menu |
| Global Hotkeys | 689-763 | Windows hotkey registration (Ctrl+Alt+C/W) |
| C# Communication | 764-839 | Stdin command handler |
| Main Loop | 842-1199 | Detection loop, model inference |

### C# Frontend
| Component | Responsibility |
|-----------|---------------|
| `MainWindowWinUI` | Main application window |
| `PythonProcessService` | Launch, monitor, communicate with Python |
| `BluetoothService` | Enumerate paired Bluetooth devices |
| `ConfigService` | Load/save `bt_config.json` |
| `TrayIconServiceWinUI` | System tray integration |
| `WebcamService` | Enumerate available webcams |

## Key Implementation Details

### YOLOv26 ONNX Inference
```python
# Output format: (batch, 84, 8400)
# 84 = 4 (bbox) + 80 (COCO classes)
# 8400 = number of anchor boxes
output_tensor = np.squeeze(outputs[0]).T  # -> (8400, 84)
phone_confidence = row[4+67]  # Class 67 = cell phone
```

### Bluetooth Audio Control
Uses `winsdk.windows.media.audio.AudioPlaybackConnection`:
- No external executables needed
- Native Windows RT API wrapper
- Async operations on background thread

### Global Hotkeys
Uses Windows API (`RegisterHotKey`):
- `Ctrl+Alt+C` - Toggle audio connection
- `Ctrl+Alt+W` - Toggle webcam detection
- Runs in dedicated daemon thread

### Detection Logic
- **Trigger**: 2 consecutive frames with phone detected
- **Release**: 25 consecutive frames without phone (~5s grace)
- **Flicker prevention**: Requires 4 consecutive misses before counting toward disconnect
- **Adaptive frame skip**: 3 when idle (responsive), 6 when stable (phone+audio on)
- **Manual Override**: `Ctrl+Alt+C` forces connection state

## Development Guidelines

### Adding New Features
1. **Python Backend**: Add command handler in `handle_stdin_commands()`
2. **C# Frontend**: Add corresponding method in `PythonProcessService`
3. **Communication**: Document new protocol messages

### Modifying Detection
- Adjust `CONF_THRESHOLD` (default: 0.45) for sensitivity
- Modify `CONSECUTIVE_FRAMES_TRIGGER` (default: 2) for response time
- Model variant can be changed via `MODEL_VARIANT` constant

### Error Handling
- Python prints `ERROR:...` messages to stdout
- C# parses and displays errors via status UI
- Always use try/except in Python async operations

## Security Considerations

1. **Bluetooth Address**: Stored in plain text in `bt_config.json`
2. **Registry Access**: Reads `HKLM\SYSTEM\...\BTHPORT\Parameters\Devices`
3. **No Network**: Application has no network communication
4. **Unsigned Builds**: Releases are unsigned (SmartScreen warnings expected)

## Testing

### Manual Testing Checklist
- [ ] Device selection shows paired Bluetooth devices
- [ ] Webcam detection starts/stops correctly
- [ ] Phone detection triggers audio connection
- [ ] Global hotkeys work (Ctrl+Alt+C, Ctrl+Alt+W)
- [ ] System tray menu functions properly
- [ ] Settings persist across restarts
- [ ] Theme switching (light/dark/auto) works

### Test Commands
```python
# Test Python backend standalone
python main.py  # Interactive mode with console UI

# Test with C# (no-ui mode)
python main.py --no-ui  # Reads commands from stdin
```

## Troubleshooting

### Common Issues
| Issue | Solution |
|-------|----------|
| Python backend won't start | Check `python_exe_path` in config |
| Webcam not detected | Try different `webcam_index` |
| Model not found | Run `python download_model.py` |
| Bluetooth not connecting | Verify device is paired in Windows |
| Build fails | Use `build-clean.ps1` in clean venv |

### Debug Logging
- Python prints to stdout (captured by C# in production)
- Set console window visible in PyInstaller spec for debugging
- Check Windows Event Viewer for crashes

## Resources

- **YOLOv26**: https://github.com/ultralytics/ultralytics
- **ONNX Runtime**: https://onnxruntime.ai/
- **WinUI 3**: https://docs.microsoft.com/windows/apps/winui/
- **Windows App SDK**: https://docs.microsoft.com/windows/apps/windows-app-sdk/

---

*This documentation is for AI coding agents. For user documentation, see README.md*
