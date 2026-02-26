using _HoldSense.Models;
using _HoldSense.ViewModels;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace _HoldSense.Services;

internal sealed class TrayIconServiceWinUI : IDisposable
{
    private readonly MainWindowWinUI _window;
    private readonly MainWindowViewModel _viewModel;
    private readonly ConfigService _configService;
    private readonly BluetoothService _bluetoothService;
    private readonly IRuntimeService _runtimeService;
    private readonly SynchronizationContext _uiContext;

    private NotifyIcon? _notifyIcon;
    private ToolStripMenuItem? _statusMenuItem;
    private ToolStripMenuItem? _connectDisconnectMenuItem;
    private ToolStripMenuItem? _autoDetectionMenuItem;

    private bool _isAudioConnected;
    private bool _isAutoDetectionEnabled = true;
    private string _deviceName = "Not configured";
    private bool _isDisposed;
    private Task? _refreshLoopTask;

    public TrayIconServiceWinUI(
        MainWindowWinUI window,
        MainWindowViewModel viewModel,
        ConfigService configService,
        BluetoothService bluetoothService,
        IRuntimeService runtimeService,
        SynchronizationContext uiContext)
    {
        _window = window;
        _viewModel = viewModel;
        _configService = configService;
        _bluetoothService = bluetoothService;
        _runtimeService = runtimeService;
        _uiContext = uiContext;
    }

    public async Task InitializeAsync()
    {
        await LoadStatusAsync();
        CreateNotifyIcon();
        _runtimeService.StatusChanged += OnRuntimeStatusChanged;
        _refreshLoopTask = RefreshLoopAsync();
    }

    private void CreateNotifyIcon()
    {
        var menu = new ContextMenuStrip();

        _statusMenuItem = new ToolStripMenuItem(GetStatusText()) { Enabled = false };
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        _connectDisconnectMenuItem = new ToolStripMenuItem(GetConnectDisconnectText());
        _connectDisconnectMenuItem.Click += async (_, __) => await OnConnectDisconnectAsync();
        menu.Items.Add(_connectDisconnectMenuItem);

        _autoDetectionMenuItem = new ToolStripMenuItem(GetAutoDetectionText());
        _autoDetectionMenuItem.Click += async (_, __) => await OnToggleAutoDetectionAsync();
        menu.Items.Add(_autoDetectionMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        var settingsMenuItem = new ToolStripMenuItem("Open Settings");
        settingsMenuItem.Click += (_, __) =>
        {
            _uiContext.Post(async _ =>
            {
                _window.BringToFront();
                await _window.OpenSettingsAsync();
            }, null);
        };
        menu.Items.Add(settingsMenuItem);

        var showMenuItem = new ToolStripMenuItem("Show Window");
        showMenuItem.Click += (_, __) => _uiContext.Post(_ => _window.BringToFront(), null);
        menu.Items.Add(showMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitMenuItem = new ToolStripMenuItem("Exit");
        exitMenuItem.Click += async (_, __) => await OnExitAsync();
        menu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "HoldSense",
            ContextMenuStrip = menu,
            Icon = LoadIcon()
        };
        _notifyIcon.DoubleClick += (_, __) => _uiContext.Post(_ => _window.BringToFront(), null);

        UpdateMenuItems();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "HoldSense.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch
        {
        }

        return SystemIcons.Application;
    }

    private async Task RefreshLoopAsync()
    {
        try
        {
            while (!_isDisposed)
            {
                await Task.Delay(2500);
                await LoadStatusAsync();
                UpdateMenuItems();
            }
        }
        catch
        {
        }
    }

    private async Task LoadStatusAsync()
    {
        try
        {
            var config = await _configService.LoadConfigAsync();
            if (config != null)
            {
                _isAutoDetectionEnabled = config.DetectionEnabled;
                if (!string.IsNullOrWhiteSpace(config.PhoneBtAddress))
                {
                    var devices = await _bluetoothService.EnumerateA2dpDevicesAsync();
                    var device = devices.FirstOrDefault(d =>
                        d.MacAddress == config.PhoneBtAddress || d.DeviceId == config.PhoneBtAddress);
                    _deviceName = device?.Name ?? config.PhoneBtAddress;
                }
                else
                {
                    _deviceName = "Not configured";
                }
            }
        }
        catch
        {
        }
    }

    private void OnRuntimeStatusChanged(object? sender, RuntimeStatus status)
    {
        _isAudioConnected = status.AudioActive;
        _isAutoDetectionEnabled = status.DetectionEnabled;
        UpdateMenuItems();
    }

    private string GetStatusText()
    {
        return _isAudioConnected ? $"Connected to {_deviceName}" : $"Disconnected ({_deviceName})";
    }

    private string GetConnectDisconnectText()
    {
        return _isAudioConnected ? "Disconnect Audio" : "Connect Audio";
    }

    private string GetAutoDetectionText()
    {
        return _isAutoDetectionEnabled ? "Auto Detection: ON" : "Auto Detection: OFF";
    }

    private void UpdateMenuItems()
    {
        if (_notifyIcon == null)
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            if (_statusMenuItem != null)
            {
                _statusMenuItem.Text = GetStatusText();
            }

            if (_connectDisconnectMenuItem != null)
            {
                _connectDisconnectMenuItem.Text = GetConnectDisconnectText();
            }

            if (_autoDetectionMenuItem != null)
            {
                _autoDetectionMenuItem.Text = GetAutoDetectionText();
            }

            _notifyIcon.Text = "HoldSense";
        }, null);
    }

    private async Task OnConnectDisconnectAsync()
    {
        if (_runtimeService.IsRunning)
        {
            await _runtimeService.ToggleAudioAsync();
        }
    }

    private async Task OnToggleAutoDetectionAsync()
    {
        if (!_runtimeService.IsRunning)
        {
            return;
        }

        _isAutoDetectionEnabled = !_isAutoDetectionEnabled;
        await _runtimeService.SetAutoEnabledAsync(_isAutoDetectionEnabled);

        var config = await _configService.LoadConfigAsync();
        if (config != null)
        {
            config.DetectionEnabled = _isAutoDetectionEnabled;
            await _configService.SaveConfigAsync(config);
            await _viewModel.InitializeAsync();
        }

        UpdateMenuItems();
    }

    private async Task OnExitAsync()
    {
        if (_runtimeService.IsRunning)
        {
            await _runtimeService.StopAsync();
        }

        _uiContext.Post(_ =>
        {
            _window.CloseForExit();
            Microsoft.UI.Xaml.Application.Current.Exit();
        }, null);
    }

    public void Dispose()
    {
        _isDisposed = true;
        _runtimeService.StatusChanged -= OnRuntimeStatusChanged;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
