namespace _HoldSense.Models;

public class BluetoothDevice
{
    public string Name { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;

    public string DisplayName => $"{Name} ({MacAddress})";

    public override string ToString() => DisplayName;
}






