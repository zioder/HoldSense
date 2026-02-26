using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using _HoldSense.Services;
using _HoldSense.ViewModels;
using System;
using System.Runtime.Versioning;
using Windows.UI;
using MessageBox = System.Windows.Forms.MessageBox;
using MessageBoxButtons = System.Windows.Forms.MessageBoxButtons;
using MessageBoxIcon = System.Windows.Forms.MessageBoxIcon;

namespace _HoldSense;

 [SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WinUIApplication : Application
{
    private MainWindowWinUI? _mainWindow;
    private IRuntimeService? _runtimeService;
    private TrayIconServiceWinUI? _trayIconService;

    public WinUIApplication()
    {
        UnhandledException += OnUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            EnsureResourceFallbacks();

            var configService = new ConfigService();
            var bluetoothService = new BluetoothService();
            _runtimeService = new AppRuntimeOrchestrator(configService);
            var viewModel = new MainWindowViewModel(configService, _runtimeService);

            await viewModel.InitializeAsync();

            _mainWindow = new MainWindowWinUI(viewModel, configService, bluetoothService, _runtimeService);
            _mainWindow.Closed += (_, __) => _trayIconService?.Dispose();
            _mainWindow.Activate();
            await _mainWindow.ApplyThemeFromConfigAsync();

            var config = await configService.LoadConfigAsync();
            if (config == null || string.IsNullOrWhiteSpace(config.PhoneBtAddress))
            {
                // Do not block startup with a modal dialog; keep the window interactive.
                viewModel.StatusMessage = "No Bluetooth device configured. Open Settings to select one.";
            }

            _trayIconService = new TrayIconServiceWinUI(
                _mainWindow,
                viewModel,
                configService,
                bluetoothService,
                _runtimeService,
                System.Threading.SynchronizationContext.Current ?? new System.Threading.SynchronizationContext());
            await _trayIconService.InitializeAsync();
            _mainWindow.BringToFront();
        }
        catch (Exception ex)
        {
            Program.LogException("WinUIApplication.OnLaunched", ex);
            MessageBox.Show(
                "HoldSense failed during startup. Check startup-error.log next to HoldSense.exe for details.",
                "HoldSense Startup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Current.Exit();
        }
    }

    private void EnsureResourceFallbacks()
    {
        var resources = Resources ??= new ResourceDictionary();
        AddBrushFallback(resources, "TabViewScrollButtonBackground", Colors.Transparent);
        AddBrushFallback(resources, "TabViewScrollButtonForeground", Colors.Transparent);
        AddBrushFallback(resources, "TabViewScrollButtonBorderBrush", Colors.Transparent);
        AddBrushFallback(resources, "TabViewButtonBackground", Colors.Transparent);
        AddBrushFallback(resources, "TabViewButtonBackgroundPointerOver", Colors.Transparent);
        AddBrushFallback(resources, "TabViewButtonBackgroundPressed", Colors.Transparent);
        AddBrushFallback(resources, "TabViewButtonBorderBrush", Colors.Transparent);
        AddBrushFallback(resources, "TabViewButtonBorderBrushPointerOver", Colors.Transparent);
        AddBrushFallback(resources, "TabViewButtonBorderBrushPressed", Colors.Transparent);
        AddBrushFallback(resources, "TabViewButtonForeground", Colors.Transparent);
        AddBrushFallback(resources, "TabViewButtonForegroundPointerOver", Colors.Transparent);
        AddBrushFallback(resources, "TabViewButtonForegroundPressed", Colors.Transparent);
        AddBrushFallback(resources, "ProgressBarForeground", Colors.Transparent);
        AddBrushFallback(resources, "ProgressBarBackground", Colors.Transparent);
        AddBrushFallback(resources, "ProgressBarBorderBrush", Colors.Transparent);
        AddBrushFallback(resources, "ProgressBarPausedForeground", Colors.Transparent);
        AddBrushFallback(resources, "ProgressBarErrorForeground", Colors.Transparent);
        AddBrushFallback(resources, "ProgressBarIndeterminateForeground", Colors.Transparent);

        AddDoubleFallback(resources, "TabViewItemScrollButonFontSize", 12);
        AddDoubleFallback(resources, "TabViewItemScrollButtonFontSize", 12);
        AddDoubleFallback(resources, "TabViewItemAddButtonFontSize", 12);
        AddDoubleFallback(resources, "TabViewItemAddButtonWidth", 32);
        AddDoubleFallback(resources, "TabViewItemAddButtonHeight", 32);
        AddDoubleFallback(resources, "TabViewItemScrollButtonWidth", 32);
        AddDoubleFallback(resources, "TabViewItemScrollButtonHeight", 32);

        AddThicknessFallback(resources, "TabViewButtonBorderThickness", 1);
        AddThicknessFallback(resources, "TabViewButtonPadding", 0);
        AddThicknessFallback(resources, "TabViewItemAddButtonMargin", 0);
        AddThicknessFallback(resources, "TabViewItemAddButtonPadding", 0);
        AddThicknessFallback(resources, "NumberBoxSpinButtonBorderThickness", 1);
        AddThicknessFallback(resources, "NumberBoxPopupSpinButtonBorderThickness", 1);
        AddThicknessFallback(resources, "ProgressBarBorderThemeThickness", 1);
        AddThicknessFallback(resources, "ProgressBarPadding", 0);
        AddThicknessFallback(resources, "ProgressBarTrackThickness", 1);
        AddDoubleFallback(resources, "ProgressBarMinHeight", 4);
        AddDoubleFallback(resources, "ProgressBarMinWidth", 100);
        AddDoubleFallback(resources, "ProgressBarIndicatorLength", 60);
        AddDoubleFallback(resources, "ProgressBarTrackMinHeight", 4);
        AddDoubleFallback(resources, "ProgressBarIndicatorMinHeight", 4);
        AddDoubleFallback(resources, "ProgressBarTrackHeight", 4);
        AddCornerRadiusFallback(resources, "ControlCornerRadius", 4);
        AddCornerRadiusFallback(resources, "OverlayCornerRadius", 8);
        AddCornerRadiusFallback(resources, "ProgressBarCornerRadius", 2);
        AddCornerRadiusFallback(resources, "ProgressBarTrackCornerRadius", 2);
        AddStyleFallback(resources, "DefaultRepeatButtonStyle", typeof(RepeatButton));
    }

    private static void AddBrushFallback(ResourceDictionary resources, string key, Color color)
    {
        if (!resources.ContainsKey(key))
            resources[key] = new SolidColorBrush(color);
    }

    private static void AddDoubleFallback(ResourceDictionary resources, string key, double value)
    {
        if (!resources.ContainsKey(key))
            resources[key] = value;
    }

    private static void AddThicknessFallback(ResourceDictionary resources, string key, double uniform)
    {
        if (!resources.ContainsKey(key))
            resources[key] = new Thickness(uniform);
    }

    private static void AddCornerRadiusFallback(ResourceDictionary resources, string key, double radius)
    {
        if (!resources.ContainsKey(key))
            resources[key] = new CornerRadius(radius);
    }

    private static void AddStyleFallback(ResourceDictionary resources, string key, Type targetType)
    {
        if (!resources.ContainsKey(key))
            resources[key] = new Style { TargetType = targetType };
    }

    private async void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        Program.LogException("WinUIApplication.UnhandledException", args.Exception);
        _trayIconService?.Dispose();

        if (_runtimeService != null && _runtimeService.IsRunning)
        {
            try
            {
                await _runtimeService.StopAsync();
            }
            catch
            {
            }
        }

        args.Handled = true;
    }
}
