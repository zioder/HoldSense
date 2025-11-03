using System.Text.Json.Serialization;

namespace _HoldSense.Models;

public class AppConfig
{
    [JsonPropertyName("phone_bt_address")]
    public string PhoneBtAddress { get; set; } = string.Empty;

    [JsonPropertyName("detection_enabled")]
    public bool DetectionEnabled { get; set; } = false;

    [JsonPropertyName("keybind_enabled")]
    public bool KeybindEnabled { get; set; } = true;

    public string PythonExePath { get; set; } = string.Empty;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "auto"; // values: auto, light, dark

    [JsonPropertyName("webcam_index")]
    public int WebcamIndex { get; set; } = 0;
}



