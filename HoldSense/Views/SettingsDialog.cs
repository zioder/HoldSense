using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using _HoldSense.Models;
using _HoldSense.Services;
using _HoldSense.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace _HoldSense.Views;

/// <summary>
/// Settings dialog styled after Windows 11 Settings / PowerToys — grouped setting rows
/// with icon, title, description and control, separated by thin dividers.
/// </summary>
internal sealed class SettingsDialog : ContentDialog
{
    private readonly SettingsViewModel _viewModel;

    // ── Device section ──────────────────────────────────────
    private readonly TextBlock _deviceNameLabel;
    private readonly TextBlock _deviceMacLabel;
    private readonly ComboBox _deviceCombo;
    private readonly Button _refreshBtn;

    // ── Preferences ─────────────────────────────────────────
    private readonly ToggleSwitch _keybindSwitch;
    private readonly ComboBox _themeCombo;

    // ── Auto detection ───────────────────────────────────────
    private readonly Border _statusBadge;
    private readonly TextBlock _statusBadgeText;
    private readonly TextBlock _modelInfoText;
    private readonly StackPanel _downloadProgressPanel;
    private readonly TextBlock _downloadProgressText;
    private readonly TextBlock _downloadStatusText;
    private readonly Button _downloadBtn;
    private readonly Button _cancelBtn;
    private readonly Button _removeBtn;
    private readonly ComboBox _webcamCombo;

    // ── Footer ───────────────────────────────────────────────
    private readonly TextBlock _footerStatus;

    private bool _syncing;
    private bool _closed;

    // ════════════════════════════════════════════════════════
    //  CONSTRUCTOR
    // ════════════════════════════════════════════════════════

    private SettingsDialog(XamlRoot xamlRoot, SettingsViewModel viewModel, ElementTheme theme)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnVmChanged;

        XamlRoot = xamlRoot;
        RequestedTheme = theme;
        Title = "Settings";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        MinWidth = 480;

        // ── Allocate controls ───────────────────────────────
        _deviceNameLabel = new TextBlock { FontWeight = FontWeights.SemiBold };
        _deviceMacLabel  = new TextBlock { FontSize = 12, Opacity = 0.6 };

        _deviceCombo = new ComboBox
        {
            Width = 260,
            DisplayMemberPath = "DisplayName",
            PlaceholderText = "Choose a device…"
        };
        _deviceCombo.ItemsSource = _viewModel.AvailableBluetoothDevices;
        _deviceCombo.SelectionChanged += (_, __) =>
        {
            if (!_syncing && _deviceCombo.SelectedItem is BluetoothDevice dev)
                _viewModel.SelectedBluetoothDevice = dev;
        };

        _refreshBtn = new Button { Content = "Refresh", Height = 32 };
        _refreshBtn.Click += async (_, __) => await _viewModel.RefreshBluetoothDevicesCommand.ExecuteAsync(null);

        _keybindSwitch = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 };
        _keybindSwitch.Toggled += (_, __) =>
        {
            if (!_syncing) _viewModel.KeybindEnabled = _keybindSwitch.IsOn;
        };

        _themeCombo = new ComboBox { MinWidth = 120 };
        _themeCombo.SelectionChanged += (_, __) =>
        {
            if (!_syncing && _themeCombo.SelectedItem is string t) _viewModel.SelectedTheme = t;
        };

        _webcamCombo = new ComboBox { Width = 200, DisplayMemberPath = "Name" };
        _webcamCombo.SelectionChanged += (_, __) =>
        {
            if (!_syncing && _webcamCombo.SelectedItem is WebcamDevice w) _viewModel.SelectedWebcam = w;
        };

        _statusBadgeText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold };
        _statusBadge = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 4),
            Child = _statusBadgeText,
            VerticalAlignment = VerticalAlignment.Center
        };

        _modelInfoText = new TextBlock { FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap };

        _downloadProgressText = new TextBlock { FontSize = 12, Opacity = 0.7 };
        _downloadStatusText   = new TextBlock { FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap };
        _downloadProgressPanel = new StackPanel
        {
            Spacing = 4,
            Visibility = Visibility.Collapsed
        };
        _downloadProgressPanel.Children.Add(_downloadProgressText);
        _downloadProgressPanel.Children.Add(new ProgressBar
        {
            Height = 3,
            IsIndeterminate = false,
            Minimum = 0,
            Maximum = 100
        });
        _downloadProgressPanel.Children.Add(_downloadStatusText);

        _downloadBtn = new Button { Content = "Download (~13 MB)" };
        _cancelBtn   = new Button { Content = "Cancel" };
        _removeBtn   = new Button { Content = "Remove" };

        _downloadBtn.Click += async (_, __) => await _viewModel.DownloadAutoDetectionCommand.ExecuteAsync(null);
        _cancelBtn.Click   += (_, __) => _viewModel.CancelDownloadCommand.Execute(null);
        _removeBtn.Click   += async (_, __) => await _viewModel.RemoveAutoDetectionCommand.ExecuteAsync(null);

        _footerStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7
        };

        Content = new ScrollViewer
        {
            MaxHeight = 580,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildLayout()
        };

        PrimaryButtonClick += OnSave;
        Closed += (_, __) => { _closed = true; _viewModel.PropertyChanged -= OnVmChanged; };
    }

    // ════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════

    public static async Task ShowAsync(XamlRoot xamlRoot, SettingsViewModel viewModel, ElementTheme theme)
    {
        await viewModel.LoadSettingsAsync();
        var dlg = new SettingsDialog(xamlRoot, viewModel, theme);
        dlg.Sync();
        await ShowWithRetry(dlg);
    }

    // ════════════════════════════════════════════════════════
    //  LAYOUT
    // ════════════════════════════════════════════════════════

    private StackPanel BuildLayout()
    {
        var root = new StackPanel { Spacing = 0, MaxWidth = 520 };

        // ── Bluetooth Device ─────────────────────────────────
        root.Children.Add(SectionHeader("Bluetooth Device"));
        root.Children.Add(BuildDeviceGroup());

        // ── Preferences ──────────────────────────────────────
        root.Children.Add(SectionHeader("Preferences"));
        root.Children.Add(BuildPreferencesGroup());

        // ── Phone Detection ───────────────────────────────────
        root.Children.Add(SectionHeader("Phone Detection"));
        root.Children.Add(BuildDetectionGroup());

        root.Children.Add(new Border { Height = 8 }); // bottom spacing
        root.Children.Add(_footerStatus);

        return root;
    }

    // ── Device group ─────────────────────────────────────────
    private Border BuildDeviceGroup()
    {
        var stack = new StackPanel();

        // Current device info row
        var infoRow = new Grid
        {
            Padding = new Thickness(16),
            ColumnSpacing = 14
        };
        infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconTile = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = Brush("ControlAltFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = "\uE702",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconTile, 0);
        infoRow.Children.Add(iconTile);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        textStack.Children.Add(_deviceNameLabel);
        textStack.Children.Add(_deviceMacLabel);
        Grid.SetColumn(textStack, 1);
        infoRow.Children.Add(textStack);

        stack.Children.Add(infoRow);
        stack.Children.Add(Divider());

        // Device picker row
        stack.Children.Add(SettingsRow(
            "\uF785",
            "Select Device",
            "Choose from paired Bluetooth devices",
            BuildDevicePicker()));

        stack.Children.Add(Divider());

        // Disconnect action row
        var disconnectBtn = new Button { Content = "Disconnect Audio", Height = 32 };
        disconnectBtn.Click += async (_, __) => await _viewModel.DisconnectAudioCommand.ExecuteAsync(null);
        stack.Children.Add(SettingsRow(
            "\uE8D3",
            "Audio Connection",
            "Force-disconnect the current audio stream",
            disconnectBtn));

        return GroupCard(stack);
    }

    private StackPanel BuildDevicePicker()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(_deviceCombo);
        row.Children.Add(_refreshBtn);
        return row;
    }

    // ── Preferences group ────────────────────────────────────
    private Border BuildPreferencesGroup()
    {
        var stack = new StackPanel();

        stack.Children.Add(SettingsRow(
            "\uE765",
            "Global Hotkeys",
            "Ctrl+Alt+C toggles audio · Ctrl+Alt+W toggles detection",
            _keybindSwitch));

        stack.Children.Add(Divider());

        stack.Children.Add(SettingsRow(
            "\uE771",
            "Appearance",
            "Choose light, dark or follow system",
            _themeCombo));

        return GroupCard(stack);
    }

    // ── Detection group ──────────────────────────────────────
    private Border BuildDetectionGroup()
    {
        var stack = new StackPanel();

        // Model status row (custom, wider)
        var modelRow = new StackPanel { Spacing = 10, Padding = new Thickness(16) };

        var modelHeader = new Grid { ColumnSpacing = 12 };
        modelHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modelHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modelHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var modelIcon = new FontIcon
        {
            Glyph = "\uE896",
            FontSize = 16,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(modelIcon, 0);
        modelHeader.Children.Add(modelIcon);

        var modelTitleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Margin = new Thickness(2, 0, 0, 0) };
        modelTitleStack.Children.Add(new TextBlock
        {
            Text = "Detection Model",
            FontWeight = FontWeights.SemiBold
        });
        modelTitleStack.Children.Add(_modelInfoText);
        Grid.SetColumn(modelTitleStack, 1);
        modelHeader.Children.Add(modelTitleStack);

        Grid.SetColumn(_statusBadge, 2);
        modelHeader.Children.Add(_statusBadge);

        modelRow.Children.Add(modelHeader);
        modelRow.Children.Add(_downloadProgressPanel);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        btnRow.Children.Add(_downloadBtn);
        btnRow.Children.Add(_cancelBtn);
        btnRow.Children.Add(_removeBtn);
        modelRow.Children.Add(btnRow);

        stack.Children.Add(modelRow);
        stack.Children.Add(Divider());

        stack.Children.Add(SettingsRow(
            "\uE8B8",
            "Webcam",
            "Camera used for phone detection",
            _webcamCombo));

        return GroupCard(stack);
    }

    // ════════════════════════════════════════════════════════
    //  EVENTS
    // ════════════════════════════════════════════════════════

    private async void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var d = args.GetDeferral();
        try { await _viewModel.SaveSettingsCommand.ExecuteAsync(null); }
        finally { d.Complete(); }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_closed)
            DispatcherQueue?.TryEnqueue(Sync);
    }

    // ════════════════════════════════════════════════════════
    //  UI SYNC
    // ════════════════════════════════════════════════════════

    private void Sync()
    {
        if (_closed) return;
        _syncing = true;
        try
        {
            // Device
            _deviceNameLabel.Text = _viewModel.SelectedDeviceName;
            _deviceMacLabel.Text  = _viewModel.SelectedDeviceMac;
            _deviceCombo.SelectedItem  = _viewModel.SelectedBluetoothDevice;
            _deviceCombo.IsEnabled     = !_viewModel.IsLoadingBluetoothDevices;
            _refreshBtn.IsEnabled      = !_viewModel.IsLoadingBluetoothDevices;

            // Preferences
            _keybindSwitch.IsOn = _viewModel.KeybindEnabled;
            _themeCombo.ItemsSource  = _viewModel.ThemeOptions.ToList();
            _themeCombo.SelectedItem = _viewModel.SelectedTheme;

            // Webcam
            _webcamCombo.ItemsSource  = _viewModel.AvailableWebcams.ToList();
            _webcamCombo.SelectedItem = _viewModel.SelectedWebcam;

            // Detection model badge
            bool installed   = _viewModel.AutoDetectionDownloaded;
            bool downloading = _viewModel.IsDownloading;

            _statusBadgeText.Text = installed ? "Installed" : "Not Installed";
            _statusBadgeText.Foreground = installed
                ? Brush("TextOnAccentFillColorPrimaryBrush")
                : Brush("TextFillColorSecondaryBrush");
            _statusBadge.Background = installed
                ? Brush("AccentFillColorDefaultBrush")
                : Brush("ControlAltFillColorSecondaryBrush");

            _modelInfoText.Text = installed
                ? $"YOLOv26 model · {_viewModel.AutoDetectionSizeText}"
                : "YOLOv26 nano model (~13 MB download required)";

            // Progress
            _downloadProgressPanel.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
            _downloadProgressText.Text = $"{_viewModel.DownloadProgress:0}%";
            if (_downloadProgressPanel.Children.Count > 1 && _downloadProgressPanel.Children[1] is ProgressBar pb)
                pb.Value = _viewModel.DownloadProgress;
            _downloadStatusText.Text = _viewModel.DownloadStatusText;

            // Buttons
            _downloadBtn.Visibility    = installed || downloading ? Visibility.Collapsed : Visibility.Visible;
            _downloadBtn.IsEnabled     = !downloading;
            _cancelBtn.Visibility      = downloading ? Visibility.Visible : Visibility.Collapsed;
            _removeBtn.Visibility      = installed && !downloading ? Visibility.Visible : Visibility.Collapsed;
            _removeBtn.IsEnabled       = !downloading;

            // Footer
            _footerStatus.Text = _viewModel.StatusMessage ?? string.Empty;
        }
        finally { _syncing = false; }
    }

    // ════════════════════════════════════════════════════════
    //  HELPERS — Windows 11-style primitives
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Standard Windows 11 settings row: [icon tile] [title + description] [control]
    /// </summary>
    private static Grid SettingsRow(string glyph, string title, string description, FrameworkElement control)
    {
        var row = new Grid { Padding = new Thickness(16), ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // icon
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // text
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // control

        // Icon tile
        var iconTile = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = Brush("ControlAltFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = glyph,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.9
            }
        };
        Grid.SetColumn(iconTile, 0);
        row.Children.Add(iconTile);

        // Text
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        textStack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrEmpty(description))
        {
            textStack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap
            });
        }
        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        // Control
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 2);
        row.Children.Add(control);

        return row;
    }

    private static TextBlock SectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Opacity = 0.85,
            Margin = new Thickness(2, 20, 0, 8)
        };
    }

    private static Border Divider()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(66, 0, 0, 0),  // aligns with text column (icon tile = 36 + gap 14 + padding 16 = 66)
            Background = Brush("DividerStrokeColorDefaultBrush")
        };
    }

    private static Border GroupCard(UIElement child)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(8),
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

    private static async Task<ContentDialogResult> ShowWithRetry(ContentDialog dlg)
    {
        for (int i = 0; i < 8; i++)
        {
            try { return await dlg.ShowAsync(); }
            catch (COMException ex) when (ex.Message.Contains("Only a single ContentDialog"))
            { await Task.Delay(75); }
        }
        return ContentDialogResult.None;
    }
}
