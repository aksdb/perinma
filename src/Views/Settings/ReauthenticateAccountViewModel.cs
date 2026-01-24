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
    private readonly string _accountName;
    private readonly CredentialManagerService _credentialManager;
    private readonly GoogleOAuthService _oauthService;

    [ObservableProperty]
    private string _statusMessage = "Click 'Reauthenticate' to start the authentication process";

    [ObservableProperty]
    private bool _isConnecting = false;

    public event EventHandler? ReauthenticationCompleted;
    public event EventHandler? CloseRequested;

    public ReauthenticateAccountViewModel(
        string accountId,
        string accountName,
        CredentialManagerService credentialManager,
        GoogleOAuthService oauthService)
    {
        _accountId = accountId;
        _accountName = accountName;
        _credentialManager = credentialManager;
        _oauthService = oauthService;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    public async Task Reauthenticate(CancellationToken ct)
    {
        IsConnecting = true;
        StatusMessage = "Starting authentication...";

        try
        {
            StatusMessage = "Opening browser for authentication...\nIf the browser didn't open, you may need to manually navigate to the OAuth URL.";

            // Authenticate with Google via the OAuth service
            var newCredentials = await _oauthService.AuthenticateAsync(ct);
            _credentialManager.StoreGoogleCredentials(_accountId, newCredentials);

            StatusMessage = "Successfully reauthenticated! Credentials have been updated.";
            ReauthenticationCompleted?.Invoke(this, EventArgs.Empty);

            // Close the window after a short delay
            await Task.Delay(1500, ct);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Reauthentication cancelled";
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
