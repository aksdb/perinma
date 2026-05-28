using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using AtomUI.Controls;
using AtomUI.Desktop.Controls;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using perinma.Messaging;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Services.CardDAV;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Utils;

namespace perinma.Views.Settings;

public sealed class SettingsPage : INavMenuNode
{
    public required string Name { get; init; }
    public required ViewModelBase ViewModel { get; init; }

    public object Header => Name;
    public PathIcon Icon => null!;
    public bool IsEnabled => true;
    public ITreeNode<INavMenuNode>? ParentNode { get; private set; }
    public IEnumerable<INavMenuNode> Children => [];
    public EntityKey? ItemKey => null;
    public IDataTemplate? HeaderTemplate => null;

    public void UpdateParentNode(INavMenuNode? parentNode)
    {
        ParentNode = parentNode;
    }
}

public partial class SettingsViewModel : ObservableRecipient,
    IRecipient<SyncAccountProgressMessage>,
    IRecipient<SyncCalendarProgressMessage>,
    IRecipient<SyncEventsProgressMessage>,
    IRecipient<SyncCompletedMessage>,
    IRecipient<SyncFailedMessage>
{
    private readonly SqliteStorage _storage;

    public IReadOnlyList<SettingsPage> Pages { get; }

    [ObservableProperty]
    private SettingsPage? _selectedPage;

    [ObservableProperty]
    private string _syncStatusText = string.Empty;

    public SettingsViewModel(DatabaseService databaseService, CredentialManagerService credentialManager, GoogleOAuthService oauthService, ICalDavService calDavService, ICardDavService cardDavService, SyncService syncService, Avalonia.Controls.Window parentWindow, SqliteStorage storage)
    {
        _storage = storage;
        var settingsService = new SettingsService(_storage);
        var contactSyncService = App.Services?.GetRequiredService<ContactSyncService>()
            ?? throw new InvalidOperationException("ContactSyncService not available");
        var mailSyncService = App.Services?.GetRequiredService<MailSyncService>()
            ?? throw new InvalidOperationException("MailSyncService not available");

        // Define available settings pages
        Pages = new List<SettingsPage>
        {
            new SettingsPage { Name = "General", ViewModel = new GeneralSettingsViewModel(settingsService) },
            new SettingsPage { Name = "Calendar", ViewModel = new CalendarSettingsViewModel(settingsService) },
            new SettingsPage { Name = "Accounts", ViewModel = new AccountListViewModel(_storage, credentialManager, oauthService, calDavService, cardDavService, syncService, contactSyncService, mailSyncService, parentWindow) }
        };

        // Select first page by default
        SelectedPage = Pages[0];

        // Enable message registration
        IsActive = true;
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

    public void Receive(SyncAccountProgressMessage message)
    {
        RunOnUiThread(() =>
            SyncStatusText = $"Syncing account {message.AccountIndex + 1} of {message.TotalAccounts}: {message.AccountName}");
    }

    public void Receive(SyncCalendarProgressMessage message)
    {
        RunOnUiThread(() =>
            SyncStatusText = $"  Syncing calendar {message.CalendarIndex + 1} of {message.TotalCalendars}: {message.CalendarName}");
    }

    public void Receive(SyncEventsProgressMessage message)
    {
        RunOnUiThread(() =>
            SyncStatusText = $"  Syncing events for {message.CalendarName} ({message.EventCount} events)...");
    }

    public void Receive(SyncCompletedMessage message)
    {
        RunOnUiThread(() =>
            SyncStatusText = $"Sync completed successfully. Synced {message.SyncedAccounts} accounts.");
    }

    public void Receive(SyncFailedMessage message)
    {
        RunOnUiThread(() =>
            SyncStatusText = $"Sync completed with {message.FailedAccounts} error(s).");
    }

    private void RunOnUiThread(Action action)
    {
        void Execute()
        {
            try
            {
                action();
            }
            catch (InvalidOperationException ex) when (IsThreadOwnershipViolation(ex))
            {
                WeakReferenceMessenger.Default.UnregisterAll(this);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Execute();
            return;
        }

        Dispatcher.UIThread.Post(Execute);
    }

    private static bool IsThreadOwnershipViolation(InvalidOperationException exception)
        => exception.Message.Contains("different thread owns it", StringComparison.OrdinalIgnoreCase);

}