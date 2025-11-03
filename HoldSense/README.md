# HoldSense - Avalonia UI

A modern Avalonia-based UI for the HoldSense phone detection and Bluetooth audio control application.

## Features

- **Device Selection**: Select your Bluetooth audio device on first launch
- **Settings Page**: Configure detection and keybind options
- **Fluent UI**: Modern Fluent theme with light/dark mode
- **Keybind Toggle**: Use Ctrl+Alt+C to manually toggle Bluetooth connection

## Requirements

- .NET 8.0 or later
- Windows 10 version 19041 or later
- Python 3.x (for the detection backend)
- Paired Bluetooth audio device

## Building

```bash
cd HoldSense
dotnet restore
dotnet build
```

## Running

```bash
dotnet run
```

Or build and run the executable from `bin\Debug\net8.0-windows10.0.19041.0\`

## Project Structure

```
HoldSense/
├── Models/              # Data models (BluetoothDevice, AppConfig)
├── Services/            # Business logic services
│   ├── BluetoothService.cs      # Bluetooth device enumeration
│   ├── ConfigService.cs         # Configuration management
│   └── PythonProcessService.cs  # Python backend process management
├── ViewModels/          # MVVM ViewModels
│   ├── MainWindowViewModel.cs
│   ├── DeviceSelectorViewModel.cs
│   └── SettingsViewModel.cs
├── Views/               # XAML views
│   ├── DeviceSelectorWindow.axaml
│   └── SettingsWindow.axaml
├── Converters/          # Value converters for data binding
├── MainWindow.axaml     # Main application window
└── App.axaml           # Application entry point
```

## Configuration

The app stores configuration in `bt_config.json` in the application directory:

```json
{
  "phone_bt_address": "XX:XX:XX:XX:XX:XX",
  "detection_enabled": true,
  "keybind_enabled": true
}
```

## Usage

1. **First Launch**: Select your Bluetooth audio device from the list
2. **Main Window**: 
   - Click "Start Detection" to begin phone detection
   - Use "Settings" to modify configuration
3. **Settings**:
   - Toggle phone detection on/off
   - Enable/disable keybind (Ctrl+Alt+C)
   - Theme: Auto (follow system), Light, or Dark
   - Change Bluetooth device

## Integration with Python Backend

The Avalonia UI launches and manages the Python detection script (`main.py`) located in the parent directory. Make sure:

1. Python is installed and in your PATH
2. All Python dependencies are installed (`pip install -r requirements.txt`)
3. The `main.py` file is in the parent directory of the executable

## Notes

- The Python backend handles the actual phone detection and Bluetooth audio connection
- The UI now defaults to Fluent theme and follows system theme by default (Auto)

## License

Same as the parent project.



