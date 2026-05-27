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
    private int _currentStepIndex;

    [ObservableProperty]
    private object? _currentStepView;

    private AccountDetailsStepViewModel? _accountDetailsStep;
    private GoogleConnectionStepViewModel? _googleConnectionStep;
    private CalDavConnectionStepViewModel? _calDavConnectionStep;
    private CardDavConnectionStepViewModel? _cardDavConnectionStep;
    private JmapConnectionStepViewModel? _jmapConnectionStep;

    public string? AccountName { get; private set; }
    public AccountType? SelectedAccountType { get; private set; }
    public AccountCapability SelectedCapabilities { get; private set; }

    public bool CanGoBack => CurrentStepIndex > 0;
    public bool IsLastStep => CurrentStepIndex == 1;

    public event EventHandler? AccountAdded;
    public event EventHandler? CloseRequested;

    public AddAccountWizardViewModel(
        SqliteStorage storage,
        CredentialManagerService credentialManager,
        GoogleOAuthService oauthService,
        ICalDavService calDavService,
        ICardDavService cardDavService)
    {
        _storage = storage;
        _credentialManager = credentialManager;
        _oauthService = oauthService;
        _calDavService = calDavService;
        _cardDavService = cardDavService;

        _accountDetailsStep = new AccountDetailsStepViewModel(storage);
        CurrentStepView = new AccountDetailsStepView
        {
            DataContext = _accountDetailsStep
        };
    }

    [RelayCommand]
    private async Task Next()
    {
        if (CurrentStepIndex != 0)
        {
            return;
        }

        if (_accountDetailsStep == null || !await _accountDetailsStep.ValidateAsync())
        {
            return;
        }

        AccountName = _accountDetailsStep.AccountName;
        SelectedAccountType = _accountDetailsStep.SelectedAccountType;
        SelectedCapabilities = _accountDetailsStep.SelectedCapabilities;
        CurrentStepView = CreateConnectionStepView(SelectedAccountType.Value);
        CurrentStepIndex = 1;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsLastStep));
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStepIndex == 0)
        {
            return;
        }

        CurrentStepIndex = 0;
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

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Finish(CancellationToken ct)
    {
        if (SelectedAccountType == null || !ValidateConnectionStep())
        {
            return;
        }

        var accountId = Guid.NewGuid().ToString();

        try
        {
            StoreCredentials(accountId);

            var accountDbo = new AccountDbo
            {
                AccountId = accountId,
                Name = AccountName ?? "Unnamed Account",
                Type = SelectedAccountType.Value.ToString(),
                Capabilities = (int)SelectedCapabilities,
            };

            var success = await _storage.CreateAccountAsync(accountDbo);
            if (!success)
            {
                _credentialManager.DeleteCredentials(accountId);
                return;
            }

            AccountAdded?.Invoke(this, EventArgs.Empty);
            WeakReferenceMessenger.Default.Send(new AccountsChangedMessage());
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _credentialManager.DeleteCredentials(accountId);
            Console.WriteLine($"Error creating account: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private object CreateConnectionStepView(AccountType accountType)
    {
        switch (accountType)
        {
            case AccountType.Google:
                _googleConnectionStep = new GoogleConnectionStepViewModel(_oauthService);
                return new GoogleConnectionStepView
                {
                    DataContext = _googleConnectionStep
                };
            case AccountType.CalDav:
                _calDavConnectionStep = new CalDavConnectionStepViewModel(_calDavService);
                return new CalDavConnectionStepView
                {
                    DataContext = _calDavConnectionStep
                };
            case AccountType.CardDav:
                _cardDavConnectionStep = new CardDavConnectionStepViewModel(_cardDavService);
                return new CardDavConnectionStepView
                {
                    DataContext = _cardDavConnectionStep
                };
            case AccountType.Jmap:
                _jmapConnectionStep = new JmapConnectionStepViewModel();
                return new JmapConnectionStepView
                {
                    DataContext = _jmapConnectionStep
                };
            default:
                throw new InvalidOperationException($"Unsupported account type: {accountType}");
        }
    }

    private bool ValidateConnectionStep()
    {
        return SelectedAccountType switch
        {
            AccountType.Google => _googleConnectionStep != null && _googleConnectionStep.IsValid(),
            AccountType.CalDav => _calDavConnectionStep != null && _calDavConnectionStep.Validate(),
            AccountType.CardDav => _cardDavConnectionStep != null && _cardDavConnectionStep.Validate(),
            AccountType.Jmap => _jmapConnectionStep != null && _jmapConnectionStep.Validate(),
            _ => false,
        };
    }

    private void StoreCredentials(string accountId)
    {
        switch (SelectedAccountType)
        {
            case AccountType.Google when _googleConnectionStep?.GetCredentials() is { } googleCredentials:
                _credentialManager.StoreGoogleCredentials(accountId, googleCredentials);
                break;
            case AccountType.CalDav when _calDavConnectionStep != null:
                _credentialManager.StoreCalDavCredentials(accountId, _calDavConnectionStep.GetCredentials());
                break;
            case AccountType.CardDav when _cardDavConnectionStep != null:
                _credentialManager.StoreCardDavCredentials(accountId, _cardDavConnectionStep.GetCredentials());
                break;
            case AccountType.Jmap when _jmapConnectionStep != null:
                _credentialManager.StoreJmapCredentials(accountId, _jmapConnectionStep.GetCredentials());
                break;
        }
    }
}
