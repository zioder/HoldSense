using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using _HoldSense.Models;
using _HoldSense.Services;

namespace _HoldSense.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly PythonProcessService _pythonService;

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


    public event EventHandler? SettingsRequested;

    public MainWindowViewModel(ConfigService configService, PythonProcessService pythonService)
    {
        _configService = configService;
        _pythonService = pythonService;

        _pythonService.ProcessExited += OnPythonExited;
    }

    public async Task InitializeAsync()
    {
        await LoadConfigAsync();

        // Push settings to Python process if it's already running
        if (_pythonService.IsRunning)
        {
            var config = await _configService.LoadConfigAsync();
            await _pythonService.SetAutoEnabledAsync(DetectionEnabled);
            await _pythonService.SetKeybindEnabledAsync(KeybindEnabled);
            if (config != null)
            {
                await _pythonService.SetWebcamIndexAsync(config.WebcamIndex);
            }
        }
    }

    private async Task LoadConfigAsync()
    {
        try
        {
            var config = await _configService.LoadConfigAsync();
            if (config != null)
            {
                DetectionEnabled = config.DetectionEnabled;
                KeybindEnabled = config.KeybindEnabled;
                
                if (!string.IsNullOrEmpty(config.PhoneBtAddress))
                {
                    SelectedDeviceName = config.PhoneBtAddress;
                }
            }
        }
        catch (Exception)
        {
        }
    }

    [RelayCommand]
    private async Task StartDetectionAsync()
    {
        if (IsDetectionRunning)
            return;

        StatusMessage = "Starting detection...";
        await _pythonService.StartAsync();
        IsDetectionRunning = true;
        StatusMessage = "Detection running";
    }

    [RelayCommand]
    private async Task StopDetectionAsync()
    {
        if (!IsDetectionRunning)
            return;

        StatusMessage = "Stopping detection...";
        await _pythonService.StopAsync();
        IsDetectionRunning = false;
        StatusMessage = "Detection stopped";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPythonExited(object? sender, EventArgs e)
    {
        IsDetectionRunning = false;
        StatusMessage = "Detection stopped unexpectedly";
    }
}



