using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace perinma.Views.Main;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
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

            if (sidebarWidth > 0 && CalendarViewGrid.ColumnDefinitions.Count > 0)
            {
                CalendarViewGrid.ColumnDefinitions[0].Width = new GridLength(sidebarWidth);
            }
        }
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var sidebarWidth = CalendarViewGrid.ColumnDefinitions.Count > 0 
                ? (int)CalendarViewGrid.ColumnDefinitions[0].Width.Value 
                : 250;
            await viewModel.SaveWindowSettingsAsync(Position.X, Position.Y, (int)Width, (int)Height, sidebarWidth);
        }
    }

    private void MnuExit_OnClick(object? sender, RoutedEventArgs e)
    {
        Environment.Exit(0);
    }
}