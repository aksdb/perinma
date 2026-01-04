using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace perinma.Views.Main;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void MnuExit_OnClick(object? sender, RoutedEventArgs e)
    {
        Environment.Exit(0);
    }
}