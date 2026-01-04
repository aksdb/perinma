using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Storage;
using perinma.Utils;

namespace perinma.Views.Settings;

public partial class SettingsViewModel(DatabaseService databaseService) : ViewModelBase
{
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