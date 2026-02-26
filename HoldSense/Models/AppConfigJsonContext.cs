using System.Text.Json;
using System.Text.Json.Serialization;

namespace _HoldSense.Models;

/// <summary>
/// JSON serializer context for AppConfig to support trimming.
/// This is required when PublishTrimmed is enabled in .NET 8+.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(AppConfig))]
public partial class AppConfigJsonContext : JsonSerializerContext
{
}

