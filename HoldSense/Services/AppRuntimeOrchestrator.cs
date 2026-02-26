using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using _HoldSense.Models;

namespace _HoldSense.Services;

 [SupportedOSPlatform("windows10.0.19041.0")]
public sealed class AppRuntimeOrchestrator : IRuntimeService
{
    private const int ConsecutiveFramesTrigger = 3;
    private const int ConsecutiveFramesIdle = 100;

    private readonly ConfigService _configService;
    private readonly BluetoothAudioConnectionService _audioService;
    private readonly DetectionRuntimeService _detectionService;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private bool _running;
    private bool _detectionEnabled;
    private bool _keybindEnabled = true;
    private bool _audioActive;
    private bool _phoneDetected;
    private string? _manualOverrideState;
    private int _detectionCounter;
    private int _idleCounter;
    private int _webcamIndex;
    private string _phoneBtAddress = string.Empty;
    private RuntimeStatus _lastStatus = new();

    public event EventHandler<RuntimeStatus>? StatusChanged;
    public event EventHandler<string>? RuntimeError;

    public bool IsRunning => _running;

    public AppRuntimeOrchestrator(ConfigService configService)
    {
        _configService = configService;
        _audioService = new BluetoothAudioConnectionService();
        _detectionService = new DetectionRuntimeService(configService);
        _hotkeyService = new GlobalHotkeyService();

        _audioService.ConnectionStateChanged += (_, connected) =>
        {
            _audioActive = connected;
            PublishStatus();
        };

        _detectionService.PhoneDetectionChanged += (_, detected) =>
        {
            _phoneDetected = detected;
            _ = EvaluateAutomaticActionAsync();
        };

        _detectionService.RuntimeError += (_, error) => RuntimeError?.Invoke(this, error);
        _hotkeyService.AudioTogglePressed += async (_, _) => await ToggleAudioFromHotkeyAsync().ConfigureAwait(false);
        _hotkeyService.DetectionTogglePressed += async (_, _) => await ToggleDetectionFromHotkeyAsync().ConfigureAwait(false);
    }

    public async Task StartAsync()
    {
        if (_running)
        {
            return;
        }

        var config = await _configService.LoadConfigAsync().ConfigureAwait(false) ?? new AppConfig();
        _phoneBtAddress = config.PhoneBtAddress ?? string.Empty;
        _detectionEnabled = config.DetectionEnabled;
        _keybindEnabled = config.KeybindEnabled;
        _webcamIndex = Math.Max(0, config.WebcamIndex);
        _manualOverrideState = null;
        _detectionCounter = 0;
        _idleCounter = 0;
        _phoneDetected = false;
        _audioActive = _audioService.IsConnected;

        await _detectionService.StartAsync(_webcamIndex).ConfigureAwait(false);
        await _detectionService.SetEnabledAsync(_detectionEnabled && _detectionService.AutoDetectionAvailable).ConfigureAwait(false);

        _hotkeyService.KeybindEnabled = _keybindEnabled;
        _hotkeyService.Start();

        _running = true;
        PublishStatus(force: true);
    }

    public async Task StopAsync()
    {
        if (!_running)
        {
            return;
        }

        _hotkeyService.Stop();
        await _detectionService.StopAsync().ConfigureAwait(false);
        await _audioService.DisconnectAsync().ConfigureAwait(false);

        _running = false;
        _audioActive = false;
        _phoneDetected = false;
        _detectionCounter = 0;
        _idleCounter = 0;
        PublishStatus(force: true);
    }

    public async Task ToggleAudioAsync()
    {
        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_running)
            {
                return;
            }

            if (_audioActive)
            {
                _manualOverrideState = "off";
                await _audioService.DisconnectAsync().ConfigureAwait(false);
            }
            else
            {
                _manualOverrideState = "on";
                await ConnectAudioInternalAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _mutex.Release();
            PublishStatus();
        }
    }

    public async Task DisconnectAudioAsync()
    {
        _manualOverrideState = "off";
        await _audioService.DisconnectAsync().ConfigureAwait(false);
        _audioActive = false;
        PublishStatus();
    }

    public async Task SetAutoEnabledAsync(bool enabled)
    {
        var autoAvailable = _detectionService.AutoDetectionAvailable;
        _detectionEnabled = enabled && autoAvailable;

        if (enabled && !autoAvailable)
        {
            RuntimeError?.Invoke(this, "Auto-detection model is not installed. Open Settings and download Auto-Detection.");
        }

        if (!_detectionEnabled)
        {
            _detectionCounter = 0;
            _idleCounter = 0;
            _phoneDetected = false;
        }

        if (_running)
        {
            await _detectionService.SetEnabledAsync(_detectionEnabled).ConfigureAwait(false);
        }

        PublishStatus();
    }

    public Task SetKeybindEnabledAsync(bool enabled)
    {
        _keybindEnabled = enabled;
        _hotkeyService.KeybindEnabled = enabled;
        PublishStatus();
        return Task.CompletedTask;
    }

    public async Task SetWebcamIndexAsync(int index)
    {
        _webcamIndex = Math.Max(0, index);
        if (_running)
        {
            await _detectionService.SetWebcamIndexAsync(_webcamIndex).ConfigureAwait(false);
        }
    }

    public Task ClearManualOverrideAsync()
    {
        _manualOverrideState = null;
        PublishStatus();
        return Task.CompletedTask;
    }

    public Task<RuntimeStatus> GetStatusAsync()
    {
        var status = new RuntimeStatus
        {
            AudioActive = _audioActive,
            DetectionEnabled = _detectionEnabled && _detectionService.AutoDetectionAvailable,
            PhoneDetected = _phoneDetected,
            AutoDetectionAvailable = _detectionService.AutoDetectionAvailable,
            KeybindEnabled = _keybindEnabled,
            ManualOverrideState = _manualOverrideState
        };
        return Task.FromResult(status);
    }

    private async Task ToggleAudioFromHotkeyAsync()
    {
        if (!_keybindEnabled)
        {
            return;
        }

        await ToggleAudioAsync().ConfigureAwait(false);
    }

    private async Task ToggleDetectionFromHotkeyAsync()
    {
        if (!_keybindEnabled)
        {
            return;
        }

        await SetAutoEnabledAsync(!_detectionEnabled).ConfigureAwait(false);
    }

    private async Task EvaluateAutomaticActionAsync()
    {
        if (!_running)
        {
            return;
        }

        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_detectionEnabled)
            {
                if (_phoneDetected)
                {
                    _idleCounter = 0;
                    _detectionCounter++;
                }
                else
                {
                    _detectionCounter = 0;
                    _idleCounter++;
                }
            }
            else
            {
                _detectionCounter = 0;
                _idleCounter = 0;
            }

            bool? autoWantsOn = null;
            if (_detectionEnabled)
            {
                if (_detectionCounter >= ConsecutiveFramesTrigger)
                {
                    autoWantsOn = true;
                }
                else if (_idleCounter >= ConsecutiveFramesIdle)
                {
                    autoWantsOn = false;
                }
            }

            string? desired = null;
            if (_keybindEnabled && _manualOverrideState == "on")
            {
                if (!_audioActive)
                {
                    desired = "connect";
                }
            }
            else if (_keybindEnabled && _manualOverrideState == "off")
            {
                if (autoWantsOn is true && !_audioActive)
                {
                    desired = "connect";
                }
                else if (autoWantsOn is false && _audioActive)
                {
                    desired = "disconnect";
                }
            }
            else
            {
                if (autoWantsOn is true && !_audioActive)
                {
                    desired = "connect";
                }
                else if (autoWantsOn is false && _audioActive)
                {
                    desired = "disconnect";
                }
            }

            if (desired == "connect")
            {
                await ConnectAudioInternalAsync().ConfigureAwait(false);
            }
            else if (desired == "disconnect")
            {
                await _audioService.DisconnectAsync().ConfigureAwait(false);
                _audioActive = false;
            }
        }
        finally
        {
            _mutex.Release();
            PublishStatus();
        }
    }

    private async Task ConnectAudioInternalAsync()
    {
        if (string.IsNullOrWhiteSpace(_phoneBtAddress))
        {
            RuntimeError?.Invoke(this, "No Bluetooth device configured.");
            return;
        }

        _audioActive = await _audioService.ConnectAsync(_phoneBtAddress).ConfigureAwait(false);
    }

    private void PublishStatus(bool force = false)
    {
        var status = new RuntimeStatus
        {
            AudioActive = _audioActive,
            DetectionEnabled = _detectionEnabled && _detectionService.AutoDetectionAvailable,
            PhoneDetected = _phoneDetected,
            AutoDetectionAvailable = _detectionService.AutoDetectionAvailable,
            KeybindEnabled = _keybindEnabled,
            ManualOverrideState = _manualOverrideState
        };

        if (!force &&
            _lastStatus.AudioActive == status.AudioActive &&
            _lastStatus.DetectionEnabled == status.DetectionEnabled &&
            _lastStatus.PhoneDetected == status.PhoneDetected &&
            _lastStatus.AutoDetectionAvailable == status.AutoDetectionAvailable &&
            _lastStatus.KeybindEnabled == status.KeybindEnabled &&
            _lastStatus.ManualOverrideState == status.ManualOverrideState)
        {
            return;
        }

        _lastStatus = status;
        StatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _audioService.Dispose();
        _detectionService.Dispose();
        _hotkeyService.Dispose();
        _mutex.Dispose();
    }
}
