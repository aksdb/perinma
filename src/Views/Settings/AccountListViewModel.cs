using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Storage;
using perinma.Views.MessageBox;
using perinma.Views.Settings.AddAccountWizard;

namespace perinma.Views.Settings;

public partial class AccountListViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;
    private readonly CredentialManagerService _credentialManager;
    private readonly GoogleOAuthService _oauthService;
    private readonly ICalDavService _calDavService;
    private readonly SyncService _syncService;
    private readonly Window _parentWindow;
    private AddAccountWindow? _addAccountWindow;
    private ReauthenticateAccountWindow? _reauthenticateWindow;

    [ObservableProperty]
    private AvaloniaList<AccountViewModel> _accounts = [];

    [ObservableProperty]
    private bool _canReauthenticate = true;

    public AccountListViewModel(SqliteStorage storage, CredentialManagerService credentialManager, GoogleOAuthService oauthService, ICalDavService calDavService, SyncService syncService, Window parentWindow)
    {
        _storage = storage;
        _credentialManager = credentialManager;
        _oauthService = oauthService;
        _calDavService = calDavService;
        _syncService = syncService;
        _parentWindow = parentWindow;
        _ = LoadAccountsAsync(); // Fire and forget initial load
    }

    [RelayCommand]
    private void AddAccount()
    {
        if (_addAccountWindow != null)
        {
            _addAccountWindow.Activate();
            return;
        }

        var wizardVm = new AddAccountWizardViewModel(_storage, _credentialManager, _oauthService, _calDavService);
        wizardVm.AccountAdded += OnAccountAdded;

        _addAccountWindow = new AddAccountWindow
        {
            DataContext = wizardVm
        };
        _addAccountWindow.Closed += (_, _) =>
        {
            wizardVm.AccountAdded -= OnAccountAdded;
            _addAccountWindow = null;
        };
        _addAccountWindow.Show();
    }

    private async Task LoadAccountsAsync()
    {
        try
        {
            var dbAccounts = await _storage.GetAllAccountsAsync();
            Accounts.Clear();
            foreach (var dbo in dbAccounts)
            {
                Accounts.Add(new AccountViewModel
                {
                    Id = Guid.Parse(dbo.AccountId),
                    Name = dbo.Name,
                    Type = Enum.Parse<AccountType>(dbo.Type, ignoreCase: true)
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading accounts: {ex.Message}");
            await MessageBoxWindow.ShowAsync(
                _parentWindow,
                "Error",
                $"Failed to load accounts: {ex.Message}",
                MessageBoxType.Error,
                MessageBoxButtons.Ok);
        }
    }

    private void OnAccountAdded(object? sender, EventArgs e)
    {
        _ = LoadAccountsAsync(); // Refresh list
    }

    [RelayCommand]
    private async Task DeleteAccount(Guid accountId)
    {
        var account = Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account == null)
        {
            Console.WriteLine($"Account not found: {accountId}");
            return;
        }

        var result = await MessageBoxWindow.ShowAsync(
            _parentWindow,
            "Delete Account",
            $"Are you sure you want to delete the account \"{account.Name}\"?\n\nThis will remove all calendars and events associated with this account.",
            MessageBoxType.Confirmation,
            MessageBoxButtons.YesNo);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var success = await _storage.DeleteAccountAsync(accountId.ToString());

            if (success)
            {
                Accounts.Remove(account);
            }
            else
            {
                Console.WriteLine($"Failed to delete account: {accountId}");
                await MessageBoxWindow.ShowAsync(
                    _parentWindow,
                    "Error",
                    $"Failed to delete account \"{account.Name}\".",
                    MessageBoxType.Error,
                    MessageBoxButtons.Ok);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting account: {ex.Message}");
            await MessageBoxWindow.ShowAsync(
                _parentWindow,
                "Error",
                $"Failed to delete account: {ex.Message}",
                MessageBoxType.Error,
                MessageBoxButtons.Ok);
        }
    }

    [RelayCommand]
    private async Task ReauthenticateAccount(Guid accountId)
    {
        if (_reauthenticateWindow != null)
        {
            _reauthenticateWindow.Activate();
            return;
        }

        // Find the account
        var account = Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account == null)
        {
            Console.WriteLine($"Account not found: {accountId}");
            return;
        }

        // Only Google accounts support OAuth reauthentication
        if (account.Type != AccountType.Google)
        {
            Console.WriteLine($"Only Google accounts support OAuth reauthentication. Account type: {account.Type}");
            await MessageBoxWindow.ShowAsync(
                _parentWindow,
                "Not Supported",
                "Reauthentication is only supported for Google accounts.",
                MessageBoxType.Information,
                MessageBoxButtons.Ok);
            return;
        }

        var reauthVm = new ReauthenticateAccountViewModel(accountId.ToString(), account.Name, _credentialManager, _oauthService);
        EventHandler onReauthenticateFinished = (_, _) =>
        {
            // Optionally trigger a sync or show a success message
            Console.WriteLine($"Account {account.Name} has been reauthenticated");
        };
        reauthVm.ReauthenticationCompleted += onReauthenticateFinished;

        _reauthenticateWindow = new ReauthenticateAccountWindow
        {
            DataContext = reauthVm
        };
        _reauthenticateWindow.Closed += (_, _) =>
        {
            reauthVm.ReauthenticationCompleted -= onReauthenticateFinished;
            _reauthenticateWindow = null;
            CanReauthenticate = true;
        };
        _reauthenticateWindow.Show();
        CanReauthenticate = false;
    }

    [RelayCommand]
    private async Task ForceResync(Guid accountId)
    {
        var account = Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account == null)
        {
            Console.WriteLine($"Account not found: {accountId}");
            return;
        }

        try
        {
            Console.WriteLine($"Force resyncing account: {account.Name}");
            var result = await _syncService.ForceResyncAccountAsync(accountId.ToString());

            if (result.Success)
            {
                Console.WriteLine($"Force resync completed for account: {account.Name}");
            }
            else
            {
                Console.WriteLine($"Force resync failed for account: {account.Name}");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"  - {error}");
                }

                var errorDetails = string.Join("\n", result.Errors);
                await MessageBoxWindow.ShowAsync(
                    _parentWindow,
                    "Sync Failed",
                    $"Failed to resync account \"{account.Name}\":\n\n{errorDetails}",
                    MessageBoxType.Error,
                    MessageBoxButtons.Ok);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during force resync: {ex.Message}");
            await MessageBoxWindow.ShowAsync(
                _parentWindow,
                "Sync Failed",
                $"Failed to resync account: {ex.Message}",
                MessageBoxType.Error,
                MessageBoxButtons.Ok);
        }
    }
}