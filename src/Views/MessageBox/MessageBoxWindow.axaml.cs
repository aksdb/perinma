using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;

namespace perinma.Views.MessageBox;

public partial class MessageBoxWindow : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;

    public MessageBoxWindow()
    {
        InitializeComponent();
    }

    public static Task<MessageBoxResult> ShowAsync(Window? owner, string title, string message, MessageBoxType type, MessageBoxButtons buttons)
    {
        var messageBox = new MessageBoxWindow();
        messageBox.Title = title;
        messageBox.MessageText.Text = message;
        messageBox.ConfigureType(type);
        messageBox.ConfigureButtons(buttons);

        var tcs = new TaskCompletionSource<MessageBoxResult>();

        messageBox.Closed += (_, _) => tcs.TrySetResult(messageBox._result);

        messageBox.ShowDialog(owner!);

        return tcs.Task;
    }

    private void ConfigureType(MessageBoxType type)
    {
        switch (type)
        {
            case MessageBoxType.Information:
                IconBorder.Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)); // Blue
                IconText.Text = "i";
                IconText.Foreground = Brushes.White;
                break;
            case MessageBoxType.Confirmation:
                IconBorder.Background = new SolidColorBrush(Color.FromRgb(16, 124, 16)); // Green
                IconText.Text = "?";
                IconText.Foreground = Brushes.White;
                break;
            case MessageBoxType.Warning:
                IconBorder.Background = new SolidColorBrush(Color.FromRgb(255, 185, 0)); // Yellow/Orange
                IconText.Text = "!";
                IconText.Foreground = Brushes.Black;
                break;
            case MessageBoxType.Error:
                IconBorder.Background = new SolidColorBrush(Color.FromRgb(196, 43, 28)); // Red
                IconText.Text = "X";
                IconText.Foreground = Brushes.White;
                break;
        }
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
