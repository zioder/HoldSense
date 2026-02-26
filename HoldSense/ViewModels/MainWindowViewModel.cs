using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using _HoldSense.Models;
using _HoldSense.Services;

namespace _HoldSense.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly IRuntimeService _runtimeService;
    private readonly SynchronizationContext? _uiContext;

    [ObservableProperty]
    private bool _isDetectionRunning;

    [ObservableProperty]
    private bool _detectionEnabled = true;

    [ObservableProperty]
    private bool _keybindEnabled = true;

    [ObservableProperty]
    private string _selectedDeviceName = "Not configured";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isAudioConnected;

    [ObservableProperty]
    private bool _autoDetectionAvailable = true;

    public event EventHandler? SettingsRequested;

    public MainWindowViewModel(ConfigService configService, IRuntimeService runtimeService)
    {
        _configService = configService;
        _runtimeService = runtimeService;
        _uiContext = SynchronizationContext.Current;

        _runtimeService.StatusChanged += OnRuntimeStatusChanged;
        _runtimeService.RuntimeError += OnRuntimeError;
    }

    public async Task InitializeAsync()
    {
        await LoadConfigAsync();

        if (_runtimeService.IsRunning)
        {
            var config = await _configService.LoadConfigAsync();
            await _runtimeService.SetAutoEnabledAsync(DetectionEnabled);
            await _runtimeService.SetKeybindEnabledAsync(KeybindEnabled);
            if (config != null)
            {
                await _runtimeService.SetWebcamIndexAsync(config.WebcamIndex);
            }

            var status = await _runtimeService.GetStatusAsync();
            ApplyStatus(status);
        }
    }

    private async Task LoadConfigAsync()
    {
        try
        {
            var config = await _configService.LoadConfigAsync();
            if (config == null)
            {
                return;
            }

            DetectionEnabled = config.DetectionEnabled;
            KeybindEnabled = config.KeybindEnabled;
            if (!string.IsNullOrEmpty(config.PhoneBtAddress))
            {
                SelectedDeviceName = config.PhoneBtAddress;
            }
        }
        catch
        {
        }
    }

    [RelayCommand]
    private async Task StartDetectionAsync()
    {
        if (IsDetectionRunning)
        {
            return;
        }

        var config = await _configService.LoadConfigAsync();
        if (config == null || string.IsNullOrWhiteSpace(config.PhoneBtAddress))
        {
            StatusMessage = "No Bluetooth device configured. Open Settings and choose a device first.";
            return;
        }

        try
        {
            StatusMessage = "Starting listener...";
            await _runtimeService.StartAsync();
            await _runtimeService.SetKeybindEnabledAsync(KeybindEnabled);
            await _runtimeService.SetWebcamIndexAsync(config.WebcamIndex);

            // Respect persisted preference instead of forcing keybind-only mode every start.
            await _runtimeService.SetAutoEnabledAsync(config.DetectionEnabled);
            IsDetectionRunning = _runtimeService.IsRunning;

            var status = await _runtimeService.GetStatusAsync();
            ApplyStatus(status);

            var configToSave = config;
            configToSave.KeybindEnabled = KeybindEnabled;
            await _configService.SaveConfigAsync(configToSave);

            StatusMessage = status.DetectionEnabled
                ? "Listening with auto-detection enabled."
                : (status.AutoDetectionAvailable
                    ? "Listening in keybind mode (Ctrl+Alt+C)."
                    : "Auto-detection model is not installed. Open Settings and download Auto-Detection.");
        }
        catch (Exception ex)
        {
            IsDetectionRunning = false;
            StatusMessage = $"Failed to start listener: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StopDetectionAsync()
    {
        if (!IsDetectionRunning)
        {
            return;
        }

        StatusMessage = "Stopping listener...";
        await _runtimeService.StopAsync();
        IsDetectionRunning = false;
        IsAudioConnected = false;
        StatusMessage = "Listener stopped";
    }

    [RelayCommand]
    private async Task ToggleAutoDetectionAsync()
    {
        if (!_runtimeService.IsRunning || !IsDetectionRunning)
        {
            StatusMessage = "Start listening first.";
            return;
        }

        if (!AutoDetectionAvailable)
        {
            DetectionEnabled = false;
            StatusMessage = "Auto-detection is not installed.";
            return;
        }

        DetectionEnabled = !DetectionEnabled;
        await _runtimeService.SetAutoEnabledAsync(DetectionEnabled);

        var configToSave = await _configService.LoadConfigAsync() ?? new AppConfig();
        configToSave.DetectionEnabled = DetectionEnabled;
        await _configService.SaveConfigAsync(configToSave);

        StatusMessage = DetectionEnabled
            ? "Auto-detection enabled."
            : "Auto-detection paused. Keybind mode is still active.";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRuntimeStatusChanged(object? sender, RuntimeStatus status)
    {
        RunOnUiThread(() => ApplyStatus(status));
    }

    private void ApplyStatus(RuntimeStatus status)
    {
        AutoDetectionAvailable = status.AutoDetectionAvailable;
        DetectionEnabled = status.DetectionEnabled;
        IsAudioConnected = status.AudioActive;
        IsDetectionRunning = _runtimeService.IsRunning;
    }

    private void OnRuntimeError(object? sender, string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return;
        }

        RunOnUiThread(() => StatusMessage = error);
    }

    private void RunOnUiThread(Action action)
    {
        if (_uiContext == null || SynchronizationContext.Current == _uiContext)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }
}
