using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using _HoldSense.Models;
using _HoldSense.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace _HoldSense.Views;

/// <summary>
/// Windows 11-style device picker dialog. Shows paired Bluetooth A2DP devices
/// in a clean list with icon tiles, name and MAC address.
/// </summary>
internal sealed class DeviceSelectorDialog : ContentDialog
{
    private readonly DeviceSelectorViewModel _viewModel;

    // ── Dynamic elements ────────────────────────────────────
    private readonly ProgressRing _spinner;
    private readonly TextBlock _statusText;
    private readonly TextBlock _emptyHint;
    private readonly StackPanel _deviceListPanel;

    private DeviceSelectorDialog(XamlRoot xamlRoot, DeviceSelectorViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnVmChanged;

        XamlRoot = xamlRoot;
        Title = "Select Bluetooth Device";
        PrimaryButtonText = "Save";
        SecondaryButtonText = "Skip";
        CloseButtonText = "Refresh";
        DefaultButton = ContentDialogButton.Primary;
        MinWidth = 420;

        // ── Controls ─────────────────────────────────────────
        _spinner = new ProgressRing { Height = 20, Width = 20, IsActive = false };

        _statusText = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        _emptyHint = new TextBlock
        {
            Text = "No paired Bluetooth devices found.\nOpen Windows Settings → Bluetooth and pair your device first.",
            FontSize = 13,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 24, 0, 24),
            Visibility = Visibility.Collapsed
        };

        _deviceListPanel = new StackPanel { Spacing = 2 };

        // ── Layout ────────────────────────────────────────────
        // Header area
        var headerGrid = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 16) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var headerIcon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(8),
            Background = Brush("ControlAltFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = "\uE702",
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(headerIcon, 0);
        headerGrid.Children.Add(headerIcon);

        var headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        headerText.Children.Add(new TextBlock
        {
            Text = "Bluetooth Audio Device",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15
        });
        headerText.Children.Add(new TextBlock
        {
            Text = "Select the device you want to route phone audio through.",
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(headerText, 1);
        headerGrid.Children.Add(headerText);

        // Status bar (spinner + text)
        var statusBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 8)
        };
        statusBar.Children.Add(_spinner);
        statusBar.Children.Add(_statusText);

        // Devices list inside a card
        var listCard = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = Brush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Brush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new StackPanel
                {
                    Padding = new Thickness(4),
                    Children = { _deviceListPanel, _emptyHint }
                }
            }
        };

        Content = new StackPanel
        {
            Spacing = 4,
            MinWidth = 380,
            Children = { headerGrid, statusBar, listCard }
        };

        PrimaryButtonClick   += OnSave;
        SecondaryButtonClick += (_, __) => _viewModel.SkipCommand.Execute(null);
        CloseButtonClick     += OnRefresh;
    }

    // ════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════

    public static async Task<BluetoothDevice?> ShowAsync(XamlRoot xamlRoot, DeviceSelectorViewModel viewModel)
    {
        await viewModel.InitializeAsync();
        var dlg = new DeviceSelectorDialog(xamlRoot, viewModel);
        dlg.Sync();
        var result = await ShowWithRetry(dlg);
        return result == ContentDialogResult.Primary ? viewModel.SelectedDevice : null;
    }

    // ════════════════════════════════════════════════════════
    //  EVENTS
    // ════════════════════════════════════════════════════════

    private async void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_viewModel.SelectedDevice is null)
        {
            _viewModel.StatusMessage = "Please select a device first, or press Skip.";
            args.Cancel = true;
            return;
        }
        var d = args.GetDeferral();
        try { await _viewModel.SelectDeviceCommand.ExecuteAsync(null); }
        finally { d.Complete(); }
    }

    private async void OnRefresh(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        await _viewModel.RefreshDevicesCommand.ExecuteAsync(null);
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue?.TryEnqueue(Sync);
    }

    // ════════════════════════════════════════════════════════
    //  UI SYNC
    // ════════════════════════════════════════════════════════

    private void Sync()
    {
        bool loading = _viewModel.IsLoading;
        _spinner.IsActive  = loading;
        _spinner.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        _statusText.Text   = _viewModel.StatusMessage;

        var devices = _viewModel.Devices.ToList();

        _deviceListPanel.Children.Clear();

        bool empty = !devices.Any() && !loading;
        _emptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        foreach (var device in devices)
        {
            bool isSelected = _viewModel.SelectedDevice?.MacAddress == device.MacAddress
                           || _viewModel.SelectedDevice?.DeviceId == device.DeviceId;

            _deviceListPanel.Children.Add(BuildDeviceRow(device, isSelected));
        }
    }

    private Border BuildDeviceRow(BluetoothDevice device, bool isSelected)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 10, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // BT icon tile
        var iconTile = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = isSelected
                ? Brush("AccentFillColorDefaultBrush")
                : Brush("ControlAltFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = "\uE702",
                FontSize = 14,
                Foreground = isSelected
                    ? Brush("TextOnAccentFillColorPrimaryBrush")
                    : Brush("TextFillColorPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconTile, 0);
        row.Children.Add(iconTile);

        // Name + MAC
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        textStack.Children.Add(new TextBlock
        {
            Text = device.Name,
            FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textStack.Children.Add(new TextBlock
        {
            Text = FormatMac(device.MacAddress),
            FontSize = 11,
            Opacity = 0.55,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, Cascadia Code, monospace")
        });
        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        // Selected check
        if (isSelected)
        {
            var check = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 14,
                Foreground = Brush("AccentFillColorDefaultBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(check, 2);
            row.Children.Add(check);
        }

        var container = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = isSelected
                ? Brush("SubtleFillColorSecondaryBrush")
                : new SolidColorBrush(Colors.Transparent),
            Child = row
        };

        // Make it clickable
        container.PointerPressed += (_, __) =>
        {
            _viewModel.SelectedDevice = device;
        };
        container.PointerEntered += (_, __) =>
        {
            if (!isSelected)
                container.Background = Brush("SubtleFillColorSecondaryBrush");
        };
        container.PointerExited += (_, __) =>
        {
            if (!isSelected)
                container.Background = new SolidColorBrush(Colors.Transparent);
        };

        return container;
    }

    // ════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════

    private static string FormatMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return "(unknown)";
        return mac.Length <= 17 ? mac.ToUpperInvariant() : mac[..8] + "…" + mac[^5..];
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
