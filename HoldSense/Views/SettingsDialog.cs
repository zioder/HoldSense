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
using Windows.Foundation;

namespace _HoldSense.Views;

internal sealed class SettingsDialog : ContentDialog
{
    private readonly SettingsViewModel _viewModel;

    // ── Dynamic UI elements ─────────────────────────────────
    private readonly TextBlock _deviceNameText;
    private readonly TextBlock _deviceMacText;
    private readonly ComboBox _deviceCombo;
    private readonly Button _refreshDevicesButton;
    private readonly ToggleSwitch _keybindToggle;
    private readonly ComboBox _themeCombo;
    private readonly ComboBox _webcamCombo;
    private readonly TextBlock _statusText;
    private readonly Border _autoDetBadge;
    private readonly TextBlock _autoDetBadgeText;
    private readonly TextBlock _autoDetSizeText;
    private readonly TextBlock _downloadStatusText;
    private readonly TextBlock _downloadProgress;
    private readonly Button _downloadButton;
    private readonly Button _cancelDownloadButton;
    private readonly Button _removeButton;
    private readonly StackPanel _downloadProgressPanel;

    private bool _updatingUi;
    private bool _isClosed;

    private SettingsDialog(XamlRoot xamlRoot, SettingsViewModel viewModel, ElementTheme requestedTheme)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Title = "Settings";
        XamlRoot = xamlRoot;
        RequestedTheme = requestedTheme;
        PrimaryButtonText = "Save";
        CloseButtonText = "Close";
        DefaultButton = ContentDialogButton.Primary;

        // ── Allocate dynamic controls ───────────────────────
        _deviceNameText = new TextBlock { FontWeight = FontWeights.SemiBold };
        _deviceMacText = new TextBlock { Opacity = 0.6, FontSize = 12 };
        _deviceCombo = new ComboBox
        {
            Width = 280,
            DisplayMemberPath = "DisplayName"
        };
        _refreshDevicesButton = new Button { Content = "Refresh", Height = 32 };

        _keybindToggle = new ToggleSwitch();

        _themeCombo = new ComboBox { MinWidth = 130 };
        _webcamCombo = new ComboBox { MinWidth = 180, DisplayMemberPath = "Name" };

        _statusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };

        _autoDetBadgeText = new TextBlock { FontSize = 12 };
        _autoDetBadge = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3, 10, 3),
            Child = _autoDetBadgeText
        };
        _autoDetSizeText = new TextBlock { Opacity = 0.6, FontSize = 12 };

        _downloadStatusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.6, FontSize = 12 };
        _downloadProgress = new TextBlock { Opacity = 0.7, FontSize = 12 };
        _downloadButton = new Button { Content = "Download (~13 MB)" };
        _cancelDownloadButton = new Button { Content = "Cancel" };
        _removeButton = new Button { Content = "Remove" };
        _downloadProgressPanel = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };

        // ── Wire events ─────────────────────────────────────
        _themeCombo.SelectionChanged += (_, __) =>
        {
            if (!_updatingUi && _themeCombo.SelectedItem is string theme)
                _viewModel.SelectedTheme = theme;
        };
        _deviceCombo.SelectionChanged += (_, __) =>
        {
            if (!_updatingUi && _deviceCombo.SelectedItem is BluetoothDevice device)
                _viewModel.SelectedBluetoothDevice = device;
        };
        _refreshDevicesButton.Click += async (_, __) => await _viewModel.RefreshBluetoothDevicesCommand.ExecuteAsync(null);
        _webcamCombo.SelectionChanged += (_, __) =>
        {
            if (!_updatingUi && _webcamCombo.SelectedItem is WebcamDevice webcam)
                _viewModel.SelectedWebcam = webcam;
        };
        _keybindToggle.Toggled += (_, __) =>
        {
            if (!_updatingUi) _viewModel.KeybindEnabled = _keybindToggle.IsOn;
        };
        _downloadButton.Click += async (_, __) => await _viewModel.DownloadAutoDetectionCommand.ExecuteAsync(null);
        _cancelDownloadButton.Click += (_, __) => _viewModel.CancelDownloadCommand.Execute(null);
        _removeButton.Click += async (_, __) => await _viewModel.RemoveAutoDetectionCommand.ExecuteAsync(null);
        _deviceCombo.ItemsSource = _viewModel.AvailableBluetoothDevices;

        // ── Build content ───────────────────────────────────
        Content = new ScrollViewer
        {
            MaxHeight = 560,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildSettingsContent()
        };

        PrimaryButtonClick += OnSaveClick;
        Closed += OnDialogClosed;
    }

    // ════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════

    public static async Task ShowAsync(XamlRoot xamlRoot, SettingsViewModel viewModel, ElementTheme requestedTheme)
    {
        await viewModel.LoadSettingsAsync();
        var dialog = new SettingsDialog(xamlRoot, viewModel, requestedTheme);
        dialog.UpdateUi();
        await ShowDialogAsync(dialog);
    }

    // ════════════════════════════════════════════════════════
    //  SETTINGS LAYOUT
    // ════════════════════════════════════════════════════════

    private StackPanel BuildSettingsContent()
    {
        var root = new StackPanel { Spacing = 4, MaxWidth = 560 };

        // ── Section: Bluetooth Device ───────────────────────
        root.Children.Add(SectionHeader("Bluetooth Device"));
        root.Children.Add(BuildDeviceSection());

        // ── Section: Preferences ────────────────────────────
        root.Children.Add(SectionHeader("Preferences"));
        root.Children.Add(BuildPreferencesGroup());

        // ── Section: Phone Detection ────────────────────────
        root.Children.Add(SectionHeader("Phone Detection"));
        root.Children.Add(BuildDetectionGroup());

        // ── Status message ──────────────────────────────────
        root.Children.Add(_statusText);

        return root;
    }

    /// <summary>
    /// Device info card with name, MAC, Change and Disconnect buttons.
    /// </summary>
    private Border BuildDeviceSection()
    {
        var body = new StackPanel { Spacing = 8 };

        // Device info row
        var infoRow = new Grid();
        infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new FontIcon
        {
            Glyph = "\uE702",
            FontSize = 24,
            Margin = new Thickness(0, 0, 14, 0),
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        infoRow.Children.Add(icon);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        textStack.Children.Add(_deviceNameText);
        textStack.Children.Add(_deviceMacText);
        Grid.SetColumn(textStack, 1);
        infoRow.Children.Add(textStack);

        body.Children.Add(infoRow);

        var pickerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        pickerRow.Children.Add(_deviceCombo);
        pickerRow.Children.Add(_refreshDevicesButton);
        body.Children.Add(pickerRow);

        // Action buttons
        var disconnectBtn = new Button { Content = "Disconnect Audio" };
        disconnectBtn.Click += async (_, __) => await _viewModel.DisconnectAudioCommand.ExecuteAsync(null);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0)
        };
        btns.Children.Add(disconnectBtn);
        body.Children.Add(btns);

        return MakeCard(body);
    }

    /// <summary>
    /// Grouped settings card: Hotkey toggle + Theme selector.
    /// </summary>
    private Border BuildPreferencesGroup()
    {
        var stack = new StackPanel();

        // Hotkey row
        stack.Children.Add(SettingsRow(
            "\uE765",
            "Hotkey Toggle",
            "Ctrl+Alt+C to connect / disconnect audio",
            _keybindToggle));

        stack.Children.Add(Divider());

        // Theme row
        stack.Children.Add(SettingsRow(
            "\uE771",
            "Appearance",
            "App color theme",
            _themeCombo));

        return MakeGroupCard(stack);
    }

    /// <summary>
    /// Grouped settings card: Auto-detection download area + toggle + webcam selector.
    /// </summary>
    private Border BuildDetectionGroup()
    {
        var stack = new StackPanel();

        // Auto-detection download area
        var downloadBody = new StackPanel { Spacing = 8, Padding = new Thickness(16) };

        // Header row with badge
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dlIcon = new FontIcon
        {
            Glyph = "\uE896",
            FontSize = 16,
            Margin = new Thickness(0, 0, 12, 0),
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dlIcon, 0);
        headerRow.Children.Add(dlIcon);

        var dlTitle = new TextBlock
        {
            Text = "Auto Detection Model",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dlTitle, 1);
        headerRow.Children.Add(dlTitle);

        Grid.SetColumn(_autoDetBadge, 2);
        _autoDetBadge.VerticalAlignment = VerticalAlignment.Center;
        headerRow.Children.Add(_autoDetBadge);

        downloadBody.Children.Add(headerRow);
        downloadBody.Children.Add(_autoDetSizeText);

        // Download progress panel (shown during download)
        _downloadProgressPanel.Children.Add(_downloadProgress);
        _downloadProgressPanel.Children.Add(_downloadStatusText);
        downloadBody.Children.Add(_downloadProgressPanel);

        // Buttons
        var dlBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        dlBtns.Children.Add(_downloadButton);
        dlBtns.Children.Add(_removeButton);
        dlBtns.Children.Add(_cancelDownloadButton);
        downloadBody.Children.Add(dlBtns);

        stack.Children.Add(downloadBody);
        stack.Children.Add(Divider());

        // Webcam selector row
        stack.Children.Add(SettingsRow(
            "\uE8B8",
            "Webcam",
            "Camera used for phone detection",
            _webcamCombo));

        return MakeGroupCard(stack);
    }

    // ════════════════════════════════════════════════════════
    //  EVENTS
    // ════════════════════════════════════════════════════════

    private async void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await _viewModel.SaveSettingsCommand.ExecuteAsync(null);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isClosed)
            return;

        DispatcherQueue?.TryEnqueue(UpdateUi);
    }

    private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _isClosed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    // ════════════════════════════════════════════════════════
    //  UI SYNC
    // ════════════════════════════════════════════════════════

    private void UpdateUi()
    {
        if (_isClosed)
            return;

        _updatingUi = true;
        try
        {
            // Device info
            _deviceNameText.Text = _viewModel.SelectedDeviceName;
            _deviceMacText.Text = _viewModel.SelectedDeviceMac;
            _deviceCombo.SelectedItem = _viewModel.SelectedBluetoothDevice;
            _deviceCombo.IsEnabled = !_viewModel.IsLoadingBluetoothDevices;
            _refreshDevicesButton.IsEnabled = !_viewModel.IsLoadingBluetoothDevices;

            // Toggles
            _keybindToggle.IsOn = _viewModel.KeybindEnabled;

            // Theme
            _themeCombo.ItemsSource = _viewModel.ThemeOptions.ToList();
            _themeCombo.SelectedItem = _viewModel.SelectedTheme;

            // Webcam
            _webcamCombo.ItemsSource = _viewModel.AvailableWebcams.ToList();
            _webcamCombo.SelectedItem = _viewModel.SelectedWebcam;

            // Auto-detection badge
            bool installed = _viewModel.AutoDetectionDownloaded;
            _autoDetBadgeText.Text = installed ? "Installed" : "Not Installed";
            _autoDetBadgeText.Foreground = installed
                ? ThemeBrush("TextOnAccentFillColorPrimaryBrush")
                : ThemeBrush("TextFillColorSecondaryBrush");
            _autoDetBadge.Background = installed
                ? ThemeBrush("AccentFillColorDefaultBrush")
                : ThemeBrush("ControlAltFillColorSecondaryBrush");

            _autoDetSizeText.Text = installed
                ? $"YOLOv26 model \u00B7 {_viewModel.AutoDetectionSizeText}"
                : "YOLOv26 model (~13 MB download)";

            // Download progress
            bool downloading = _viewModel.IsDownloading;
            _downloadProgressPanel.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
            _downloadProgress.Text = $"Progress: {_viewModel.DownloadProgress:0}%";
            _downloadStatusText.Text = _viewModel.DownloadStatusText;

            _downloadButton.IsEnabled = !downloading && !installed;
            _downloadButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            _cancelDownloadButton.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
            _removeButton.IsEnabled = !downloading && installed;
            _removeButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;

            // Status
            _statusText.Text = _viewModel.StatusMessage;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    // ════════════════════════════════════════════════════════
    //  FLUENT DESIGN HELPERS
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// A Windows 11-style settings row: [Icon] Title / Description ... [Control].
    /// </summary>
    private static Grid SettingsRow(string glyph, string title, string description, FrameworkElement control)
    {
        var row = new Grid { Padding = new Thickness(16) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.75
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold
        });
        if (!string.IsNullOrEmpty(description))
        {
            textStack.Children.Add(new TextBlock
            {
                Text = description,
                Opacity = 0.6,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }
        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(16, 0, 0, 0);
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
            Opacity = 0.85,
            Margin = new Thickness(4, 20, 0, 6)
        };
    }

    private static Border Divider()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(48, 0, 0, 0),
            Background = ThemeBrush("DividerStrokeColorDefaultBrush")
        };
    }

    private static Border MakeCard(UIElement child, Thickness? padding = null)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = padding ?? new Thickness(16),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Child = child
        };
    }

    private static Border MakeGroupCard(UIElement child)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(8),
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

    private static Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        return ShowDialogWithRetryAsync(dialog);
    }

    private static async Task<ContentDialogResult> ShowDialogWithRetryAsync(ContentDialog dialog)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                return await dialog.ShowAsync();
            }
            catch (COMException ex) when (ex.Message.Contains("Only a single ContentDialog can be open at any time.", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(75);
            }
        }

        return ContentDialogResult.None;
    }
}
