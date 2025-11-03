using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using _HoldSense.Models;
using _HoldSense.Services;

namespace _HoldSense.ViewModels;

public partial class DeviceSelectorViewModel : ViewModelBase
{
    private readonly BluetoothService _bluetoothService;
    private readonly ConfigService _configService;

    [ObservableProperty]
    private ObservableCollection<BluetoothDevice> _devices = new();

    [ObservableProperty]
    private BluetoothDevice? _selectedDevice;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Loading Bluetooth devices...";

    public event EventHandler<BluetoothDevice?>? DeviceSelected;

    public DeviceSelectorViewModel(BluetoothService bluetoothService, ConfigService configService)
    {
        _bluetoothService = bluetoothService;
        _configService = configService;
    }

    public async Task InitializeAsync()
    {
        await RefreshDevicesAsync();
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        IsLoading = true;
        StatusMessage = "Scanning for Bluetooth devices...";

        try
        {
            var devices = await _bluetoothService.EnumerateA2dpDevicesAsync();
            Devices.Clear();

            foreach (var device in devices)
            {
                Devices.Add(device);
            }

            if (Devices.Count == 0)
            {
                StatusMessage = "No Bluetooth audio devices found. Make sure your device is paired.";
            }
            else
            {
                StatusMessage = $"Found {Devices.Count} device(s). Select your phone:";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error scanning devices: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelectDevice))]
    private async Task SelectDeviceAsync()
    {
        if (SelectedDevice == null)
            return;

        try
        {
            var config = await _configService.LoadConfigAsync() ?? new AppConfig();
            config.PhoneBtAddress = SelectedDevice.MacAddress;
            await _configService.SaveConfigAsync(config);

            DeviceSelected?.Invoke(this, SelectedDevice);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving configuration: {ex.Message}";
        }
    }

    private bool CanSelectDevice() => SelectedDevice != null;

    [RelayCommand]
    private void Skip()
    {
        DeviceSelected?.Invoke(this, null);
    }

    partial void OnSelectedDeviceChanged(BluetoothDevice? value)
    {
        SelectDeviceCommand.NotifyCanExecuteChanged();
    }
}






