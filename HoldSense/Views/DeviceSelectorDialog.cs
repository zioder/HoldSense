using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using _HoldSense.Models;
using _HoldSense.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Foundation;

namespace _HoldSense.Views;

internal sealed class DeviceSelectorDialog : ContentDialog
{
    private readonly DeviceSelectorViewModel _viewModel;

    // ── Dynamic UI elements ─────────────────────────────────
    private readonly TextBlock _statusText;
    private readonly ProgressRing _loadingRing;
    private readonly ListView _deviceList;
    private readonly TextBlock _emptyText;

    private DeviceSelectorDialog(XamlRoot xamlRoot, DeviceSelectorViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Title = "Select Bluetooth Device";
        XamlRoot = xamlRoot;
        PrimaryButtonText = "Save";
        SecondaryButtonText = "Skip";
        CloseButtonText = "Refresh";
        DefaultButton = ContentDialogButton.Primary;

        // ── Allocate controls ───────────────────────────────
        _statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        };

        _loadingRing = new ProgressRing
        {
            Height = 24,
            Width = 24,
            IsActive = false,
            Visibility = Visibility.Collapsed
        };

        _emptyText = new TextBlock
        {
            Text = "No devices found. Make sure your Bluetooth device is paired in Windows Settings.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.6,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 8, 0, 8)
        };

        _deviceList = new ListView
        {
            Height = 300,
            SelectionMode = ListViewSelectionMode.Single
        };
        _deviceList.SelectionChanged += (_, __) =>
        {
            if (_deviceList.SelectedItem is DeviceEntry entry)
                _viewModel.SelectedDevice = entry.Device;
        };

        // ── Build layout ────────────────────────────────────
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new FontIcon
        {
            Glyph = "\uE702",
            FontSize = 18,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = "Choose the Bluetooth device for audio playback.",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });

        var loadingRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        loadingRow.Children.Add(_loadingRing);
        loadingRow.Children.Add(_statusText);

        Content = new StackPanel
        {
            Spacing = 10,
            MinWidth = 360,
            Children =
            {
                header,
                loadingRow,
                _emptyText,
                new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
                    BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
                    BorderThickness = new Thickness(1),
                    Child = _deviceList
                }
            }
        };

        PrimaryButtonClick += OnPrimaryButtonClick;
        SecondaryButtonClick += (_, __) => _viewModel.SkipCommand.Execute(null);
        CloseButtonClick += OnRefreshClick;
    }

    // ════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════

    public static async Task<BluetoothDevice?> ShowAsync(XamlRoot xamlRoot, DeviceSelectorViewModel viewModel)
    {
        await viewModel.InitializeAsync();
        var dialog = new DeviceSelectorDialog(xamlRoot, viewModel);
        dialog.UpdateUi();
        var result = await ShowDialogAsync(dialog);
        return result == ContentDialogResult.Primary ? viewModel.SelectedDevice : null;
    }

    // ════════════════════════════════════════════════════════
    //  EVENTS
    // ════════════════════════════════════════════════════════

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_viewModel.SelectedDevice is null)
        {
            _viewModel.StatusMessage = "Select a device first, or press Skip.";
            args.Cancel = true;
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            await _viewModel.SelectDeviceCommand.ExecuteAsync(null);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnRefreshClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        await _viewModel.RefreshDevicesCommand.ExecuteAsync(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateUi);
    }

    // ════════════════════════════════════════════════════════
    //  UI SYNC
    // ════════════════════════════════════════════════════════

    private void UpdateUi()
    {
        _statusText.Text = _viewModel.StatusMessage;
        _loadingRing.IsActive = _viewModel.IsLoading;
        _loadingRing.Visibility = _viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        var entries = BuildEntries(_viewModel.Devices);
        _deviceList.ItemsSource = entries;

        bool empty = !entries.Any() && !_viewModel.IsLoading;
        _emptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _deviceList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        if (_viewModel.SelectedDevice is not null)
        {
            var selected = entries.FirstOrDefault(e =>
                e.Device.MacAddress == _viewModel.SelectedDevice.MacAddress ||
                e.Device.DeviceId == _viewModel.SelectedDevice.DeviceId);
            _deviceList.SelectedItem = selected;
        }
    }

    // ════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════

    private static IReadOnlyList<DeviceEntry> BuildEntries(IEnumerable<BluetoothDevice> devices)
    {
        return devices.Select(d => new DeviceEntry(d)).ToList();
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

    // ── Item wrapper for ListView ───────────────────────────
    private sealed class DeviceEntry
    {
        public BluetoothDevice Device { get; }
        public string Display => $"\uE702  {Device.Name}  ({FormatIdentifier(Device.MacAddress)})";

        public DeviceEntry(BluetoothDevice device) => Device = device;

        public override string ToString() => Display;

        private static string FormatIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "(unknown)";
            }

            if (value.Length <= 28)
            {
                return value;
            }

            return value[..14] + "..." + value[^10..];
        }
    }
}
