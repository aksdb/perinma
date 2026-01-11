using System;
using Avalonia.Controls;

namespace perinma.Views.Settings;

public partial class ReauthenticateAccountWindow : Window
{
    public ReauthenticateAccountWindow()
    {
        InitializeComponent();

        // Subscribe to close request from ViewModel
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ReauthenticateAccountViewModel viewModel)
        {
            viewModel.CloseRequested += (_, _) => Close();
        }
    }
}
