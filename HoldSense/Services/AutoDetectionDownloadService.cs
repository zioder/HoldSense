using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace _HoldSense.Services;

/// <summary>
/// Downloads and manages optional auto-detection ONNX models.
/// </summary>
public class AutoDetectionDownloadService
{
    private const string OnnxOptimizedFileName = "yolo26n_416_int8.onnx";
    private const string OnnxStandardFileName = "yolo26n.onnx";

    // Preferred model URL (known-good). Keep fallback for resilience.
    private const string OptimizedModelUrl = "https://github.com/ultralytics/assets/releases/download/v8.3.0/yolo11n.onnx";
    private const string StandardModelUrl = "https://github.com/ultralytics/assets/releases/download/v8.3.0/yolo11n.onnx";
    private const long MinOnnxSizeBytes = 2_000_000;

    private readonly ConfigService _configService;
    private readonly HttpClient _httpClient;

    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;
    public event EventHandler<string>? StatusChanged;

    public AutoDetectionDownloadService(ConfigService configService)
    {
        _configService = configService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public string GetModelPath()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;

        string? FindModelInDir(string dir)
        {
            var optimized = Path.Combine(dir, OnnxOptimizedFileName);
            if (File.Exists(optimized))
            {
                return optimized;
            }

            var standard = Path.Combine(dir, OnnxStandardFileName);
            if (File.Exists(standard))
            {
                return standard;
            }

            return null;
        }

        for (var current = exeDir; !string.IsNullOrWhiteSpace(current); current = Directory.GetParent(current)?.FullName ?? string.Empty)
        {
            var model = FindModelInDir(current);
            if (!string.IsNullOrWhiteSpace(model))
            {
                return model;
            }
        }

        return Path.Combine(exeDir, OnnxOptimizedFileName);
    }

    public bool IsAutoDetectionReady()
    {
        var modelPath = GetModelPath();
        if (!File.Exists(modelPath))
        {
            return false;
        }

        var fileInfo = new FileInfo(modelPath);
        return fileInfo.Length >= MinOnnxSizeBytes;
    }

    public async Task<bool> DownloadAutoDetectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var modelPath = GetModelPath();
            var modelDir = Path.GetDirectoryName(modelPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(modelDir);

            if (IsAutoDetectionReady())
            {
                var existingMb = new FileInfo(modelPath).Length / (1024.0 * 1024.0);
                ProgressChanged?.Invoke(this, new DownloadProgressEventArgs(100, existingMb, existingMb));
                StatusChanged?.Invoke(this, $"Model already available ({existingMb:F1} MB).");
                var existingConfig = await _configService.LoadConfigAsync() ?? new Models.AppConfig();
                existingConfig.AutoDetectionDownloaded = true;
                await _configService.SaveConfigAsync(existingConfig);
                return true;
            }

            // Attempt optimized model first.
            StatusChanged?.Invoke(this, "Downloading optimized AI model...");
            var optimizedPath = Path.Combine(modelDir, OnnxOptimizedFileName);
            var downloaded = await DownloadFileAsync(OptimizedModelUrl, optimizedPath, cancellationToken);

            // Fallback model URL.
            if (!downloaded)
            {
                StatusChanged?.Invoke(this, "Optimized model unavailable, downloading fallback model...");
                var standardPath = Path.Combine(modelDir, OnnxStandardFileName);
                downloaded = await DownloadFileAsync(StandardModelUrl, standardPath, cancellationToken);
            }

            if (!downloaded || !IsAutoDetectionReady())
            {
                StatusChanged?.Invoke(this, "Download failed. Could not fetch a valid ONNX model.");
                return false;
            }

            var config = await _configService.LoadConfigAsync() ?? new Models.AppConfig();
            config.AutoDetectionDownloaded = true;
            await _configService.SaveConfigAsync(config);
            StatusChanged?.Invoke(this, "Download complete! Auto-detection is ready.");
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke(this, "Download cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var tempPath = destinationPath + ".tmp";

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var totalBytes = response.Content.Headers.ContentLength ?? 1L;
            var downloadedBytes = 0L;

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                var buffer = new byte[8192];
                var lastProgress = DateTime.UtcNow;

                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read <= 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloadedBytes += read;

                    if ((DateTime.UtcNow - lastProgress).TotalMilliseconds > 100)
                    {
                        var pct = totalBytes <= 0 ? 0 : downloadedBytes * 100.0 / totalBytes;
                        var downMb = downloadedBytes / (1024.0 * 1024.0);
                        var totalMb = totalBytes / (1024.0 * 1024.0);
                        ProgressChanged?.Invoke(this, new DownloadProgressEventArgs(pct, downMb, totalMb));
                        StatusChanged?.Invoke(this, $"Downloading: {downMb:F1} / {totalMb:F1} MB ({pct:F0}%)");
                        lastProgress = DateTime.UtcNow;
                    }
                }
            }

            var fileInfo = new FileInfo(tempPath);
            if (fileInfo.Length < MinOnnxSizeBytes)
            {
                File.Delete(tempPath);
                return false;
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempPath, destinationPath);

            var fileMb = fileInfo.Length / (1024.0 * 1024.0);
            ProgressChanged?.Invoke(this, new DownloadProgressEventArgs(100, fileMb, fileMb));
            return true;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            return false;
        }
    }

    public async Task<bool> RemoveAutoDetectionAsync()
    {
        try
        {
            var path = GetModelPath();
            var dir = Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory;
            var optimized = Path.Combine(dir, OnnxOptimizedFileName);
            var standard = Path.Combine(dir, OnnxStandardFileName);

            if (File.Exists(optimized))
            {
                File.Delete(optimized);
            }

            if (File.Exists(standard))
            {
                File.Delete(standard);
            }

            var config = await _configService.LoadConfigAsync() ?? new Models.AppConfig();
            config.AutoDetectionDownloaded = false;
            config.DetectionEnabled = false;
            await _configService.SaveConfigAsync(config);
            StatusChanged?.Invoke(this, "Auto-detection components removed.");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error removing: {ex.Message}");
            return false;
        }
    }

    public double GetAutoDetectionSizeMB()
    {
        try
        {
            var modelPath = GetModelPath();
            if (File.Exists(modelPath))
            {
                return new FileInfo(modelPath).Length / (1024.0 * 1024.0);
            }
        }
        catch
        {
        }

        return 0;
    }
}

public class DownloadProgressEventArgs : EventArgs
{
    public double ProgressPercent { get; }
    public double DownloadedMB { get; }
    public double TotalMB { get; }

    public DownloadProgressEventArgs(double progressPercent, double downloadedMB, double totalMB)
    {
        ProgressPercent = progressPercent;
        DownloadedMB = downloadedMB;
        TotalMB = totalMB;
    }
}
