using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using perinma.Services;

namespace perinma.Views.Calendar.EventEdit;

public partial class SendInvitesDialog : Window
{
    public SendInvitesDialog()
    {
        InitializeComponent();
    }

    public SendInvitesResult Result { get; private set; } = SendInvitesResult.SendToAll;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is SendInvitesDialogViewModel vm)
        {
            vm.OkRequested += result =>
            {
                Result = result;
                Close(result);
            };
        }
    }
}
