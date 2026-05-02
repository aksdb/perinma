using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace perinma.Views.Calendar.EventEdit;

public partial class SendInvitesDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _sendToAll = true;

    [ObservableProperty]
    private bool _sendToExternalOnly;

    [ObservableProperty]
    private bool _sendToNone;

    public event Action<SendInvitesResult>? OkRequested;

    partial void OnSendToAllChanged(bool value)
    {
        if (value)
        {
            SendToExternalOnly = false;
            SendToNone = false;
        }
    }

    partial void OnSendToExternalOnlyChanged(bool value)
    {
        if (value)
        {
            SendToAll = false;
            SendToNone = false;
        }
    }

    partial void OnSendToNoneChanged(bool value)
    {
        if (value)
        {
            SendToAll = false;
            SendToExternalOnly = false;
        }
    }

    [RelayCommand]
    private void Ok()
    {
        var result = SendToAll ? SendInvitesResult.SendToAll :
                      SendToExternalOnly ? SendInvitesResult.SendToExternalOnly :
                      SendInvitesResult.SendToNone;
        OkRequested?.Invoke(result);
    }

    [RelayCommand]
    private void Cancel()
    {
        OkRequested?.Invoke(SendInvitesResult.SendToNone);
    }
}
