namespace _HoldSense.Models;

public class RuntimeStatus
{
    public bool AudioActive { get; set; }
    public bool DetectionEnabled { get; set; }
    public bool PhoneDetected { get; set; }
    public bool AutoDetectionAvailable { get; set; } = true;
    public bool KeybindEnabled { get; set; } = true;
    public string? ManualOverrideState { get; set; }
}
