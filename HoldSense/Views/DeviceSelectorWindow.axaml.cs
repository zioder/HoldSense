using Avalonia.Controls;
using _HoldSense.ViewModels;

namespace _HoldSense.Views;

public partial class DeviceSelectorWindow : Window
{
    public DeviceSelectorWindow()
    {
        InitializeComponent();
    }

    public DeviceSelectorWindow(DeviceSelectorViewModel viewModel) : this()
    {
        DataContext = viewModel;
        
        viewModel.DeviceSelected += (sender, device) =>
        {
            Close(device);
        };
    }
}






