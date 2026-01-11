using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Storage;
using perinma.Views.Settings.AddAccountWizard;

namespace perinma.Views.Settings;

public partial class AccountListViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;
    private readonly CredentialManagerService _credentialManager;
    private AddAccountWindow? _addAccountWindow;
    private ReauthenticateAccountWindow? _reauthenticateWindow;

    [ObservableProperty]
    private AvaloniaList<AccountViewModel> _accounts = [];

    [ObservableProperty]
    private bool _canReauthenticate = true;

    public AccountListViewModel(SqliteStorage storage, CredentialManagerService credentialManager)
    {
        _storage = storage;
        _credentialManager = credentialManager;
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

        var wizardVm = new AddAccountWizardViewModel(_storage, _credentialManager);
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
            // TODO: Show error to user
            Console.WriteLine($"Error loading accounts: {ex.Message}");
        }
    }

    private void OnAccountAdded(object? sender, EventArgs e)
    {
        _ = LoadAccountsAsync(); // Refresh list
    }

    [RelayCommand]
    private async Task DeleteAccount(Guid accountId)
    {
        try
        {
            var success = await _storage.DeleteAccountAsync(accountId.ToString());

            if (success)
            {
                // Remove from UI
                var account = Accounts.FirstOrDefault(a => a.Id == accountId);
                if (account != null)
                {
                    Accounts.Remove(account);
                }
            }
            else
            {
                // TODO: Show error to user
                Console.WriteLine($"Failed to delete account: {accountId}");
            }
        }
        catch (Exception ex)
        {
            // TODO: Show error to user
            Console.WriteLine($"Error deleting account: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ReauthenticateAccount(Guid accountId)
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
            return;
        }

        var reauthVm = new ReauthenticateAccountViewModel(accountId.ToString(), account.Name, _credentialManager);
        reauthVm.ReauthenticationCompleted += (_, _) =>
        {
            // Optionally trigger a sync or show a success message
            Console.WriteLine($"Account {account.Name} has been reauthenticated");
        };

        _reauthenticateWindow = new ReauthenticateAccountWindow
        {
            DataContext = reauthVm
        };
        _reauthenticateWindow.Closed += (_, _) =>
        {
            reauthVm.ReauthenticationCompleted -= (_, _) => { };
            _reauthenticateWindow = null;
            CanReauthenticate = true;
        };
        _reauthenticateWindow.Show();
        CanReauthenticate = false;
    }
}