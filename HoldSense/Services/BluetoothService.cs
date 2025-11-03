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
    public async Task<List<BluetoothDevice>> EnumerateA2dpDevicesAsync()
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                return new List<BluetoothDevice>();
            }

            var selector = AudioPlaybackConnection.GetDeviceSelector();
            var devices = await DeviceInformation.FindAllAsync(selector);

            var results = new List<BluetoothDevice>();

            foreach (var device in devices)
            {
                var mac = ExtractMacFromDeviceId(device.Id);
                if (!string.IsNullOrEmpty(mac))
                {
                    results.Add(new BluetoothDevice
                    {
                        Name = device.Name ?? "(Unknown)",
                        MacAddress = mac,
                        DeviceId = device.Id
                    });
                }
            }

            return results;
        }
        catch (Exception)
        {
            return new List<BluetoothDevice>();
        }
    }

    private string? ExtractMacFromDeviceId(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return null;

        // Search for a 12-character hexadecimal string (MAC address format in device IDs)
        var match = Regex.Match(deviceId, @"([0-9A-Fa-f]{12})");
        if (match.Success)
        {
            var macStr = match.Groups[1].Value;
            // Format as XX:XX:XX:XX:XX:XX
            return string.Join(":", Enumerable.Range(0, 6)
                .Select(i => macStr.Substring(i * 2, 2)))
                .ToUpper();
        }

        return null;
    }
}



