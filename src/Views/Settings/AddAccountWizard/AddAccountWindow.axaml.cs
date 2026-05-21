using System;

namespace perinma.Views.Settings.AddAccountWizard;

public partial class AddAccountWindow : AtomUI.Desktop.Controls.Window
{
    public AddAccountWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is AddAccountWizardViewModel viewModel)
        {
            viewModel.CloseRequested += (_, _) => Close();
        }
    }
}
