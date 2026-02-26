using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _HoldSense.Models;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace _HoldSense.Services;

public class BluetoothService
{
    private static readonly Regex MacWithSeparatorsRegex = new(
        @"(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])",
        RegexOptions.Compiled);

    private static readonly Regex MacCompactRegex = new(
        @"(?:#|_Dev_)([0-9A-Fa-f]{12})(?=[_#&\\]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<List<BluetoothDevice>> EnumerateA2dpDevicesAsync()
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                return new List<BluetoothDevice>();
            }

            var selector = AudioPlaybackConnection.GetDeviceSelector();
            var requestedProperties = new[]
            {
                "System.Devices.Aep.DeviceAddress"
            };
            var devices = await DeviceInformation.FindAllAsync(selector, requestedProperties);

            var results = new List<BluetoothDevice>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var device in devices)
            {
                if (string.IsNullOrWhiteSpace(device.Id))
                {
                    continue;
                }

                var identifier = ExtractMacFromDeviceId(device.Id)
                    ?? ExtractMacFromProperties(device)
                    ?? device.Id;

                if (!seen.Add(identifier))
                {
                    continue;
                }

                results.Add(new BluetoothDevice
                {
                    Name = string.IsNullOrWhiteSpace(device.Name) ? "(Unknown)" : device.Name,
                    MacAddress = identifier,
                    DeviceId = device.Id
                });
            }

            return results;
        }
        catch (Exception)
        {
            return new List<BluetoothDevice>();
        }
    }

    private static string? ExtractMacFromDeviceId(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return null;

        var separated = MacWithSeparatorsRegex.Match(deviceId);
        if (separated.Success)
        {
            return NormalizeMac(separated.Value);
        }

        var compact = MacCompactRegex.Match(deviceId);
        if (compact.Success)
        {
            return NormalizeMac(compact.Groups[1].Value);
        }

        return null;
    }

    private static string? ExtractMacFromProperties(DeviceInformation device)
    {
        if (!device.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var value) || value == null)
        {
            return null;
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var separated = MacWithSeparatorsRegex.Match(text);
        if (separated.Success)
        {
            return NormalizeMac(separated.Value);
        }

        var compact = Regex.Match(text, @"(?<![0-9A-Fa-f])([0-9A-Fa-f]{12})(?![0-9A-Fa-f])");
        if (compact.Success)
        {
            return NormalizeMac(compact.Groups[1].Value);
        }

        return null;
    }

    private static string NormalizeMac(string value)
    {
        var hex = Regex.Replace(value, @"[^0-9A-Fa-f]", string.Empty).ToUpperInvariant();
        if (hex.Length != 12)
        {
            return value.Trim().ToUpperInvariant();
        }

        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }
}

