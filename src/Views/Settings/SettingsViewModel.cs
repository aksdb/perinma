using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Storage;
using perinma.Utils;
using perinma.Views;

namespace perinma.ViewModels;

public partial class SettingsViewModel(DatabaseService databaseService) : ViewModelBase
{

    [ObservableProperty]
    private AvaloniaList<AccountSettings> _accounts = [];
    
    public enum AccountType
    {
        Google,
    }
    
    public abstract partial class AccountSettings : ViewModelBase
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private AccountType _type;
    }

    public partial class GoogleAccountSettings : AccountSettings
    {
        
    }

    [RelayCommand(IncludeCancelCommand = true)]
    public async Task<bool> WaitForHttp(CancellationToken c)
    {
        var tcs = new TaskCompletionSource<bool>();
        await using var registration = c.Register(() => tcs.TrySetCanceled());

        var url = HttpUtil.StartHttpCallbackListener(result =>
        {
            if (result.IsSuccess)
            {
                tcs.TrySetResult(true);
            }
            else
            {
                tcs.TrySetException(result.Error!);
            }
        }, c);
        
        Console.WriteLine($"Connect to: {url}");
        return await tcs.Task;
    }

    [RelayCommand]
    public void Abort()
    {
        WaitForHttpCommand.Cancel();
    }
    
}