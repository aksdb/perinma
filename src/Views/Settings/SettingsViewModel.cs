using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Storage;
using perinma.Utils;
using perinma.ViewModels;

namespace perinma.Views.Settings;

public class SettingsPage
{
    public required string Name { get; init; }
    public required ViewModelBase ViewModel { get; init; }
}

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;

    public IReadOnlyList<SettingsPage> Pages { get; }

    [ObservableProperty]
    private SettingsPage? _selectedPage;

    public SettingsViewModel(DatabaseService databaseService, CredentialManagerService credentialManager)
    {
        _storage = new SqliteStorage(databaseService, credentialManager);

        // Define available settings pages
        Pages = new List<SettingsPage>
        {
            new SettingsPage { Name = "General", ViewModel = new GeneralSettingsViewModel() },
            new SettingsPage { Name = "Accounts", ViewModel = new AccountListViewModel(_storage, credentialManager) }
        };

        // Select first page by default
        SelectedPage = Pages[0];
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