using Avalonia.Controls;
using Avalonia.Interactivity;

namespace perinma.Views.Main;

public partial class ReauthenticationDialogWindow : Window
{
    public ReauthenticationDialogWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (DataContext is ReauthenticationDialogViewModel viewModel)
        {
            viewModel.ReauthenticationCompleted -= ViewModel_ReauthenticationCompleted;
            viewModel.CloseRequested -= ViewModel_CloseRequested;
        }
    }

    public void SetViewModel(ReauthenticationDialogViewModel viewModel)
    {
        DataContext = viewModel;
        viewModel.ReauthenticationCompleted += ViewModel_ReauthenticationCompleted;
        viewModel.CloseRequested += ViewModel_CloseRequested;
    }

    private void ViewModel_ReauthenticationCompleted(object? sender, System.EventArgs e)
    {
        Close();
    }

    private void ViewModel_CloseRequested(object? sender, System.EventArgs e)
    {
        Close();
    }
}
