using Avalonia.Controls;
using _HoldSense.ViewModels;
using _HoldSense.Views;
using _HoldSense.Services;
using System.Threading.Tasks;

namespace _HoldSense;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel? _viewModel;
    private readonly ConfigService? _configService;
    private readonly BluetoothService? _bluetoothService;
    private readonly PythonProcessService? _pythonService;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel, ConfigService configService, BluetoothService bluetoothService, PythonProcessService pythonService) : this()
    {
        _viewModel = viewModel;
        _configService = configService;
        _bluetoothService = bluetoothService;
        _pythonService = pythonService;
        
        DataContext = viewModel;

        _viewModel.SettingsRequested += OnSettingsRequested;

        // Handle close event - minimize to tray instead
        Closing += OnClosing;

        // Initialize the view model
        Task.Run(async () => await _viewModel.InitializeAsync());
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Prevent actual closing, just hide the window
        e.Cancel = true;
        Hide();
    }

    private async void OnSettingsRequested(object? sender, System.EventArgs e)
    {
        var webcamService = new WebcamService();
        var settingsViewModel = new SettingsViewModel(_configService!, _bluetoothService!, webcamService, _pythonService);
        await settingsViewModel.LoadSettingsAsync();

        var settingsWindow = new SettingsWindow(settingsViewModel);
        
        settingsViewModel.ChangeDeviceRequested += async (s, args) =>
        {
            settingsWindow.Close();
            await ShowDeviceSelectorAsync();
        };

        await settingsWindow.ShowDialog(this);

        // Reload config after settings window closes
        if (_viewModel != null)
        {
            await _viewModel.InitializeAsync();
        }
    }

    private async Task ShowDeviceSelectorAsync()
    {
        var selectorViewModel = new DeviceSelectorViewModel(_bluetoothService!, _configService!);
        await selectorViewModel.InitializeAsync();

        var selectorWindow = new DeviceSelectorWindow(selectorViewModel);
        await selectorWindow.ShowDialog(this);

        // Reload config after device selection
        if (_viewModel != null)
        {
            await _viewModel.InitializeAsync();
        }
    }
}
