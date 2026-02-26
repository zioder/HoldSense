using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using _HoldSense.Services;
using _HoldSense.ViewModels;
using _HoldSense.Views;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace _HoldSense;

internal sealed class MainWindowWinUI : Window
{
    // ── Services & ViewModel ────────────────────────────────
    private readonly MainWindowViewModel _viewModel;
    private readonly ConfigService _configService;
    private readonly BluetoothService _bluetoothService;
    private readonly IRuntimeService _runtimeService;
    private readonly WebcamService _webcamService;

    // ── UI Elements (updated dynamically) ───────────────────
    private Grid _root = null!;

    // Status card
    private TextBlock _statusLabel = null!;
    private TextBlock _statusDescription = null!;
    private Border _statusDot = null!;
    private Border _accentStrip = null!;
    private Border _activityBar = null!;

    // Quick status cards
    private TextBlock _audioStatusText = null!;
    private TextBlock _detectionStatusText = null!;

    // Device card
    private TextBlock _devicePrimaryText = null!;
    private TextBlock _deviceSecondaryText = null!;

    // Action bar
    private Button _toggleButton = null!;
    private TextBlock _toggleButtonText = null!;
    private FontIcon _toggleButtonIcon = null!;
    private ToggleSwitch _autoDetectionToggle = null!;
    private Border _heroIconTile = null!;
    private bool _suppressAutoToggleEvent;

    public MainWindowWinUI(
        MainWindowViewModel viewModel,
        ConfigService configService,
        BluetoothService bluetoothService,
        IRuntimeService runtimeService)
    {
        _viewModel = viewModel;
        _configService = configService;
        _bluetoothService = bluetoothService;
        _runtimeService = runtimeService;
        _webcamService = new WebcamService();

        Title = "HoldSense";

        // ── Mica Backdrop ───────────────────────────────────
        // Mica can fail on some Windows/runtime combinations; degrade gracefully.
        try
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
        catch (Exception ex)
        {
            Program.LogException("MainWindowWinUI.MicaBackdrop", ex);
        }
        ConfigureWindowChrome();

        // ── Custom Title Bar ────────────────────────────────
        ExtendsContentIntoTitleBar = true;

        // ── Build UI Tree ───────────────────────────────────
        var titleBarArea = BuildTitleBar();
        var mainContent = BuildMainContent();

        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(titleBarArea, 0);
        _root.Children.Add(titleBarArea);

        Grid.SetRow(mainContent, 1);
        _root.Children.Add(mainContent);

        Content = _root;
        SetTitleBar(titleBarArea);

        // ── Window Size ─────────────────────────────────────
        AppWindow.Resize(new Windows.Graphics.SizeInt32(620, 760));

        // ── Events ──────────────────────────────────────────
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += async (_, __) => await StopPythonIfRunningAsync();
        UpdateUi();
    }

    // ════════════════════════════════════════════════════════
    //  PUBLIC API (consumed by TrayIconService & WinUIApplication)
    // ════════════════════════════════════════════════════════

    public void CloseForExit() => Close();

    public void BringToFront()
    {
        try
        {
            if (AppWindow?.Presenter is OverlappedPresenter presenter &&
                presenter.State == OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }

            AppWindow?.Show();
        }
        catch
        {
        }

        Activate();
    }

    public async Task OpenSettingsAsync()
    {
        try
        {
            var xamlRoot = await GetXamlRootAsync();
            var settingsVm = new SettingsViewModel(_configService, _bluetoothService, _webcamService, _runtimeService);
            await SettingsDialog.ShowAsync(xamlRoot, settingsVm, _root.ActualTheme);
            await _viewModel.InitializeAsync();
            await ApplyThemeFromConfigAsync();
        }
        catch (Exception ex)
        {
            Program.LogException("MainWindowWinUI.OpenSettingsAsync", ex);
            _viewModel.StatusMessage = "Settings failed to open. Check startup-error.log for details.";
        }
    }

    public async Task<bool> ShowDeviceSelectorAsync()
    {
        var xamlRoot = await GetXamlRootAsync();
        var vm = new DeviceSelectorViewModel(_bluetoothService, _configService);
        var selected = await DeviceSelectorDialog.ShowAsync(xamlRoot, vm);
        await _viewModel.InitializeAsync();
        return selected != null;
    }

    public async Task ApplyThemeFromConfigAsync()
    {
        var config = await _configService.LoadConfigAsync();
        var theme = config?.Theme?.Trim().ToLowerInvariant();
        _root.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    // ════════════════════════════════════════════════════════
    //  UI CONSTRUCTION
    // ════════════════════════════════════════════════════════

    private Grid BuildTitleBar()
    {
        var bar = new Grid
        {
            Height = 48,
            Padding = new Thickness(12, 0, 12, 0)
        };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconContainer = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = ThemeBrush("ControlAltFillColorSecondaryBrush"),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new FontIcon
            {
                Glyph = "\uE767",
                FontSize = 12,
                Opacity = 0.9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(iconContainer, 0);
        bar.Children.Add(iconContainer);

        var title = new TextBlock
        {
            Text = "HoldSense",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8
        };
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);

        var badge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 2, 8, 2),
            Background = ThemeBrush("ControlAltFillColorSecondaryBrush"),
            Child = new TextBlock
            {
                Text = "WinUI 3",
                FontSize = 11,
                Opacity = 0.8
            }
        };
        Grid.SetColumn(badge, 3);
        bar.Children.Add(badge);

        return bar;
    }

    private StackPanel BuildMainContent()
    {
        var panel = new StackPanel
        {
            Spacing = 14,
            Padding = new Thickness(24, 12, 24, 24)
        };

        panel.Children.Add(BuildHeroCard());
        panel.Children.Add(BuildServiceStatusCard());
        panel.Children.Add(BuildQuickStatusRow());
        panel.Children.Add(BuildDeviceCard());
        panel.Children.Add(BuildActionBar());
        panel.Children.Add(BuildShortcutsCard());

        return panel;
    }

    private Border BuildHeroCard()
    {
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _heroIconTile = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(10),
            Background = ThemeBrush("ControlStrongFillColorDefaultBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new FontIcon
            {
                Glyph = "\uE767",
                FontSize = 18,
                Foreground = ThemeBrush("TextFillColorPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        grid.Children.Add(_heroIconTile);

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = "HoldSense",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold
        });
        text.Children.Add(new TextBlock
        {
            Text = "Pick up your phone and route audio through your PC instantly.",
            Opacity = 0.68,
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        return MakeCard(grid, new Thickness(18));
    }

    private Border BuildShortcutsCard()
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock
        {
            Text = "Keyboard Shortcuts",
            FontWeight = FontWeights.SemiBold
        });
        body.Children.Add(new TextBlock
        {
            Text = "Ctrl+Alt+C toggles audio  ·  Ctrl+Alt+W toggles detection",
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        });
        return MakeCard(body, new Thickness(16, 14, 16, 14));
    }

    /// <summary>
    /// Main status card with accent left-strip indicator, status dot, label and activity bar.
    /// </summary>
    private UIElement BuildServiceStatusCard()
    {
        var outer = new Grid();
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Accent strip on the left edge
        _accentStrip = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(1.5),
            Margin = new Thickness(0, 12, 0, 12),
            Background = ThemeBrush("SystemFillColorNeutralBrush")
        };
        Grid.SetColumn(_accentStrip, 0);
        outer.Children.Add(_accentStrip);

        // Card body
        var body = new StackPanel { Spacing = 6 };

        // Header row with dot + label
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        _statusDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            Background = ThemeBrush("SystemFillColorNeutralBrush")
        };
        headerRow.Children.Add(_statusDot);

        _statusLabel = new TextBlock
        {
            Text = "Stopped",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerRow.Children.Add(_statusLabel);
        body.Children.Add(headerRow);

        // Description
        _statusDescription = new TextBlock
        {
            Text = "Press Start to begin listening in lightweight keybind mode.",
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        };
        body.Children.Add(_statusDescription);

        // Indeterminate progress bar (visible when running)
        _activityBar = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Margin = new Thickness(0, 6, 0, 0),
            Opacity = 0,
            Background = ThemeBrush("SystemFillColorNeutralBrush")
        };
        body.Children.Add(_activityBar);

        var card = MakeCard(body, new Thickness(12, 16, 16, 16));
        Grid.SetColumn(card, 1);
        outer.Children.Add(card);

        return outer;
    }

    /// <summary>
    /// Two side-by-side mini cards: Audio and Detection status.
    /// </summary>
    private Grid BuildQuickStatusRow()
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── Audio Card ──────────────────────────────────────
        var audioBody = new StackPanel { Spacing = 8 };

        var audioHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        audioHeader.Children.Add(new FontIcon
        {
            Glyph = "\uE767",
            FontSize = 16,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center
        });
        audioHeader.Children.Add(new TextBlock
        {
            Text = "Audio",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        audioBody.Children.Add(audioHeader);

        _audioStatusText = new TextBlock
        {
            Text = "Disconnected",
            Opacity = 0.6,
            FontSize = 14
        };
        audioBody.Children.Add(_audioStatusText);

        var audioCard = MakeCard(audioBody);
        Grid.SetColumn(audioCard, 0);
        grid.Children.Add(audioCard);

        // ── Detection Card ──────────────────────────────────
        var detBody = new StackPanel { Spacing = 8 };

        var detHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        detHeader.Children.Add(new FontIcon
        {
            Glyph = "\uE714",
            FontSize = 16,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center
        });
        detHeader.Children.Add(new TextBlock
        {
            Text = "Detection",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        detBody.Children.Add(detHeader);

        _detectionStatusText = new TextBlock
        {
            Text = "Stopped",
            Opacity = 0.6,
            FontSize = 14
        };
        detBody.Children.Add(_detectionStatusText);

        var detCard = MakeCard(detBody);
        Grid.SetColumn(detCard, 1);
        grid.Children.Add(detCard);

        return grid;
    }

    /// <summary>
    /// Bluetooth device info card with icon, name and subtitle.
    /// </summary>
    private Border BuildDeviceCard()
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new FontIcon
        {
            Glyph = "\uE702",
            FontSize = 20,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.75
        };
        Grid.SetColumn(icon, 0);
        content.Children.Add(icon);

        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2
        };

        _devicePrimaryText = new TextBlock
        {
            Text = "No device selected",
            FontWeight = FontWeights.SemiBold
        };
        textStack.Children.Add(_devicePrimaryText);

        _deviceSecondaryText = new TextBlock
        {
            Text = "Open Settings to configure a Bluetooth device",
            Opacity = 0.6,
            FontSize = 12
        };
        textStack.Children.Add(_deviceSecondaryText);

        Grid.SetColumn(textStack, 1);
        content.Children.Add(textStack);

        return MakeCard(content);
    }

    /// <summary>
    /// Action bar with Start/Stop toggle button and Settings button.
    /// </summary>
    private Grid BuildActionBar()
    {
        var grid = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Toggle detection (accent primary button)
        var toggleContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _toggleButtonIcon = new FontIcon { Glyph = "\uE768", FontSize = 14 };
        toggleContent.Children.Add(_toggleButtonIcon);
        _toggleButtonText = new TextBlock { Text = "Start Detection", VerticalAlignment = VerticalAlignment.Center };
        toggleContent.Children.Add(_toggleButtonText);

        _toggleButton = new Button
        {
            Content = toggleContent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 44,
            Style = ThemeStyle("AccentButtonStyle")
        };
        _toggleButton.Click += async (_, __) =>
        {
            if (_viewModel.IsDetectionRunning)
                await _viewModel.StopDetectionCommand.ExecuteAsync(null);
            else
                await _viewModel.StartDetectionCommand.ExecuteAsync(null);
        };
        Grid.SetColumn(_toggleButton, 0);
        grid.Children.Add(_toggleButton);

        _autoDetectionToggle = new ToggleSwitch
        {
            Header = "Auto Detection",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 140
        };
        _autoDetectionToggle.Toggled += async (_, __) =>
        {
            if (_suppressAutoToggleEvent)
                return;
            await _viewModel.ToggleAutoDetectionCommand.ExecuteAsync(null);
        };
        Grid.SetColumn(_autoDetectionToggle, 1);
        grid.Children.Add(_autoDetectionToggle);

        // Settings button
        var settingsContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        settingsContent.Children.Add(new FontIcon { Glyph = "\uE713", FontSize = 14 });
        settingsContent.Children.Add(new TextBlock { Text = "Settings", VerticalAlignment = VerticalAlignment.Center });

        var settingsButton = new Button
        {
            Content = settingsContent,
            Height = 44
        };
        settingsButton.Click += async (_, __) => await OpenSettingsAsync();
        Grid.SetColumn(settingsButton, 2);
        grid.Children.Add(settingsButton);

        return grid;
    }

    // ════════════════════════════════════════════════════════
    //  UI UPDATE
    // ════════════════════════════════════════════════════════

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DispatcherQueue != null)
            DispatcherQueue.TryEnqueue(UpdateUi);
        else
            UpdateUi();
    }

    private void UpdateUi()
    {
        bool running = _viewModel.IsDetectionRunning;

        // ── Status card ─────────────────────────────────────
        var positive = ThemeBrush("SystemFillColorSuccessBrush");
        var neutral = ThemeBrush("SystemFillColorNeutralBrush");

        if (_statusLabel != null && _statusDot != null && _accentStrip != null && _statusDescription != null && _activityBar != null)
        {
            _statusLabel.Text = running ? "Running" : "Stopped";
            _statusDot.Background = running ? positive : neutral;
            _accentStrip.Background = running ? positive : neutral;

            _statusDescription.Text = running
                ? (_viewModel.DetectionEnabled
                    ? "Listening. Auto-detection is active."
                    : "Listening in lightweight keybind mode.")
                : (string.IsNullOrWhiteSpace(_viewModel.StatusMessage) || _viewModel.StatusMessage == "Ready"
                    ? "Press Start to begin listening in lightweight keybind mode."
                    : _viewModel.StatusMessage);

            _activityBar.Opacity = running ? 1 : 0;
            _activityBar.Background = running ? positive : neutral;
        }

        if (_heroIconTile != null)
            _heroIconTile.Background = running ? positive : ThemeBrush("ControlStrongFillColorDefaultBrush");

        // ── Quick status ────────────────────────────────────
        if (_audioStatusText != null)
            _audioStatusText.Text = _viewModel.IsAudioConnected ? "Connected" : "Disconnected";
        if (_detectionStatusText != null)
            _detectionStatusText.Text = running
                ? (_viewModel.DetectionEnabled ? "Auto On" : "Keybind Only")
                : "Stopped";

        // ── Device card ─────────────────────────────────────
        bool hasDevice = _viewModel.SelectedDeviceName != "Not configured"
                         && !string.IsNullOrWhiteSpace(_viewModel.SelectedDeviceName);

        if (_devicePrimaryText != null)
            _devicePrimaryText.Text = hasDevice ? _viewModel.SelectedDeviceName : "No device selected";
        if (_deviceSecondaryText != null)
            _deviceSecondaryText.Text = hasDevice
                ? "Bluetooth audio device"
                : "Open Settings to configure a Bluetooth device";

        // ── Toggle button ───────────────────────────────────
        if (_toggleButtonText != null)
            _toggleButtonText.Text = running ? "Stop Detection" : "Start Detection";
        if (_toggleButtonIcon != null)
            _toggleButtonIcon.Glyph = running ? "\uE769" : "\uE768"; // Pause / Play

        if (_autoDetectionToggle != null)
        {
            _suppressAutoToggleEvent = true;
            _autoDetectionToggle.IsOn = _viewModel.DetectionEnabled;
            _autoDetectionToggle.IsEnabled = running && _viewModel.AutoDetectionAvailable;
            _suppressAutoToggleEvent = false;
        }

    }

    // ════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════

    private void ConfigureWindowChrome()
    {
        if (AppWindow?.TitleBar is not AppWindowTitleBar titleBar)
            return;

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private static Border MakeCard(UIElement child, Thickness? padding = null)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = padding ?? new Thickness(18),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Child = child
        };
    }

    private static Brush ThemeBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
            return brush;
        return new SolidColorBrush(Colors.Transparent);
    }

    private static Style? ThemeStyle(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Style style)
            return style;
        return null;
    }

    private async Task StopPythonIfRunningAsync()
    {
        if (_runtimeService.IsRunning)
        {
            try { await _runtimeService.StopAsync(); }
            catch { }
        }
    }

    private async Task<XamlRoot> GetXamlRootAsync()
    {
        while (Content.XamlRoot == null)
            await Task.Delay(50);
        return Content.XamlRoot;
    }
}
