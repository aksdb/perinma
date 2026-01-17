using System.Threading.Tasks;
using Avalonia.Controls;

namespace perinma.Views.MessageBox;

public partial class MessageBoxWindow : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;
    private TaskCompletionSource<MessageBoxResult>? _tcs;

    public MessageBoxWindow()
    {
        InitializeComponent();
    }

    public static Task<MessageBoxResult> ShowAsync(Window owner, string title, string message, MessageBoxButtons buttons)
    {
        var messageBox = new MessageBoxWindow();
        messageBox.Title = title;
        messageBox.MessageText.Text = message;
        messageBox.ConfigureButtons(buttons);

        var tcs = new TaskCompletionSource<MessageBoxResult>();
        messageBox._tcs = tcs;

        messageBox.Closed += (_, _) => tcs.TrySetResult(messageBox._result);

        messageBox.ShowDialog(owner);

        return tcs.Task;
    }

    private void ConfigureButtons(MessageBoxButtons buttons)
    {
        switch (buttons)
        {
            case MessageBoxButtons.Ok:
                OkButton.IsVisible = true;
                OkButton.IsDefault = true;
                break;
            case MessageBoxButtons.OkCancel:
                OkButton.IsVisible = true;
                CancelButton.IsVisible = true;
                OkButton.IsDefault = true;
                CancelButton.IsCancel = true;
                break;
            case MessageBoxButtons.YesNo:
                YesButton.IsVisible = true;
                NoButton.IsVisible = true;
                YesButton.IsDefault = true;
                NoButton.IsCancel = true;
                break;
        }
    }

    private void OnOkClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _result = MessageBoxResult.Ok;
        Close();
    }

    private void OnCancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _result = MessageBoxResult.Cancel;
        Close();
    }

    private void OnYesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _result = MessageBoxResult.Yes;
        Close();
    }

    private void OnNoClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _result = MessageBoxResult.No;
        Close();
    }
}
