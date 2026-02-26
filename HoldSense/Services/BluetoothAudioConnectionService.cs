using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace _HoldSense.Services;

 [SupportedOSPlatform("windows10.0.19041.0")]
public sealed class BluetoothAudioConnectionService : IDisposable
{
    private static readonly Regex MacWithSeparatorsRegex = new(
        @"^(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$",
        RegexOptions.Compiled);

    private static readonly Regex MacCompactRegex = new(
        @"^[0-9A-Fa-f]{12}$",
        RegexOptions.Compiled);

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private AudioPlaybackConnection? _connection;
    private string? _deviceId;
    private bool _isConnected;

    public event EventHandler<bool>? ConnectionStateChanged;

    public bool IsConnected => _isConnected;

    public async Task<bool> ConnectAsync(string deviceIdentifier)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(deviceIdentifier))
        {
            return false;
        }

        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isConnected)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(_deviceId))
            {
                _deviceId = IsLikelyDeviceId(deviceIdentifier)
                    ? deviceIdentifier
                    : await ResolveDeviceIdFromMacAsync(deviceIdentifier).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(_deviceId))
            {
                return false;
            }

            _connection = AudioPlaybackConnection.TryCreateFromId(_deviceId);
            if (_connection == null)
            {
                return false;
            }

            _connection.StateChanged += OnConnectionStateChanged;
            await _connection.StartAsync().AsTask().ConfigureAwait(false);
            var openResult = await _connection.OpenAsync().AsTask().ConfigureAwait(false);

            if (openResult.Status == AudioPlaybackConnectionOpenResultStatus.Success)
            {
                SetConnected(true);
                return true;
            }

            if (IsLikelyDeviceId(deviceIdentifier))
            {
                // Device IDs may become stale after re-pair; retry once by fresh lookup if we can.
                _deviceId = await ResolveDeviceIdFromMacAsync(deviceIdentifier).ConfigureAwait(false);
            }

            CleanupConnection();
            SetConnected(false);
            return false;
        }
        catch
        {
            CleanupConnection();
            SetConnected(false);
            return false;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> DisconnectAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return false;
        }

        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            CleanupConnection();
            SetConnected(false);
            return true;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<string?> ResolveDeviceIdFromMacAsync(string deviceIdentifier)
    {
        try
        {
            var search = deviceIdentifier.Replace(":", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
            var selector = AudioPlaybackConnection.GetDeviceSelector();
            var devices = await DeviceInformation.FindAllAsync(selector).AsTask().ConfigureAwait(false);
            foreach (var device in devices)
            {
                if (string.Equals(device.Id, deviceIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    return device.Id;
                }

                if (device.Id?.ToUpperInvariant().Contains(search, StringComparison.Ordinal) == true)
                {
                    return device.Id;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private void OnConnectionStateChanged(AudioPlaybackConnection sender, object args)
    {
        switch (sender.State)
        {
            case AudioPlaybackConnectionState.Opened:
                SetConnected(true);
                break;
            case AudioPlaybackConnectionState.Closed:
                SetConnected(false);
                break;
        }
    }

    private void SetConnected(bool connected)
    {
        if (_isConnected == connected)
        {
            return;
        }

        _isConnected = connected;
        ConnectionStateChanged?.Invoke(this, _isConnected);
    }

    private void CleanupConnection()
    {
        if (_connection == null)
        {
            return;
        }

        try
        {
            _connection.StateChanged -= OnConnectionStateChanged;
            _connection.Dispose();
        }
        catch
        {
        }
        finally
        {
            _connection = null;
        }
    }

    public void Dispose()
    {
        CleanupConnection();
        _mutex.Dispose();
    }

    private static bool IsLikelyDeviceId(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        if (identifier.Contains("\\", StringComparison.Ordinal) ||
            identifier.Contains("#", StringComparison.Ordinal) ||
            identifier.StartsWith("BTH", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (MacWithSeparatorsRegex.IsMatch(identifier))
        {
            return false;
        }

        return !MacCompactRegex.IsMatch(identifier);
    }
}
