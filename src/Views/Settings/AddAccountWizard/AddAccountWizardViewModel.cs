using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Views.Settings.AddAccountWizard;

public partial class AddAccountWizardViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;
    private readonly CredentialManagerService _credentialManager;
    private readonly GoogleOAuthService _oauthService;

    [ObservableProperty]
    private int _currentStepIndex = 0;

    [ObservableProperty]
    private object? _currentStepView;

    // Step 1 data
    private AccountDetailsStepViewModel? _accountDetailsStep;
    public string? AccountName { get; private set; }
    public AccountType? AccountType { get; private set; }

    // Step 2 data
    private GoogleConnectionStepViewModel? _googleConnectionStep;
    private CalDavConnectionStepViewModel? _calDavConnectionStep;

    // Computed properties
    public bool CanGoBack => CurrentStepIndex > 0;
    public bool IsLastStep => CurrentStepIndex == 1;

    // Event raised when account is successfully added
    public event EventHandler? AccountAdded;

    public AddAccountWizardViewModel(SqliteStorage storage, CredentialManagerService credentialManager, GoogleOAuthService oauthService)
    {
        _storage = storage;
        _credentialManager = credentialManager;
        _oauthService = oauthService;

        // Initialize first step
        _accountDetailsStep = new AccountDetailsStepViewModel(storage);
        CurrentStepView = new AccountDetailsStepView
        {
            DataContext = _accountDetailsStep
        };
    }

    [RelayCommand]
    private async Task Next()
    {
        if (CurrentStepIndex == 0)
        {
            // Validate step 1
            if (_accountDetailsStep == null || !await _accountDetailsStep.ValidateAsync())
                return;

            // Save data from step 1
            AccountName = _accountDetailsStep.AccountName;
            AccountType = _accountDetailsStep.SelectedAccountType;

            // Create step 2 based on account type
            if (AccountType == Settings.AccountType.Google)
            {
                _googleConnectionStep = new GoogleConnectionStepViewModel(_oauthService);
                CurrentStepView = new GoogleConnectionStepView
                {
                    DataContext = _googleConnectionStep
                };
            }
            else if (AccountType == Settings.AccountType.CalDav)
            {
                _calDavConnectionStep = new CalDavConnectionStepViewModel();
                CurrentStepView = new CalDavConnectionStepView
                {
                    DataContext = _calDavConnectionStep
                };
            }

            CurrentStepIndex = 1;
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(IsLastStep));
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStepIndex > 0)
        {
            CurrentStepIndex = 0;

            // Return to step 1
            if (_accountDetailsStep != null)
            {
                CurrentStepView = new AccountDetailsStepView
                {
                    DataContext = _accountDetailsStep
                };
            }

            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(IsLastStep));
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Finish(CancellationToken ct)
    {
        // Validate step 2
        if (AccountType == Settings.AccountType.Google)
        {
            if (_googleConnectionStep == null || !_googleConnectionStep.IsValid())
                return;
        }
        else if (AccountType == Settings.AccountType.CalDav)
        {
            if (_calDavConnectionStep == null || !_calDavConnectionStep.Validate())
                return;
        }

        try
        {
            var accountId = Guid.NewGuid().ToString();

            // Store credentials in platform keyring
            if (AccountType == Settings.AccountType.Google && _googleConnectionStep != null)
            {
                var credentials = _googleConnectionStep.GetCredentials();
                if (credentials != null)
                {
                    _credentialManager.StoreGoogleCredentials(accountId, credentials);
                }
            }
            else if (AccountType == Settings.AccountType.CalDav && _calDavConnectionStep != null)
            {
                var credentials = _calDavConnectionStep.GetCredentials();
                _credentialManager.StoreCalDavCredentials(accountId, credentials);
            }

            // Create account in database (without credentials)
            var accountDbo = new AccountDbo
            {
                AccountId = accountId,
                Name = AccountName ?? "Unnamed Account",
                Type = AccountType?.ToString() ?? "Google",
            };

            var success = await _storage.CreateAccountAsync(accountDbo);

            if (success)
            {
                // Raise event to notify AccountListViewModel
                AccountAdded?.Invoke(this, EventArgs.Empty);

                // Close window (will be handled by window code-behind)
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                // If account creation failed, clean up credentials
                _credentialManager.DeleteCredentials(accountId);
            }
        }
        catch (Exception ex)
        {
            // TODO: Show error dialog
            Console.WriteLine($"Error creating account: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Event for window to subscribe to
    public event EventHandler? CloseRequested;
}
