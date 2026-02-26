using System;
using System.Threading.Tasks;
using _HoldSense.Models;

namespace _HoldSense.Services;

public interface IRuntimeService : IDisposable
{
    event EventHandler<RuntimeStatus>? StatusChanged;
    event EventHandler<string>? RuntimeError;

    bool IsRunning { get; }

    Task StartAsync();
    Task StopAsync();
    Task ToggleAudioAsync();
    Task DisconnectAudioAsync();
    Task SetAutoEnabledAsync(bool enabled);
    Task SetKeybindEnabledAsync(bool enabled);
    Task SetWebcamIndexAsync(int index);
    Task ClearManualOverrideAsync();
    Task<RuntimeStatus> GetStatusAsync();
}
