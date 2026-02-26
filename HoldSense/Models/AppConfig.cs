using System;
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

    [JsonPropertyName("python_exe_path")]
    [Obsolete("Deprecated: Python backend is no longer required.")]
    public string PythonExePath { get; set; } = string.Empty;

    [JsonPropertyName("backend_mode")]
    public string BackendMode { get; set; } = "dotnet";

    [JsonPropertyName("enable_python_fallback")]
    public bool EnablePythonFallback { get; set; } = false;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "auto"; // values: auto, light, dark

    [JsonPropertyName("webcam_index")]
    public int WebcamIndex { get; set; } = 0;

    /// <summary>
    /// Whether auto-detection components (ONNX model, OpenCV, etc.) have been downloaded.
    /// When false, the app runs in keybind-only mode.
    /// </summary>
    [JsonPropertyName("auto_detection_downloaded")]
    public bool AutoDetectionDownloaded { get; set; } = false;
}
