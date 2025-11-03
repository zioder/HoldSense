using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace _HoldSense.Services;

public class WebcamDevice
{
    public string Name { get; set; } = string.Empty;
    public int Index { get; set; }
    public string Id { get; set; } = string.Empty;
}

public class WebcamService
{
    public async Task<List<WebcamDevice>> EnumerateWebcamsAsync()
    {
        var devices = new List<WebcamDevice>();
        
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                // Fallback: return devices 0-9 for older Windows
                for (int i = 0; i < 10; i++)
                {
                    devices.Add(new WebcamDevice
                    {
                        Name = $"Camera {i}",
                        Index = i,
                        Id = i.ToString()
                    });
                }
                return devices;
            }

            // Use Windows.Devices.Enumeration to find video capture devices
            var deviceSelector = Windows.Devices.Enumeration.DeviceClass.VideoCapture;
            var videoDevices = await DeviceInformation.FindAllAsync(deviceSelector);

            int index = 0;
            foreach (var device in videoDevices)
            {
                devices.Add(new WebcamDevice
                {
                    Name = device.Name ?? $"Camera {index}",
                    Index = index,
                    Id = device.Id
                });
                index++;
            }

            // If no devices found via API, provide fallback indices
            if (devices.Count == 0)
            {
                for (int i = 0; i < 10; i++)
                {
                    devices.Add(new WebcamDevice
                    {
                        Name = $"Camera {i}",
                        Index = i,
                        Id = i.ToString()
                    });
                }
            }
        }
        catch (Exception)
        {
            // Fallback: return devices 0-9 if enumeration fails
            for (int i = 0; i < 10; i++)
            {
                devices.Add(new WebcamDevice
                {
                    Name = $"Camera {i}",
                    Index = i,
                    Id = i.ToString()
                });
            }
        }

        return devices;
    }
}

