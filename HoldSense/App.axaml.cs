using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using _HoldSense.Models;
using _HoldSense.Services;
using _HoldSense.ViewModels;
using _HoldSense.Views;
using System;
using System.Threading.Tasks;
using Avalonia.Styling;

namespace _HoldSense;

public partial class App : Application
{
    private TrayIconService? _trayIconService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Initialize services first
            var configService = new ConfigService();
            var bluetoothService = new BluetoothService();
            var pythonService = new PythonProcessService(configService);

            // Initialize system tray
            _trayIconService = new TrayIconService(desktop, pythonService, configService, bluetoothService);
            _trayIconService.Initialize();

            // Ensure python is stopped on app exit as a fallback
            desktop.Exit += async (_, __) =>
            {
                try
                {
                    if (pythonService.IsRunning)
                    {
                        await pythonService.StopAsync();
                    }
                }
                catch { }
            };

            // Check if device is already configured
            Task.Run(async () =>
            {
                try
                {
                    var config = await configService.LoadConfigAsync();

                    // Apply theme as early as possible based on config
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (Application.Current != null)
                        {
                            var theme = config?.Theme?.Trim().ToLowerInvariant();
                            Application.Current.RequestedThemeVariant = theme switch
                            {
                                "light" => ThemeVariant.Light,
                                "dark" => ThemeVariant.Dark,
                                _ => ThemeVariant.Default // Auto: follow system
                            };
                        }
                    });
                    
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        if (config == null || string.IsNullOrEmpty(config.PhoneBtAddress))
                        {
                            // Show device selector first as the main window
                            var selectorViewModel = new DeviceSelectorViewModel(bluetoothService, configService);
                            await selectorViewModel.InitializeAsync();

                            var selectorWindow = new DeviceSelectorWindow(selectorViewModel);
                            
                            // Set selector as main window temporarily
                            desktop.MainWindow = selectorWindow;
                            selectorWindow.Show();
                            
                            // Wait for device selection via event
                            BluetoothDevice? selectedDevice = null;
                            var tcs = new TaskCompletionSource<BluetoothDevice?>();
                            
                            selectorViewModel.DeviceSelected += (sender, device) =>
                            {
                                if (!tcs.Task.IsCompleted)
                                {
                                    tcs.SetResult(device);
                                }
                            };
                            
                            // Handle window closing as fallback (user closes without selecting)
                            selectorWindow.Closing += (sender, e) =>
                            {
                                if (!tcs.Task.IsCompleted)
                                {
                                    tcs.SetResult(null); // Treat as skip
                                }
                            };
                            
                            // Wait for selection
                            selectedDevice = await tcs.Task;
                            
                            // Close selector window if not already closed
                            try
                            {
                                selectorWindow.Close();
                            }
                            catch
                            {
                                // Window already closed, ignore
                            }
                            
                            // After device selection (or skip), show main window
                            ShowMainWindow(configService, bluetoothService, pythonService, desktop);
                        }
                        else
                        {
                            // Device already configured, show main window directly
                            ShowMainWindow(configService, bluetoothService, pythonService, desktop);
                        }
                    });
                }
                catch (Exception)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // Show main window even on error
                        ShowMainWindow(configService, bluetoothService, pythonService, desktop);
                    });
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow(
        ConfigService configService,
        BluetoothService bluetoothService,
        PythonProcessService pythonService,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var mainViewModel = new MainWindowViewModel(configService, pythonService);
        desktop.MainWindow = new MainWindow(mainViewModel, configService, bluetoothService, pythonService);
        desktop.MainWindow.Show();
    }
}