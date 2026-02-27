using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using _HoldSense.Services;
using _HoldSense.ViewModels;
using _HoldSense.Views;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.UI;

namespace _HoldSense;

internal sealed class MainWindowWinUI : Window
{
    // ── Services & ViewModel ────────────────────────────────
    private readonly MainWindowViewModel _viewModel;
    private readonly ConfigService _configService;
    private readonly BluetoothService _bluetoothService;
    private readonly IRuntimeService _runtimeService;
    private readonly WebcamService _webcamService;

    // ── Root ────────────────────────────────────────────────
    private Grid _root = null!;

    // ── Status area ─────────────────────────────────────────
    private Border _statusPill = null!;
    private TextBlock _statusPillText = null!;
    private Ellipse _statusDot = null!;

    // ── Device row ──────────────────────────────────────────
    private TextBlock _deviceName = null!;
    private TextBlock _deviceMac = null!;
    private FontIcon _deviceIcon = null!;

    // ── Primary action button ────────────────────────────────
    private Button _primaryButton = null!;
    private TextBlock _primaryButtonLabel = null!;
    private FontIcon _primaryButtonIcon = null!;

    // ── Auto detection toggle row ────────────────────────────
    private ToggleSwitch _autoSwitch = null!;
    private TextBlock _autoSwitchSubtitle = null!;
    private bool _suppressAutoToggle;

    // ── Info tiles ───────────────────────────────────────────
    private TextBlock _audioTileValue = null!;
    private TextBlock _detectionTileValue = null!;
    private Border _audioTileIndicator = null!;
    private Border _detectionTileIndicator = null!;

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

        // Mica backdrop
        try { SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base }; }
        catch (Exception ex) { Program.LogException("MainWindowWinUI.MicaBackdrop", ex); }

        ExtendsContentIntoTitleBar = true;
        ConfigureWindowChrome();

        // Build UI
        var titleBar = BuildTitleBar();
        var content = BuildContent();

        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(titleBar, 0);
        _root.Children.Add(titleBar);
        Grid.SetRow(content, 1);
        _root.Children.Add(content);

        Content = _root;
        SetTitleBar(titleBar);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(480, 680));

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += async (_, __) => await StopIfRunningAsync();
        UpdateUi();
    }

    // ════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════

    public void CloseForExit() => Close();

    public void BringToFront()
    {
        try
        {
            if (AppWindow?.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized)
                p.Restore();
            AppWindow?.Show();
        }
        catch { }
        Activate();
    }

    public async Task OpenSettingsAsync()
    {
        try
        {
            var xamlRoot = await GetXamlRootAsync();
            var vm = new SettingsViewModel(_configService, _bluetoothService, _webcamService, _runtimeService);
            await SettingsDialog.ShowAsync(xamlRoot, vm, _root.ActualTheme);
            await _viewModel.InitializeAsync();
            await ApplyThemeFromConfigAsync();
        }
        catch (Exception ex)
        {
            Program.LogException("MainWindowWinUI.OpenSettingsAsync", ex);
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
            "dark"  => ElementTheme.Dark,
            _       => ElementTheme.Default
        };
    }

    // ════════════════════════════════════════════════════════
    //  TITLE BAR
    // ════════════════════════════════════════════════════════

    private Grid BuildTitleBar()
    {
        var bar = new Grid
        {
            Height = 48,
            Padding = new Thickness(16, 0, 12, 0)
        };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // icon
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // title
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // spacer
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // settings btn

        // App icon
        var iconBorder = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = "\uE767",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconBorder, 0);
        bar.Children.Add(iconBorder);

        // App title
        var titleText = new TextBlock
        {
            Text = "HoldSense",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.9
        };
        Grid.SetColumn(titleText, 1);
        bar.Children.Add(titleText);

        // Settings button (top-right, not draggable)
        var settingsBtn = new Button
        {
            Height = 32,
            Width = 32,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = "\uE713", FontSize = 14 }
        };
        // Make it "flat" style
        settingsBtn.Style = TryGetStyle("NavigationBackButtonNormalStyle") ?? settingsBtn.Style;
        settingsBtn.Click += async (_, __) => await OpenSettingsAsync();
        Grid.SetColumn(settingsBtn, 3);
        bar.Children.Add(settingsBtn);

        return bar;
    }

    // ════════════════════════════════════════════════════════
    //  MAIN CONTENT
    // ════════════════════════════════════════════════════════

    private ScrollViewer BuildContent()
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            Padding = new Thickness(20, 16, 20, 24)
        };

        panel.Children.Add(BuildHeaderSection());
        panel.Children.Add(BuildDeviceCard());
        panel.Children.Add(BuildPrimaryButton());
        panel.Children.Add(BuildInfoTilesRow());
        panel.Children.Add(BuildAutoDetectionCard());
        panel.Children.Add(BuildShortcutsCard());

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    // ── Header: App name + status pill ──────────────────────
    private UIElement BuildHeaderSection()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Spacing = 2 };
        titleStack.Children.Add(new TextBlock
        {
            Text = "HoldSense",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Bluetooth audio for your phone on your PC",
            FontSize = 12,
            Opacity = 0.6
        });
        Grid.SetColumn(titleStack, 0);
        row.Children.Add(titleStack);

        // Status pill
        _statusDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = Brush("SystemFillColorNeutralBrush")
        };

        _statusPillText = new TextBlock
        {
            Text = "Stopped",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var pillContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        pillContent.Children.Add(_statusDot);
        pillContent.Children.Add(_statusPillText);

        _statusPill = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12, 6, 12, 6),
            Background = Brush("ControlAltFillColorSecondaryBrush"),
            BorderBrush = Brush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = pillContent
        };
        Grid.SetColumn(_statusPill, 1);
        row.Children.Add(_statusPill);

        return row;
    }

    // ── Device card ─────────────────────────────────────────
    private Border BuildDeviceCard()
    {
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // BT icon tile
        var iconTile = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = Brush("ControlAltFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        _deviceIcon = new FontIcon
        {
            Glyph = "\uE702",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconTile.Child = _deviceIcon;
        Grid.SetColumn(iconTile, 0);
        row.Children.Add(iconTile);

        // Text
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        _deviceName = new TextBlock
        {
            Text = "No device selected",
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _deviceMac = new TextBlock
        {
            Text = "Open Settings to pair a device",
            FontSize = 12,
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        textStack.Children.Add(_deviceName);
        textStack.Children.Add(_deviceMac);
        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        // Change device button (subtle)
        var changeBtn = new HyperlinkButton
        {
            Content = "Change",
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 4, 8, 4)
        };
        changeBtn.Click += async (_, __) => await OpenSettingsAsync();
        Grid.SetColumn(changeBtn, 2);
        row.Children.Add(changeBtn);

        return Card(row, new Thickness(14, 12, 14, 12));
    }

    // ── Primary listen button ────────────────────────────────
    private Border BuildPrimaryButton()
    {
        var contentStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _primaryButtonIcon = new FontIcon { Glyph = "\uE768", FontSize = 16 };
        _primaryButtonLabel = new TextBlock
        {
            Text = "Start Listening",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        contentStack.Children.Add(_primaryButtonIcon);
        contentStack.Children.Add(_primaryButtonLabel);

        _primaryButton = new Button
        {
            Content = contentStack,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 52,
            Style = TryGetStyle("AccentButtonStyle")
        };
        _primaryButton.Click += async (_, __) =>
        {
            if (_viewModel.IsDetectionRunning)
                await _viewModel.StopDetectionCommand.ExecuteAsync(null);
            else
                await _viewModel.StartDetectionCommand.ExecuteAsync(null);
        };

        // Wrap in a padding-only container so the card doesn't add extra corner radius nesting
        return Card(_primaryButton, new Thickness(0));
    }

    // ── Audio / Detection info tiles ─────────────────────────
    private Grid BuildInfoTilesRow()
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Audio tile
        var audioTile = BuildInfoTile(
            "\uE767",
            "Audio",
            "Disconnected",
            out _audioTileValue,
            out _audioTileIndicator);
        Grid.SetColumn(audioTile, 0);
        grid.Children.Add(audioTile);

        // Detection tile
        var detTile = BuildInfoTile(
            "\uE714",
            "Detection",
            "Stopped",
            out _detectionTileValue,
            out _detectionTileIndicator);
        Grid.SetColumn(detTile, 1);
        grid.Children.Add(detTile);

        return grid;
    }

    private Border BuildInfoTile(string glyph, string label, string initialValue,
        out TextBlock valueText, out Border indicator)
    {
        var body = new StackPanel { Spacing = 10 };

        // Header row
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Opacity = 0.7 });
        header.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center
        });
        body.Children.Add(header);

        valueText = new TextBlock
        {
            Text = initialValue,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        };
        body.Children.Add(valueText);

        // Bottom accent line
        indicator = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = Brush("SystemFillColorNeutralBrush"),
            Opacity = 0.4
        };
        body.Children.Add(indicator);

        return Card(body, new Thickness(14, 12, 14, 12));
    }

    // ── Auto detection card ──────────────────────────────────
    private Border BuildAutoDetectionCard()
    {
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Camera icon tile
        var iconTile = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = Brush("ControlAltFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = "\uE8B8",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconTile, 0);
        row.Children.Add(iconTile);

        // Text
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        textStack.Children.Add(new TextBlock
        {
            Text = "Auto Detection",
            FontWeight = FontWeights.SemiBold
        });
        _autoSwitchSubtitle = new TextBlock
        {
            Text = "Detects phone via webcam automatically",
            FontSize = 12,
            Opacity = 0.6
        };
        textStack.Children.Add(_autoSwitchSubtitle);
        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        // Toggle switch (no header, just the switch)
        _autoSwitch = new ToggleSwitch
        {
            OffContent = "",
            OnContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        _autoSwitch.Toggled += async (_, __) =>
        {
            if (_suppressAutoToggle) return;
            await _viewModel.ToggleAutoDetectionCommand.ExecuteAsync(null);
        };
        Grid.SetColumn(_autoSwitch, 2);
        row.Children.Add(_autoSwitch);

        return Card(row, new Thickness(14, 12, 14, 12));
    }

    // ── Keyboard shortcuts card ──────────────────────────────
    private Border BuildShortcutsCard()
    {
        var body = new Grid { ColumnSpacing = 12 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new FontIcon
        {
            Glyph = "\uE765",
            FontSize = 14,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        body.Children.Add(icon);

        var shortcuts = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        shortcuts.Children.Add(MakeShortcutRow("Ctrl + Alt + C", "Toggle audio connection"));
        shortcuts.Children.Add(MakeShortcutRow("Ctrl + Alt + W", "Toggle detection"));
        Grid.SetColumn(shortcuts, 1);
        body.Children.Add(shortcuts);

        return Card(body, new Thickness(14, 12, 14, 12));
    }

    private static Grid MakeShortcutRow(string keys, string description)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyText = new TextBlock
        {
            Text = keys,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Cascadia Code, monospace"),
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.85
        };
        Grid.SetColumn(keyText, 0);
        row.Children.Add(keyText);

        var descText = new TextBlock
        {
            Text = description,
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(descText, 1);
        row.Children.Add(descText);

        return row;
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
        bool audioOn = _viewModel.IsAudioConnected;
        bool autoOn  = _viewModel.DetectionEnabled;

        // ── Status pill ──────────────────────────────────────
        if (_statusPillText != null && _statusDot != null && _statusPill != null)
        {
            var activeBrush  = Brush("SystemFillColorSuccessBrush");
            var neutralBrush = Brush("SystemFillColorNeutralBrush");

            _statusDot.Fill = running ? activeBrush : neutralBrush;
            _statusPillText.Text = running ? "Active" : "Stopped";
            _statusPill.Background = running
                ? TintBrush(Colors.Green, 0.08f)
                : Brush("ControlAltFillColorSecondaryBrush");
            _statusPill.BorderBrush = running ? activeBrush : Brush("CardStrokeColorDefaultBrush");
        }

        // ── Device card ──────────────────────────────────────
        bool hasDevice = _viewModel.SelectedDeviceName != "Not configured"
                         && !string.IsNullOrWhiteSpace(_viewModel.SelectedDeviceName);

        if (_deviceName != null)
            _deviceName.Text = hasDevice ? _viewModel.SelectedDeviceName : "No device selected";
        if (_deviceMac != null)
            _deviceMac.Text = hasDevice ? "Bluetooth Audio · A2DP" : "Open Settings to configure";

        // ── Primary button ───────────────────────────────────
        if (_primaryButtonLabel != null)
            _primaryButtonLabel.Text = running ? "Stop Listening" : "Start Listening";
        if (_primaryButtonIcon != null)
            _primaryButtonIcon.Glyph = running ? "\uE769" : "\uE768";

        // ── Info tiles ───────────────────────────────────────
        if (_audioTileValue != null)
        {
            _audioTileValue.Text = audioOn ? "Connected" : "Disconnected";
            _audioTileValue.Opacity = audioOn ? 1.0 : 0.6;
        }
        if (_audioTileIndicator != null)
        {
            _audioTileIndicator.Background = audioOn
                ? Brush("SystemFillColorSuccessBrush")
                : Brush("SystemFillColorNeutralBrush");
            _audioTileIndicator.Opacity = audioOn ? 0.9 : 0.3;
        }

        if (_detectionTileValue != null)
        {
            _detectionTileValue.Text = !running ? "Stopped" : autoOn ? "Auto" : "Keybind";
            _detectionTileValue.Opacity = running ? 1.0 : 0.6;
        }
        if (_detectionTileIndicator != null)
        {
            _detectionTileIndicator.Background = running
                ? Brush("SystemFillColorSuccessBrush")
                : Brush("SystemFillColorNeutralBrush");
            _detectionTileIndicator.Opacity = running ? 0.9 : 0.3;
        }

        // ── Auto detection toggle ────────────────────────────
        if (_autoSwitch != null)
        {
            _suppressAutoToggle = true;
            _autoSwitch.IsOn = autoOn;
            _autoSwitch.IsEnabled = running && _viewModel.AutoDetectionAvailable;
            _suppressAutoToggle = false;
        }
        if (_autoSwitchSubtitle != null)
        {
            _autoSwitchSubtitle.Text = !_viewModel.AutoDetectionAvailable
                ? "Model not downloaded — see Settings"
                : autoOn ? "Webcam active, watching for your phone"
                         : "Enable to use webcam-based detection";
        }
    }

    // ════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════

    private void ConfigureWindowChrome()
    {
        if (AppWindow?.TitleBar is not AppWindowTitleBar titleBar) return;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 128, 128, 128);
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(50, 128, 128, 128);
    }

    private static Border Card(UIElement child, Thickness? padding = null)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = padding ?? new Thickness(14),
            Background = Brush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Brush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Child = child
        };
    }

    private static Brush Brush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush b)
            return b;
        return new SolidColorBrush(Colors.Transparent);
    }

    private static SolidColorBrush TintBrush(Color tint, float alpha)
    {
        return new SolidColorBrush(Color.FromArgb(
            (byte)(alpha * 255),
            tint.R, tint.G, tint.B));
    }

    private static Style? TryGetStyle(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Style s)
            return s;
        return null;
    }

    private async Task StopIfRunningAsync()
    {
        if (_runtimeService.IsRunning)
            try { await _runtimeService.StopAsync(); } catch { }
    }

    private async Task<XamlRoot> GetXamlRootAsync()
    {
        while (Content.XamlRoot == null)
            await Task.Delay(50);
        return Content.XamlRoot;
    }
}
