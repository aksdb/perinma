using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace perinma.Views.MessageBox;

public partial class MessageBoxWindow : AtomUI.Desktop.Controls.Window
{
    private static readonly SolidColorBrush InformationBrush = new(Color.FromRgb(0, 120, 212));
    private static readonly SolidColorBrush ConfirmationBrush = new(Color.FromRgb(16, 124, 16));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(255, 185, 0));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(196, 43, 28));
    private static readonly SolidColorBrush LightIconBrush = new(Colors.White);
    private static readonly SolidColorBrush DarkIconBrush = new(Colors.Black);

    private const string InformationIconPath = "M10,2A8,8 0 1 1 2,10A8,8 0 0 1 10,2M10,6A1,1 0 1 0 10,8A1,1 0 0 0 10,6M9,9H11V14H9Z";
    private const string ConfirmationIconPath = "M10,2A8,8 0 1 1 2,10A8,8 0 0 1 10,2M8.75,7.5A1.75,1.75 0 1 1 12.25,7.5C12.25,8.47 11.63,9.06 10.91,9.48C10.27,9.85 10,10.11 10,10.75V11H8.75V10.62C8.75,9.44 9.3,8.88 10.14,8.38C10.81,7.99 11,7.73 11,7.5A0.75,0.75 0 1 0 9.5,7.5H8.75M9,12.5H11V14H9Z";
    private const string WarningIconPath = "M10,2L18,17H2L10,2M9,7V11H11V7H9M9,13V15H11V13H9Z";
    private const string ErrorIconPath = "M10,2A8,8 0 1 1 2,10A8,8 0 0 1 10,2M6.7,5.3L5.3,6.7L8.59,10L5.3,13.3L6.7,14.7L10,11.41L13.3,14.7L14.7,13.3L11.41,10L14.7,6.7L13.3,5.3L10,8.59L6.7,5.3Z";

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

        if (owner != null)
        {
            messageBox.ShowDialog(owner);
        }
        else
        {
            messageBox.Show();
        }

        return tcs.Task;
    }

    private void ConfigureType(MessageBoxType type)
    {
        var (backgroundBrush, iconBrush, iconPath) = type switch
        {
            MessageBoxType.Information => (InformationBrush, LightIconBrush, InformationIconPath),
            MessageBoxType.Confirmation => (ConfirmationBrush, LightIconBrush, ConfirmationIconPath),
            MessageBoxType.Warning => (WarningBrush, DarkIconBrush, WarningIconPath),
            MessageBoxType.Error => (ErrorBrush, LightIconBrush, ErrorIconPath),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

        IconHost.Background = backgroundBrush;
        TypeIconPresenter.IconBrush = iconBrush;
        TypeIconPresenter.Icon = new PathIcon
        {
            Data = StreamGeometry.Parse(iconPath)
        };
    }

    private void ConfigureButtons(MessageBoxButtons buttons)
    {
        ResetButton(OkButton);
        ResetButton(CancelButton);
        ResetButton(YesButton);
        ResetButton(NoButton);

        switch (buttons)
        {
            case MessageBoxButtons.Ok:
                ShowPrimaryButton(OkButton);
                break;
            case MessageBoxButtons.OkCancel:
                ShowPrimaryButton(OkButton);
                ShowSecondaryButton(CancelButton, isCancel: true);
                break;
            case MessageBoxButtons.YesNo:
                ShowPrimaryButton(YesButton);
                ShowSecondaryButton(NoButton, isCancel: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(buttons), buttons, null);
        }
    }

    private static void ResetButton(AtomUI.Desktop.Controls.Button button)
    {
        button.IsVisible = false;
        button.IsDefault = false;
        button.IsCancel = false;
        button.ButtonType = AtomUI.Desktop.Controls.ButtonType.Default;
    }

    private static void ShowPrimaryButton(AtomUI.Desktop.Controls.Button button)
    {
        button.IsVisible = true;
        button.IsDefault = true;
        button.ButtonType = AtomUI.Desktop.Controls.ButtonType.Primary;
    }

    private static void ShowSecondaryButton(AtomUI.Desktop.Controls.Button button, bool isCancel = false)
    {
        button.IsVisible = true;
        button.IsCancel = isCancel;
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Ok;
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Cancel;
        Close();
    }

    private void OnYesClicked(object? sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Yes;
        Close();
    }

    private void OnNoClicked(object? sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.No;
        Close();
    }
}
