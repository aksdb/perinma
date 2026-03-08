using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using perinma.Messaging;
using perinma.Models;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Services.CardDAV;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Views.Settings.AddAccountWizard;

public partial class AddAccountWizardViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;
    private readonly CredentialManagerService _credentialManager;
    private readonly GoogleOAuthService _oauthService;
    private readonly ICalDavService _calDavService;
    private readonly ICardDavService _cardDavService;

    [ObservableProperty]
    private int _currentStepIndex = 0;

    [ObservableProperty]
    private object? _currentStepView;

    // Step 1 data
    private AccountDetailsStepViewModel? _accountDetailsStep;
    public string? AccountName { get; private set; }
    public AccountType? SelectedAccountType { get; private set; }

    // Step 2 data
    private GoogleConnectionStepViewModel? _googleConnectionStep;
    private CalDavConnectionStepViewModel? _calDavConnectionStep;
    private CardDavConnectionStepViewModel? _cardDavConnectionStep;

    // Computed properties
    public bool CanGoBack => CurrentStepIndex > 0;
    public bool IsLastStep => CurrentStepIndex == 1;

    // Event raised when account is successfully added
    public event EventHandler? AccountAdded;

    public AddAccountWizardViewModel(SqliteStorage storage, CredentialManagerService credentialManager, GoogleOAuthService oauthService, ICalDavService calDavService, ICardDavService cardDavService)
    {
        _storage = storage;
        _credentialManager = credentialManager;
        _oauthService = oauthService;
        _calDavService = calDavService;
        _cardDavService = cardDavService;

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
            SelectedAccountType = _accountDetailsStep.SelectedAccountType;

            // Create step 2 based on account type
            if (SelectedAccountType == AccountType.Google)
            {
                _googleConnectionStep = new GoogleConnectionStepViewModel(_oauthService);
                CurrentStepView = new GoogleConnectionStepView
                {
                    DataContext = _googleConnectionStep
                };
            }
            else if (SelectedAccountType == AccountType.CalDav)
            {
                _calDavConnectionStep = new CalDavConnectionStepViewModel(_calDavService);
                CurrentStepView = new CalDavConnectionStepView
                {
                    DataContext = _calDavConnectionStep
                };
            }
            else if (SelectedAccountType == AccountType.CardDav)
            {
                _cardDavConnectionStep = new CardDavConnectionStepViewModel(_cardDavService);
                CurrentStepView = new CardDavConnectionStepView
                {
                    DataContext = _cardDavConnectionStep
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
        if (SelectedAccountType == AccountType.Google)
        {
            if (_googleConnectionStep == null || !_googleConnectionStep.IsValid())
                return;
        }
        else if (SelectedAccountType == AccountType.CalDav)
        {
            if (_calDavConnectionStep == null || !_calDavConnectionStep.Validate())
                return;
        }
        else if (SelectedAccountType == AccountType.CardDav)
        {
            if (_cardDavConnectionStep == null || !_cardDavConnectionStep.Validate())
                return;
        }

        try
        {
            var accountId = Guid.NewGuid().ToString();

            // Store credentials in platform keyring
            if (SelectedAccountType == AccountType.Google && _googleConnectionStep != null)
            {
                var credentials = _googleConnectionStep.GetCredentials();
                if (credentials != null)
                {
                    _credentialManager.StoreGoogleCredentials(accountId, credentials);
                }
            }
            else if (SelectedAccountType == AccountType.CalDav && _calDavConnectionStep != null)
            {
                var credentials = _calDavConnectionStep.GetCredentials();
                _credentialManager.StoreCalDavCredentials(accountId, credentials);
            }
            else if (SelectedAccountType == AccountType.CardDav && _cardDavConnectionStep != null)
            {
                var credentials = _cardDavConnectionStep.GetCredentials();
                _credentialManager.StoreCardDavCredentials(accountId, credentials);
            }

            // Create account in database (without credentials)
            var accountDbo = new AccountDbo
            {
                AccountId = accountId,
                Name = AccountName ?? "Unnamed Account",
                Type = SelectedAccountType?.ToString() ?? "Google",
            };

            var success = await _storage.CreateAccountAsync(accountDbo);

            if (success)
            {
                // Raise event to notify AccountListViewModel
                AccountAdded?.Invoke(this, EventArgs.Empty);
                
                // Send message to notify CalendarListViewModel and other subscribers
                WeakReferenceMessenger.Default.Send(new AccountsChangedMessage());

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
