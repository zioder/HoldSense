using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using _HoldSense.Models;
using _HoldSense.Services;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;

namespace _HoldSense.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly BluetoothService _bluetoothService;
    private readonly WebcamService _webcamService;
    private readonly PythonProcessService? _pythonService;

    [ObservableProperty]
    private string _selectedDeviceMac = "Not configured";

    [ObservableProperty]
    private string _selectedDeviceName = "No device selected";

    [ObservableProperty]
    private bool _detectionEnabled = true;

    [ObservableProperty]
    private bool _keybindEnabled = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _selectedTheme = "Auto";

    [ObservableProperty]
    private WebcamDevice? _selectedWebcam;

    public ObservableCollection<string> ThemeOptions { get; } = new(new[] { "Auto", "Light", "Dark" });

    public ObservableCollection<WebcamDevice> AvailableWebcams { get; } = new();

    public event EventHandler? ChangeDeviceRequested;
    public event EventHandler? CloseRequested;

    public SettingsViewModel(ConfigService configService, BluetoothService bluetoothService, WebcamService webcamService, PythonProcessService? pythonService = null)
    {
        _configService = configService;
        _bluetoothService = bluetoothService;
        _webcamService = webcamService;
        _pythonService = pythonService;
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            var config = await _configService.LoadConfigAsync();
            if (config != null)
            {
                SelectedDeviceMac = config.PhoneBtAddress;
                DetectionEnabled = config.DetectionEnabled;
                KeybindEnabled = config.KeybindEnabled;
                SelectedTheme = string.IsNullOrWhiteSpace(config.Theme)
                    ? "Auto"
                    : (config.Theme.Equals("light", StringComparison.OrdinalIgnoreCase)
                        ? "Light"
                        : (config.Theme.Equals("dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Auto"));

                // Try to get device name
                var devices = await _bluetoothService.EnumerateA2dpDevicesAsync();
                var device = devices.FirstOrDefault(d => d.MacAddress == config.PhoneBtAddress);
                SelectedDeviceName = device?.Name ?? "Unknown Device";

                // Load webcam settings
                await LoadWebcamsAsync();
                var webcamIndex = config.WebcamIndex;
                SelectedWebcam = AvailableWebcams.FirstOrDefault(w => w.Index == webcamIndex) 
                    ?? AvailableWebcams.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading settings: {ex.Message}";
        }
    }

    private async Task LoadWebcamsAsync()
    {
        try
        {
            var webcams = await _webcamService.EnumerateWebcamsAsync();
            AvailableWebcams.Clear();
            foreach (var webcam in webcams)
            {
                AvailableWebcams.Add(webcam);
            }
        }
        catch (Exception)
        {
            // If enumeration fails, add at least camera 0 as fallback
            if (AvailableWebcams.Count == 0)
            {
                AvailableWebcams.Add(new WebcamDevice { Name = "Camera 0", Index = 0, Id = "0" });
            }
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            var config = await _configService.LoadConfigAsync() ?? new AppConfig();
            config.DetectionEnabled = DetectionEnabled;
            config.KeybindEnabled = KeybindEnabled;
            config.Theme = (SelectedTheme ?? "Auto").Trim().ToLowerInvariant();
            config.WebcamIndex = SelectedWebcam?.Index ?? 0;
            await _configService.SaveConfigAsync(config);

            // Propagate settings to Python runtime if running
            if (_pythonService != null && _pythonService.IsRunning)
            {
                await _pythonService.SetAutoEnabledAsync(DetectionEnabled);
                await _pythonService.SetKeybindEnabledAsync(KeybindEnabled);
                await _pythonService.SetWebcamIndexAsync(config.WebcamIndex);
            }

            // Apply theme immediately
            if (Application.Current != null)
            {
                var theme = config.Theme;
                Application.Current.RequestedThemeVariant = theme switch
                {
                    "light" => ThemeVariant.Light,
                    "dark" => ThemeVariant.Dark,
                    _ => ThemeVariant.Default
                };
            }

            StatusMessage = "Settings saved successfully!";
            await Task.Delay(2000);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ChangeDevice()
    {
        ChangeDeviceRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task DisconnectAudioAsync()
    {
        try
        {
            if (_pythonService != null && _pythonService.IsRunning)
            {
                await _pythonService.DisconnectAudioAsync();
                StatusMessage = "Disconnecting audio...";
                await Task.Delay(1500);
                StatusMessage = string.Empty;
            }
            else
            {
                StatusMessage = "Detection service is not running.";
                await Task.Delay(2000);
                StatusMessage = string.Empty;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error disconnecting: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

