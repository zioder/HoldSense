using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using System;
using System.Linq;
using System.Threading.Tasks;
using _HoldSense.Views;
using _HoldSense.ViewModels;

namespace _HoldSense.Services;

public class TrayIconService
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private TrayIcon? _trayIcon;
    private readonly PythonProcessService? _pythonService;
    private readonly ConfigService _configService;
    private readonly BluetoothService _bluetoothService;
    
    private NativeMenuItem? _statusMenuItem;
    private NativeMenuItem? _connectDisconnectMenuItem;
    private NativeMenuItem? _autoDetectionMenuItem;
    
    private bool _isAudioConnected = false;
    private bool _isAutoDetectionEnabled = true;
    private string _deviceName = "Not configured";

    public TrayIconService(
        IClassicDesktopStyleApplicationLifetime desktop, 
        PythonProcessService? pythonService,
        ConfigService configService,
        BluetoothService bluetoothService)
    {
        _desktop = desktop;
        _pythonService = pythonService;
        _configService = configService;
        _bluetoothService = bluetoothService;
    }

    public async void Initialize()
    {
        if (Application.Current == null) return;

        // Load initial status
        await LoadStatusAsync();

        // Load the icon from assets
        WindowIcon? icon = null;
        try
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://HoldSense/assets/HoldSense.ico"));
            icon = new WindowIcon(iconStream);
        }
        catch
        {
            // Fallback: try loading from file path
            try
            {
                var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "HoldSense.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    icon = new WindowIcon(iconPath);
                }
            }
            catch
            {
                // If icon loading fails, continue without icon
            }
        }

        _trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "HoldSense - Running",
            IsVisible = true
        };

        var menu = new NativeMenu();

        // Status display (non-clickable, informational)
        _statusMenuItem = new NativeMenuItem { Header = GetStatusText(), IsEnabled = false };
        menu.Add(_statusMenuItem);

        menu.Add(new NativeMenuItemSeparator());

        // Connect/Disconnect
        _connectDisconnectMenuItem = new NativeMenuItem { Header = GetConnectDisconnectText() };
        _connectDisconnectMenuItem.Click += OnConnectDisconnect;
        menu.Add(_connectDisconnectMenuItem);

        // Auto Detection Toggle
        _autoDetectionMenuItem = new NativeMenuItem { Header = GetAutoDetectionText() };
        _autoDetectionMenuItem.Click += OnToggleAutoDetection;
        menu.Add(_autoDetectionMenuItem);

        menu.Add(new NativeMenuItemSeparator());

        // Open Settings
        var settingsMenuItem = new NativeMenuItem { Header = "Open Settings" };
        settingsMenuItem.Click += OnOpenSettings;
        menu.Add(settingsMenuItem);

        // Show Window
        var showMenuItem = new NativeMenuItem { Header = "Show Window" };
        showMenuItem.Click += OnShowWindow;
        menu.Add(showMenuItem);

        menu.Add(new NativeMenuItemSeparator());

        // Exit
        var exitMenuItem = new NativeMenuItem { Header = "Exit" };
        exitMenuItem.Click += OnExit;
        menu.Add(exitMenuItem);

        _trayIcon.Menu = menu;
        _trayIcon.Clicked += OnTrayIconClicked;

        // Setup Python output monitoring to update status
        if (_pythonService != null)
        {
            _pythonService.OutputReceived += OnPythonOutputReceived;
        }

        // Start periodic status refresh
        _ = Task.Run(async () =>
        {
            while (_trayIcon != null)
            {
                await Task.Delay(2000);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await LoadStatusAsync();
                    UpdateMenuItems();
                });
            }
        });
    }

    private async Task LoadStatusAsync()
    {
        try
        {
            var config = await _configService.LoadConfigAsync();
            if (config != null)
            {
                _isAutoDetectionEnabled = config.DetectionEnabled;
                
                if (!string.IsNullOrEmpty(config.PhoneBtAddress))
                {
                    // Try to get device name
                    var devices = await _bluetoothService.EnumerateA2dpDevicesAsync();
                    var device = devices.FirstOrDefault(d => d.MacAddress == config.PhoneBtAddress);
                    _deviceName = device?.Name ?? config.PhoneBtAddress;
                }
                else
                {
                    _deviceName = "Not configured";
                }
            }
        }
        catch { }
    }

    private void OnPythonOutputReceived(object? sender, string output)
    {
        if (output.StartsWith("STATUS:audio_active:"))
        {
            var value = output.Split(':')[2].Trim();
            _isAudioConnected = value.Equals("true", StringComparison.OrdinalIgnoreCase) || 
                               value.Equals("True", StringComparison.OrdinalIgnoreCase);
            
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateMenuItems();
            });
        }
        else if (output.StartsWith("STATUS:detection_enabled:"))
        {
            var value = output.Split(':')[2].Trim();
            _isAutoDetectionEnabled = value.Equals("true", StringComparison.OrdinalIgnoreCase) || 
                                     value.Equals("True", StringComparison.OrdinalIgnoreCase);
            
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateMenuItems();
            });
        }
    }

    private string GetStatusText()
    {
        if (_isAudioConnected)
        {
            return $"✓ Connected to {_deviceName}";
        }
        else
        {
            return $"○ Disconnected ({_deviceName})";
        }
    }

    private string GetConnectDisconnectText()
    {
        return _isAudioConnected ? "Disconnect Audio" : "Connect Audio";
    }

    private string GetAutoDetectionText()
    {
        return _isAutoDetectionEnabled ? "✓ Auto Detection" : "○ Auto Detection";
    }

    private void UpdateMenuItems()
    {
        if (_statusMenuItem != null)
        {
            _statusMenuItem.Header = GetStatusText();
        }
        if (_connectDisconnectMenuItem != null)
        {
            _connectDisconnectMenuItem.Header = GetConnectDisconnectText();
        }
        if (_autoDetectionMenuItem != null)
        {
            _autoDetectionMenuItem.Header = GetAutoDetectionText();
        }
        if (_trayIcon != null)
        {
            _trayIcon.ToolTipText = GetStatusText();
        }
    }

    private void OnShowWindow(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_desktop.MainWindow != null)
        {
            _desktop.MainWindow.Show();
            _desktop.MainWindow.WindowState = WindowState.Normal;
            _desktop.MainWindow.Activate();
        }
    }

    private async void OnConnectDisconnect(object? sender, EventArgs e)
    {
        if (_pythonService == null || !_pythonService.IsRunning)
        {
            return;
        }

        try
        {
            await _pythonService.ToggleAudioAsync();
        }
        catch { }
    }

    private async void OnToggleAutoDetection(object? sender, EventArgs e)
    {
        if (_pythonService == null || !_pythonService.IsRunning)
        {
            return;
        }

        try
        {
            await _pythonService.ToggleDetectionAsync();
            _isAutoDetectionEnabled = !_isAutoDetectionEnabled;
            
            // Save to config
            var config = await _configService.LoadConfigAsync();
            if (config != null)
            {
                config.DetectionEnabled = _isAutoDetectionEnabled;
                await _configService.SaveConfigAsync(config);
            }
            
            UpdateMenuItems();
        }
        catch { }
    }

    private async void OnOpenSettings(object? sender, EventArgs e)
    {
        ShowMainWindow();
        
        // Wait a bit for window to show, then trigger settings
        await Task.Delay(200);
        
        if (_desktop.MainWindow is MainWindow mainWindow)
        {
            // Trigger the settings command
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (mainWindow.DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.OpenSettingsCommand.Execute(null);
                }
            });
        }
    }

    private async void OnExit(object? sender, EventArgs e)
    {
        try
        {
            if (_pythonService != null && _pythonService.IsRunning)
            {
                await _pythonService.StopAsync();
            }
        }
        catch { }
        _desktop.Shutdown();
    }

    public void Dispose()
    {
        if (_pythonService != null)
        {
            _pythonService.OutputReceived -= OnPythonOutputReceived;
        }
        
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon = null;
        }
    }
}

