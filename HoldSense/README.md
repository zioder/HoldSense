# HoldSense - WinUI 3 App

HoldSense is a Windows 10/11 desktop utility built with WinUI 3 and .NET 8.

## Runtime Architecture

- In-process runtime (no Python subprocess).
- Webcam capture: `OpenCvSharp4`.
- Phone detection: ONNX + `Microsoft.ML.OnnxRuntime.DirectML` (GPU with CPU fallback).
- Bluetooth audio control: Windows `AudioPlaybackConnection`.
- Global hotkeys: `Ctrl+Alt+C` (audio toggle), `Ctrl+Alt+W` (detection toggle).

## Build

```bash
cd HoldSense
dotnet restore
dotnet build
```

## Run

```bash
dotnet run
```

## Config

`bt_config.json` (next to executable) stores runtime options:

```json
{
  "phone_bt_address": "XX:XX:XX:XX:XX:XX",
  "detection_enabled": false,
  "keybind_enabled": true,
  "python_exe_path": "",
  "backend_mode": "dotnet",
  "enable_python_fallback": false,
  "theme": "auto",
  "webcam_index": 0,
  "auto_detection_downloaded": false
}
```
