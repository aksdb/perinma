using Avalonia.Controls;
using Avalonia.Interactivity;

namespace perinma.Views.Debug;

public partial class DebugWindow : Window
{
    public DebugWindow()
    {
        InitializeComponent();
        Closing += DebugWindow_Closing;
    }

    private async void DebugWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is DebugWindowViewModel viewModel)
        {
            await viewModel.OnClosingAsync();
        }
    }
}
