using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using _HoldSense.Models;

namespace _HoldSense.Services;

public class ConfigService
{
    private const string ConfigFileName = "bt_config.json";
    private readonly string _configPath;

    public ConfigService()
    {
        // Store config in the same directory as the executable
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        _configPath = Path.Combine(exeDir, ConfigFileName);
    }

    public string ConfigPath => _configPath;

    public async Task<AppConfig?> LoadConfigAsync()
    {
        try
        {
            if (!File.Exists(_configPath))
                return null;

            var json = await File.ReadAllTextAsync(_configPath);
            // Use the source-generated context for trimming support
            return JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task SaveConfigAsync(AppConfig config)
    {
        try
        {
            // Use the source-generated context for trimming support
            var json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception)
        {
            throw;
        }
    }

    public bool ConfigExists()
    {
        return File.Exists(_configPath);
    }
}


