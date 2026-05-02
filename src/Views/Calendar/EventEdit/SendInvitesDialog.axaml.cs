using Avalonia.Controls;

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
            vm.OkRequested += (s, result) =>
            {
                Result = result;
                Close(result);
            };
        }
    }
}

public enum SendInvitesResult
{
    SendToAll,
    SendToExternalOnly,
    SendToNone
}
