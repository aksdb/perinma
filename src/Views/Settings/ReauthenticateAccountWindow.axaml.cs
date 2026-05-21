using System;

namespace perinma.Views.Settings;

public partial class ReauthenticateAccountWindow : AtomUI.Desktop.Controls.Window
{
    public ReauthenticateAccountWindow()
    {
        InitializeComponent();
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
