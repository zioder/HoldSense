using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using _HoldSense.Models;
using _HoldSense.Services;
using System.Collections.ObjectModel;

namespace _HoldSense.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly BluetoothService _bluetoothService;
    private readonly WebcamService _webcamService;
    private readonly IRuntimeService? _runtimeService;
    private readonly AutoDetectionDownloadService _downloadService;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty]
    private string _selectedDeviceMac = "Not configured";

    [ObservableProperty]
    private string _selectedDeviceName = "No device selected";

    [ObservableProperty]
    private bool _detectionEnabled = false;

    [ObservableProperty]
    private bool _keybindEnabled = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _selectedTheme = "Auto";

    [ObservableProperty]
    private WebcamDevice? _selectedWebcam;

    [ObservableProperty]
    private BluetoothDevice? _selectedBluetoothDevice;

    [ObservableProperty]
    private bool _isLoadingBluetoothDevices;

    // Auto-detection download properties
    [ObservableProperty]
    private bool _autoDetectionDownloaded = false;

    [ObservableProperty]
    private bool _isDownloading = false;

    [ObservableProperty]
    private double _downloadProgress = 0;

    [ObservableProperty]
    private string _downloadStatusText = string.Empty;

    [ObservableProperty]
    private string _autoDetectionStatusText = "Not Installed";

    [ObservableProperty]
    private string _autoDetectionStatusColor = "#808080";

    [ObservableProperty]
    private string _autoDetectionSizeText = string.Empty;

    public ObservableCollection<string> ThemeOptions { get; } = new(new[] { "Auto", "Light", "Dark" });

    public ObservableCollection<WebcamDevice> AvailableWebcams { get; } = new();
    public ObservableCollection<BluetoothDevice> AvailableBluetoothDevices { get; } = new();

    public event EventHandler? ChangeDeviceRequested;
    public event EventHandler? CloseRequested;

    public SettingsViewModel(ConfigService configService, BluetoothService bluetoothService, WebcamService webcamService, IRuntimeService? runtimeService = null)
    {
        _configService = configService;
        _bluetoothService = bluetoothService;
        _webcamService = webcamService;
        _runtimeService = runtimeService;
        _downloadService = new AutoDetectionDownloadService(configService);

        // Subscribe to download events
        _downloadService.ProgressChanged += OnDownloadProgressChanged;
        _downloadService.StatusChanged += OnDownloadStatusChanged;
    }

    private void OnDownloadProgressChanged(object? sender, DownloadProgressEventArgs e)
    {
        DownloadProgress = e.ProgressPercent;
    }

    private void OnDownloadStatusChanged(object? sender, string status)
    {
        DownloadStatusText = status;
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
                AutoDetectionDownloaded = config.AutoDetectionDownloaded;
                SelectedTheme = string.IsNullOrWhiteSpace(config.Theme)
                    ? "Auto"
                    : (config.Theme.Equals("light", StringComparison.OrdinalIgnoreCase)
                        ? "Light"
                        : (config.Theme.Equals("dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Auto"));

                await RefreshBluetoothDevicesAsync();

                if (!string.IsNullOrWhiteSpace(config.PhoneBtAddress))
                {
                    var device = AvailableBluetoothDevices.FirstOrDefault(d =>
                        d.MacAddress == config.PhoneBtAddress || d.DeviceId == config.PhoneBtAddress);
                    if (device != null)
                    {
                        SelectedBluetoothDevice = device;
                    }
                    else
                    {
                        SelectedBluetoothDevice = null;
                        SelectedDeviceName = "Unknown Device";
                        SelectedDeviceMac = config.PhoneBtAddress;
                    }
                }
                else
                {
                    SelectedBluetoothDevice = null;
                    SelectedDeviceName = "No device selected";
                    SelectedDeviceMac = "Not configured";
                }

                // Load webcam settings
                await LoadWebcamsAsync();
                var webcamIndex = config.WebcamIndex;
                SelectedWebcam = AvailableWebcams.FirstOrDefault(w => w.Index == webcamIndex) 
                    ?? AvailableWebcams.FirstOrDefault();
            }

            // Check actual auto-detection status (model file exists)
            UpdateAutoDetectionStatus();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshBluetoothDevicesAsync()
    {
        try
        {
            IsLoadingBluetoothDevices = true;

            var devices = await _bluetoothService.EnumerateA2dpDevicesAsync();
            AvailableBluetoothDevices.Clear();
            foreach (var device in devices)
            {
                AvailableBluetoothDevices.Add(device);
            }

            if (SelectedBluetoothDevice != null)
            {
                var selected = AvailableBluetoothDevices.FirstOrDefault(d =>
                    d.MacAddress == SelectedBluetoothDevice.MacAddress || d.DeviceId == SelectedBluetoothDevice.DeviceId);
                if (selected != null)
                {
                    SelectedBluetoothDevice = selected;
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading Bluetooth devices: {ex.Message}";
        }
        finally
        {
            IsLoadingBluetoothDevices = false;
        }
    }

    private void UpdateAutoDetectionStatus()
    {
        var isReady = _downloadService.IsAutoDetectionReady();
        AutoDetectionDownloaded = isReady;

        if (isReady)
        {
            AutoDetectionStatusText = "Installed";
            AutoDetectionStatusColor = "#4CAF50";
            var sizeMB = _downloadService.GetAutoDetectionSizeMB();
            AutoDetectionSizeText = $"Downloaded: {sizeMB:F1} MB";
        }
        else
        {
            AutoDetectionStatusText = "Not Installed";
            AutoDetectionStatusColor = "#808080";
            AutoDetectionSizeText = string.Empty;
            DetectionEnabled = false; // Can't enable detection without model
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
    private async Task DownloadAutoDetectionAsync()
    {
        if (IsDownloading)
            return;

        try
        {
            IsDownloading = true;
            DownloadProgress = 0;
            DownloadStatusText = "Starting download...";
            
            _downloadCts = new CancellationTokenSource();
            var success = await _downloadService.DownloadAutoDetectionAsync(_downloadCts.Token);

            if (success)
            {
                StatusMessage = "Auto-detection downloaded successfully! Please restart the app.";
                UpdateAutoDetectionStatus();
            }
            else
            {
                StatusMessage = string.IsNullOrWhiteSpace(DownloadStatusText)
                    ? "Download failed. Please try again."
                    : DownloadStatusText;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download error: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    [RelayCommand]
    private async Task RemoveAutoDetectionAsync()
    {
        try
        {
            var success = await _downloadService.RemoveAutoDetectionAsync();
            if (success)
            {
                StatusMessage = "Auto-detection components removed.";
                UpdateAutoDetectionStatus();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error removing: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            var config = await _configService.LoadConfigAsync() ?? new AppConfig();
            
            // Only allow enabling detection if auto-detection is downloaded
            if (DetectionEnabled && !AutoDetectionDownloaded)
            {
                StatusMessage = "Please download auto-detection components first.";
                DetectionEnabled = false;
                await Task.Delay(2000);
                StatusMessage = string.Empty;
                return;
            }

            config.DetectionEnabled = DetectionEnabled;
            config.KeybindEnabled = KeybindEnabled;
            config.PhoneBtAddress = SelectedBluetoothDevice?.MacAddress ?? string.Empty;
            config.Theme = (SelectedTheme ?? "Auto").Trim().ToLowerInvariant();
            config.WebcamIndex = SelectedWebcam?.Index ?? 0;
            config.AutoDetectionDownloaded = AutoDetectionDownloaded;
            await _configService.SaveConfigAsync(config);

            // Propagate settings to runtime if running
            if (_runtimeService != null && _runtimeService.IsRunning)
            {
                await _runtimeService.SetAutoEnabledAsync(DetectionEnabled);
                await _runtimeService.SetKeybindEnabledAsync(KeybindEnabled);
                await _runtimeService.SetWebcamIndexAsync(config.WebcamIndex);
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
            if (_runtimeService != null && _runtimeService.IsRunning)
            {
                await _runtimeService.DisconnectAudioAsync();
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

    partial void OnSelectedBluetoothDeviceChanged(BluetoothDevice? value)
    {
        if (value != null)
        {
            SelectedDeviceName = value.Name;
            SelectedDeviceMac = value.MacAddress;
            if (string.IsNullOrEmpty(StatusMessage) || StatusMessage.StartsWith("Error loading Bluetooth devices:", StringComparison.Ordinal))
            {
                StatusMessage = "Device selected. Click Save to apply.";
            }
        }
    }
}
