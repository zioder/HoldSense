using Avalonia.Controls;
using _HoldSense.ViewModels;

namespace _HoldSense.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;

        viewModel.CloseRequested += (sender, e) =>
        {
            Close();
        };
    }
}






