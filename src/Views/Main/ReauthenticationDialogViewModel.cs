using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage.Models;

namespace perinma.Views.Main;

public partial class ReauthenticationDialogViewModel : ViewModelBase
{
    private readonly string _accountId;
    private readonly string _accountName;
    private readonly CredentialManagerService _credentialManager;
    private readonly GoogleOAuthService _oauthService;
    private readonly GoogleCalendarService _googleCalendarService;
    private Task<GoogleCredentials>? _credentialsTask;

    [ObservableProperty]
    private string _statusMessage = "Preparing authentication link...";

    [ObservableProperty]
    private bool _isCompleted = false;

    [ObservableProperty]
    private bool _isError = false;

    [ObservableProperty]
    private string? _oAuthUrl = null;

    public string AccountName => _accountName;

    public event EventHandler? ReauthenticationCompleted;

    public ReauthenticationDialogViewModel(
        string accountId,
        string accountName,
        CredentialManagerService credentialManager,
        GoogleOAuthService oauthService,
        GoogleCalendarService googleCalendarService)
    {
        _accountId = accountId;
        _accountName = accountName;
        _credentialManager = credentialManager;
        _oauthService = oauthService;
        _googleCalendarService = googleCalendarService;

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        try
        {
            var (oauthUrl, credentialsTask) = await _oauthService.StartAuthenticationAsync();
            OAuthUrl = oauthUrl;
            _credentialsTask = credentialsTask;
            StatusMessage = "Click the link below to authenticate with your Google account.\nAfter authorizing, the window will close automatically.";
        }
        catch (Exception ex)
        {
            IsError = true;
            StatusMessage = $"Error preparing authentication: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CompleteAuthentication()
    {
        if (_credentialsTask == null)
        {
            StatusMessage = "Authentication not initialized properly.";
            return;
        }

        try
        {
            var newCredentials = await _credentialsTask;

            if (newCredentials != null)
            {
                _credentialManager.StoreGoogleCredentials(_accountId, newCredentials);
                IsCompleted = true;
                StatusMessage = "Authentication successful!";
                ReauthenticationCompleted?.Invoke(this, EventArgs.Empty);

                await Task.Delay(500);
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            IsError = true;
            StatusMessage = $"Authentication failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CloseRequested;
}
