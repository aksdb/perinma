using System;
using System.Threading.Tasks;
using AtomUI;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace perinma.Views.Main;

public partial class MainWindow : AtomUI.Desktop.Controls.Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        
        viewModel.AfterLoad();
            
        var (x, y, width, height, sidebarWidth) = await viewModel.GetWindowSettingsAsync();

        if (x != int.MinValue && y != int.MinValue)
        {
            Position = new Avalonia.PixelPoint(x, y);
        }

        if (width > 0 && height > 0)
        {
            Width = width;
            Height = height;
        }

        if (sidebarWidth > 0)
        {
            AtomUI.Desktop.Controls.Splitter.SetSize(CalendarListPane, new Dimension(sidebarWidth));
        }
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var sidebarWidth = AtomUI.Desktop.Controls.Splitter.GetSize(CalendarListPane)?.Value is > 0 and var savedWidth
                ? (int)savedWidth
                : (int)Math.Round(CalendarListPane.Bounds.Width);
            await viewModel.SaveWindowSettingsAsync(Position.X, Position.Y, (int)Width, (int)Height, sidebarWidth);
            await viewModel.SaveViewStateAsync();
            await viewModel.SaveThemeAsync();
            viewModel.Cleanup();
        }
    }

    private void MnuExit_OnClick(object? sender, RoutedEventArgs e)
    {
        Environment.Exit(0);
    }

    private async void EnableDebuggingMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.ToggleDebuggingCommand.ExecuteAsync(null);
    }
}