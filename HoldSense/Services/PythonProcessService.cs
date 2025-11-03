using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace _HoldSense.Services;

public class PythonProcessService
{
    private Process? _pythonProcess;
    private readonly string _pythonScriptPath;
    private readonly string? _venvPythonPath;
    private readonly ConfigService _configService;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<string>? ErrorReceived;
    public event EventHandler? ProcessExited;

    public bool IsRunning => _pythonProcess != null && !_pythonProcess.HasExited;

    public PythonProcessService(ConfigService configService)
    {
        _configService = configService;

        // Try multiple strategies to find main.py and .venv
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string? scriptPath = null;
        string? venvPython = null;

        // Strategy 1: Check 4 levels up (for net8.0-windows Debug/Release builds)
        var projectRoot = Directory.GetParent(exeDir)?.Parent?.Parent?.Parent?.FullName;
        if (projectRoot != null)
        {
            var candidatePath = Path.Combine(projectRoot, "main.py");
            if (File.Exists(candidatePath))
            {
                scriptPath = candidatePath;
                // Check for .venv in the same directory
                var venvPath = Path.Combine(projectRoot, ".venv", "Scripts", "python.exe");
                if (File.Exists(venvPath))
                {
                    venvPython = venvPath;
                }
            }
        }

        // Strategy 2: Check 3 levels up (alternative structure)
        if (scriptPath == null)
        {
            projectRoot = Directory.GetParent(exeDir)?.Parent?.Parent?.FullName;
            if (projectRoot != null)
            {
                var candidatePath = Path.Combine(projectRoot, "main.py");
                if (File.Exists(candidatePath))
                {
                    scriptPath = candidatePath;
                    // Check for .venv in the same directory
                    var venvPath = Path.Combine(projectRoot, ".venv", "Scripts", "python.exe");
                    if (File.Exists(venvPath))
                    {
                        venvPython = venvPath;
                    }
                }
            }
        }

        // Strategy 4: Check same directory as exe
        if (scriptPath == null)
        {
            var candidatePath = Path.Combine(exeDir, "main.py");
            if (File.Exists(candidatePath))
            {
                scriptPath = candidatePath;
                // Check for .venv in the same directory
                var venvPath = Path.Combine(exeDir, ".venv", "Scripts", "python.exe");
                if (File.Exists(venvPath))
                {
                    venvPython = venvPath;
                }
            }
        }

        // Strategy 5: Check parent of exe directory
        if (scriptPath == null)
        {
            var parentDir = Directory.GetParent(exeDir)?.FullName;
            if (parentDir != null)
            {
                var candidatePath = Path.Combine(parentDir, "main.py");
                if (File.Exists(candidatePath))
                {
                    scriptPath = candidatePath;
                    // Check for .venv in the same directory
                    var venvPath = Path.Combine(parentDir, ".venv", "Scripts", "python.exe");
                    if (File.Exists(venvPath))
                    {
                        venvPython = venvPath;
                    }
                }
            }
        }

        // Strategy 3: Check 5 levels up (for exe in subproject directory)
        if (scriptPath == null)
        {
            projectRoot = Directory.GetParent(exeDir)?.Parent?.Parent?.Parent?.Parent?.FullName;
            if (projectRoot != null)
            {
                var candidatePath = Path.Combine(projectRoot, "main.py");
                if (File.Exists(candidatePath))
                {
                    scriptPath = candidatePath;
                    // Check for .venv in the same directory
                    var venvPath = Path.Combine(projectRoot, ".venv", "Scripts", "python.exe");
                    if (File.Exists(venvPath))
                    {
                        venvPython = venvPath;
                    }
                }
            }
        }

        _pythonScriptPath = scriptPath ?? string.Empty;
        _venvPythonPath = venvPython;
        
    }

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        // Validate that the script path exists
        if (string.IsNullOrEmpty(_pythonScriptPath) || !File.Exists(_pythonScriptPath))
        {
            var errorMsg = string.IsNullOrEmpty(_pythonScriptPath)
                ? "Python script path is empty. Could not locate main.py."
                : $"Python script not found at: {_pythonScriptPath}";
            
            ErrorReceived?.Invoke(this, errorMsg);
            throw new FileNotFoundException(errorMsg, _pythonScriptPath);
        }

        try
        {
            var config = await _configService.LoadConfigAsync();
            
            // Prioritize .venv Python, then config, then system Python
            string pythonExe;
            if (!string.IsNullOrEmpty(_venvPythonPath) && File.Exists(_venvPythonPath))
            {
                pythonExe = _venvPythonPath;
            }
            else if (!string.IsNullOrEmpty(config?.PythonExePath))
            {
                pythonExe = config.PythonExePath;
            }
            else
            {
                pythonExe = "python";
            }

            // Ensure the path is properly quoted and escaped
            var scriptPathQuoted = $"\"{_pythonScriptPath}\"";
            var workingDir = Path.GetDirectoryName(_pythonScriptPath);
            if (string.IsNullOrEmpty(workingDir))
            {
                workingDir = Environment.CurrentDirectory;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"{scriptPathQuoted} --no-ui",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDir
            };

            _pythonProcess = new Process { StartInfo = startInfo };

            _pythonProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    OutputReceived?.Invoke(this, e.Data);
                }
            };

            _pythonProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    ErrorReceived?.Invoke(this, e.Data);
                }
            };

            _pythonProcess.Exited += (sender, e) =>
            {
                ProcessExited?.Invoke(this, EventArgs.Empty);
            };

            _pythonProcess.EnableRaisingEvents = true;

            _pythonProcess.Start();
            _pythonProcess.BeginOutputReadLine();
            _pythonProcess.BeginErrorReadLine();

            
        }
        catch (Exception)
        {
            throw;
        }

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_pythonProcess == null || _pythonProcess.HasExited)
        {
            return;
        }

        try
        {
            _pythonProcess.Kill(entireProcessTree: true);
            await _pythonProcess.WaitForExitAsync();
            _pythonProcess.Dispose();
            _pythonProcess = null;
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task SendCommandAsync(string command)
    {
        if (_pythonProcess == null || _pythonProcess.HasExited)
        {
            return;
        }

        try
        {
            await _pythonProcess.StandardInput.WriteLineAsync(command);
            await _pythonProcess.StandardInput.FlushAsync();
        }
        catch (Exception)
        {
        }
    }

    public async Task ToggleDetectionAsync()
    {
        await SendCommandAsync("toggle_detection");
    }

    public async Task ToggleAudioAsync()
    {
        await SendCommandAsync("toggle_audio");
    }

    public async Task DisconnectAudioAsync()
    {
        await SendCommandAsync("disconnect_audio");
    }

    public async Task GetStatusAsync()
    {
        await SendCommandAsync("get_status");
    }

    public async Task SetKeybindEnabledAsync(bool enabled)
    {
        await SendCommandAsync($"set_keybind_enabled:{(enabled ? "true" : "false")}");
    }

    public async Task SetAutoEnabledAsync(bool enabled)
    {
        await SendCommandAsync($"set_auto_enabled:{(enabled ? "true" : "false")}");
    }

    public async Task SetWebcamIndexAsync(int index)
    {
        await SendCommandAsync($"set_webcam_index:{index}");
    }

    public void Dispose()
    {
        if (_pythonProcess != null && !_pythonProcess.HasExited)
        {
            _pythonProcess.Kill(entireProcessTree: true);
            _pythonProcess.Dispose();
        }
    }
}

