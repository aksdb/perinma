using Avalonia.Controls;

namespace perinma.Views.Main;

public partial class AboutDialogWindow : Window
{
    public AboutDialogWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(AboutDialogViewModel viewModel)
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
    }
}
