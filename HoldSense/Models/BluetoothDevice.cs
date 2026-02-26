namespace _HoldSense.Models;

public class BluetoothDevice
{
    public string Name { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;

    public string DisplayName => $"{Name} ({FormatIdentifier(MacAddress)})";

    public override string ToString() => DisplayName;

    private static string FormatIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(unknown)";

        if (value.Length <= 28)
            return value;

        return value[..14] + "..." + value[^10..];
    }
}






