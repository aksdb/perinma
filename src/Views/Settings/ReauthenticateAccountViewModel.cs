using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage.Models;

namespace perinma.Views.Settings;

public partial class ReauthenticateAccountViewModel : ViewModelBase
{
    private readonly string _accountId;
    private readonly CredentialManagerService _credentialManager;
    private readonly GoogleOAuthService _oauthService;

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private bool _isConnecting;

    public event EventHandler? ReauthenticationCompleted;
    public event EventHandler? CloseRequested;

    public ReauthenticateAccountViewModel(
        string accountId,
        string accountName,
        CredentialManagerService credentialManager,
        GoogleOAuthService oauthService,
        bool needsMailUpgrade)
    {
        _accountId = accountId;
        AccountName = accountName;
        _credentialManager = credentialManager;
        _oauthService = oauthService;
        NeedsMailUpgrade = needsMailUpgrade;
        StatusMessage = needsMailUpgrade
            ? "Click 'Grant Mail Access' to reconnect your Google account and add Gmail permissions."
            : "Click 'Reauthenticate' to start the authentication process.";
    }

    public string AccountName { get; }
    public bool NeedsMailUpgrade { get; }
    public string WindowTitle => NeedsMailUpgrade ? "Enable Mail Access" : "Reauthenticate Account";
    public string ActionLabel => NeedsMailUpgrade ? "Grant Mail Access" : "Reauthenticate";
    public string IntroText => NeedsMailUpgrade
        ? "This Google account needs Gmail permissions before it can be used in Mail."
        : "You need to reauthenticate your Google account.";
    public string DetailText => NeedsMailUpgrade
        ? "This updates the OAuth grant so the same Google account can sync calendar, contacts, and mail."
        : "This may be necessary if your access token expired or if you need to adjust permissions.";

    [RelayCommand(IncludeCancelCommand = true)]
    public async Task Reauthenticate(CancellationToken ct)
    {
        IsConnecting = true;
        StatusMessage = NeedsMailUpgrade
            ? "Opening browser to grant Gmail permissions..."
            : "Opening browser for authentication...";

        try
        {
            var newCredentials = await _oauthService.AuthenticateAsync(ct);
            _credentialManager.StoreGoogleCredentials(_accountId, newCredentials);

            StatusMessage = NeedsMailUpgrade
                ? "Mail access enabled. Credentials have been updated."
                : "Successfully reauthenticated! Credentials have been updated.";
            ReauthenticationCompleted?.Invoke(this, EventArgs.Empty);

            await Task.Delay(1500, ct);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = NeedsMailUpgrade
                ? "Mail access update cancelled"
                : "Reauthentication cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
