using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace _HoldSense.Services;

public sealed class DetectionRuntimeService : IDisposable
{
    private const float ConfThreshold = 0.25f;
    private const float NmsThreshold = 0.45f;
    private const int ProcessEveryNFrames = 2;

    private readonly ConfigService _configService;
    private readonly AutoDetectionDownloadService _downloadService;
    private readonly object _sync = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private VideoCapture? _capture;
    private InferenceSession? _session;
    private string? _inputName;
    private int _inputWidth = 640;
    private int _inputHeight = 640;
    private bool _enabled;
    private bool _phoneDetected;
    private bool _running;
    private int _webcamIndex;

    public event EventHandler<bool>? PhoneDetectionChanged;
    public event EventHandler<string>? RuntimeError;

    public bool IsRunning => _running;
    public bool IsEnabled => _enabled;
    public bool PhoneDetected => _phoneDetected;
    public bool AutoDetectionAvailable => _downloadService.IsAutoDetectionReady();

    public DetectionRuntimeService(ConfigService configService)
    {
        _configService = configService;
        _downloadService = new AutoDetectionDownloadService(configService);
    }

    public async Task StartAsync(int webcamIndex)
    {
        if (_running)
        {
            return;
        }

        _webcamIndex = webcamIndex;
        _cts = new CancellationTokenSource();
        _running = true;
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!_running)
        {
            return;
        }

        _cts?.Cancel();
        if (_loopTask != null)
        {
            try
            {
                await _loopTask;
            }
            catch
            {
            }
        }

        CleanupCamera();
        CleanupModel();
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
        _running = false;
        SetPhoneDetected(false);
    }

    public Task SetEnabledAsync(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            CleanupCamera();
            CleanupModel();
            SetPhoneDetected(false);
        }
        return Task.CompletedTask;
    }

    public Task SetWebcamIndexAsync(int webcamIndex)
    {
        if (webcamIndex < 0)
        {
            webcamIndex = 0;
        }

        lock (_sync)
        {
            _webcamIndex = webcamIndex;
        }

        CleanupCamera();
        return Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var frameCounter = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_enabled)
            {
                await Task.Delay(200, cancellationToken);
                continue;
            }

            if (!AutoDetectionAvailable)
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            if (!EnsureModelLoaded())
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            if (!EnsureCameraOpen())
            {
                await Task.Delay(100, cancellationToken);
                continue;
            }

            using var frame = new Mat();
            if (_capture == null || !_capture.Read(frame) || frame.Empty())
            {
                await Task.Delay(10, cancellationToken);
                continue;
            }

            frameCounter++;
            if (frameCounter % ProcessEveryNFrames != 0)
            {
                continue;
            }

            bool detected;
            try
            {
                detected = DetectPhone(frame);
            }
            catch (Exception ex)
            {
                RuntimeError?.Invoke(this, $"Detection failed: {ex.Message}");
                await Task.Delay(100, cancellationToken);
                continue;
            }

            SetPhoneDetected(detected);
        }
    }

    private bool EnsureCameraOpen()
    {
        if (_capture != null && _capture.IsOpened())
        {
            return true;
        }

        CleanupCamera();
        var idx = _webcamIndex;
        var apis = new[]
        {
            VideoCaptureAPIs.DSHOW,
            VideoCaptureAPIs.MSMF,
            VideoCaptureAPIs.ANY
        };

        foreach (var api in apis)
        {
            var cap = new VideoCapture(idx, api);
            if (cap.IsOpened())
            {
                _capture = cap;
                return true;
            }

            cap.Dispose();
        }

        RuntimeError?.Invoke(this, $"Unable to open webcam index {idx}.");
        return false;
    }

    private bool EnsureModelLoaded()
    {
        if (_session != null)
        {
            return true;
        }

        try
        {
            var modelPath = _downloadService.GetModelPath();
            if (!System.IO.File.Exists(modelPath))
            {
                RuntimeError?.Invoke(this, $"Model not found: {modelPath}");
                return false;
            }

            var options = new SessionOptions();
            try
            {
                options.AppendExecutionProvider_DML();
            }
            catch
            {
            }

            _session = new InferenceSession(modelPath, options);
            _inputName = _session.InputMetadata.Keys.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(_inputName))
            {
                RuntimeError?.Invoke(this, "Model input name could not be resolved.");
                CleanupModel();
                return false;
            }

            var dims = _session.InputMetadata[_inputName].Dimensions;
            if (dims.Length >= 4)
            {
                if (dims[2] > 0) _inputHeight = dims[2];
                if (dims[3] > 0) _inputWidth = dims[3];
            }
            return true;
        }
        catch (Exception ex)
        {
            RuntimeError?.Invoke(this, $"Model load failed: {ex.Message}");
            CleanupModel();
            return false;
        }
    }

    private bool DetectPhone(Mat frame)
    {
        if (_session == null || string.IsNullOrWhiteSpace(_inputName))
        {
            return false;
        }

        var width = frame.Width;
        var height = frame.Height;
        var scale = Math.Min((float)_inputWidth / width, (float)_inputHeight / height);
        var newW = Math.Max(1, (int)(width * scale));
        var newH = Math.Max(1, (int)(height * scale));
        var xOffset = (_inputWidth - newW) / 2;
        var yOffset = (_inputHeight - newH) / 2;

        using var resized = new Mat();
        Cv2.Resize(frame, resized, new Size(newW, newH), interpolation: InterpolationFlags.Linear);

        using var padded = new Mat(new Size(_inputWidth, _inputHeight), MatType.CV_8UC3, new Scalar(114, 114, 114));
        var roi = new Rect(xOffset, yOffset, newW, newH);
        resized.CopyTo(new Mat(padded, roi));

        if (!padded.IsContinuous())
        {
            throw new InvalidOperationException("Padded frame is not continuous.");
        }

        padded.GetArray(out Vec3b[] pixels);
        var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });
        for (var y = 0; y < _inputHeight; y++)
        {
            for (var x = 0; x < _inputWidth; x++)
            {
                var i = y * _inputWidth + x;
                var p = pixels[i];
                tensor[0, 0, y, x] = p.Item0 / 255f;
                tensor[0, 1, y, x] = p.Item1 / 255f;
                tensor[0, 2, y, x] = p.Item2 / 255f;
            }
        }

        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Run(inputs);
        var outTensor = outputs.First().AsTensor<float>();
        var dims = outTensor.Dimensions.ToArray();
        if (dims.Length != 3)
        {
            return false;
        }

        var candidates = new List<Candidate>(64);
        if (dims[1] == 84)
        {
            var anchorCount = dims[2];
            for (var i = 0; i < anchorCount; i++)
            {
                var score = outTensor[0, 4 + 67, i];
                if (score < ConfThreshold)
                {
                    continue;
                }

                var cx = outTensor[0, 0, i];
                var cy = outTensor[0, 1, i];
                var bw = outTensor[0, 2, i];
                var bh = outTensor[0, 3, i];
                candidates.Add(CreateCandidate(cx, cy, bw, bh, score, width, height, xOffset, yOffset, scale));
            }
        }
        else if (dims[2] == 84)
        {
            var anchorCount = dims[1];
            for (var i = 0; i < anchorCount; i++)
            {
                var score = outTensor[0, i, 4 + 67];
                if (score < ConfThreshold)
                {
                    continue;
                }

                var cx = outTensor[0, i, 0];
                var cy = outTensor[0, i, 1];
                var bw = outTensor[0, i, 2];
                var bh = outTensor[0, i, 3];
                candidates.Add(CreateCandidate(cx, cy, bw, bh, score, width, height, xOffset, yOffset, scale));
            }
        }
        else
        {
            return false;
        }

        var kept = ApplyNms(candidates, NmsThreshold);
        return kept.Count > 0;
    }

    private static Candidate CreateCandidate(
        float cx,
        float cy,
        float bw,
        float bh,
        float score,
        int frameWidth,
        int frameHeight,
        int xOffset,
        int yOffset,
        float scale)
    {
        var x1 = (cx - bw / 2f - xOffset) / scale;
        var y1 = (cy - bh / 2f - yOffset) / scale;
        var x2 = (cx + bw / 2f - xOffset) / scale;
        var y2 = (cy + bh / 2f - yOffset) / scale;

        x1 = Math.Clamp(x1, 0, frameWidth);
        x2 = Math.Clamp(x2, 0, frameWidth);
        y1 = Math.Clamp(y1, 0, frameHeight);
        y2 = Math.Clamp(y2, 0, frameHeight);

        return new Candidate(x1, y1, x2, y2, score);
    }

    private static List<Candidate> ApplyNms(List<Candidate> candidates, float iouThreshold)
    {
        var selected = new List<Candidate>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Score))
        {
            var keep = true;
            foreach (var picked in selected)
            {
                if (ComputeIoU(candidate, picked) > iouThreshold)
                {
                    keep = false;
                    break;
                }
            }

            if (keep)
            {
                selected.Add(candidate);
            }
        }

        return selected;
    }

    private static float ComputeIoU(Candidate a, Candidate b)
    {
        var x1 = Math.Max(a.X1, b.X1);
        var y1 = Math.Max(a.Y1, b.Y1);
        var x2 = Math.Min(a.X2, b.X2);
        var y2 = Math.Min(a.Y2, b.Y2);

        var w = Math.Max(0, x2 - x1);
        var h = Math.Max(0, y2 - y1);
        var intersection = w * h;
        if (intersection <= 0)
        {
            return 0f;
        }

        var areaA = Math.Max(0, a.X2 - a.X1) * Math.Max(0, a.Y2 - a.Y1);
        var areaB = Math.Max(0, b.X2 - b.X1) * Math.Max(0, b.Y2 - b.Y1);
        var union = areaA + areaB - intersection;
        if (union <= 0)
        {
            return 0f;
        }

        return intersection / union;
    }

    private void SetPhoneDetected(bool detected)
    {
        if (_phoneDetected == detected)
        {
            return;
        }

        _phoneDetected = detected;
        PhoneDetectionChanged?.Invoke(this, detected);
    }

    private void CleanupCamera()
    {
        lock (_sync)
        {
            if (_capture == null)
            {
                return;
            }

            try
            {
                if (_capture.IsOpened())
                {
                    _capture.Release();
                }
            }
            catch
            {
            }
            finally
            {
                _capture.Dispose();
                _capture = null;
            }
        }
    }

    private void CleanupModel()
    {
        _session?.Dispose();
        _session = null;
        _inputName = null;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private readonly record struct Candidate(float X1, float Y1, float X2, float Y2, float Score);
}
