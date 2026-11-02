using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage.Models;

namespace perinma.Views.Settings.AddAccountWizard;

public partial class GoogleConnectionStepViewModel : ViewModelBase
{
    private readonly GoogleOAuthService _oauthService;
    private readonly AccountCapability _capabilities;
    private GoogleCredentials? _credentials;
    private Task<GoogleCredentials>? _credentialsTask;

    [ObservableProperty]
    private string _statusMessage = "Click 'Connect' to authenticate with Google";

    [ObservableProperty]
    private bool _isConnecting = false;

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private string? _oAuthUrl = null;

    public GoogleConnectionStepViewModel(GoogleOAuthService oauthService, AccountCapability capabilities)
    {
        _oauthService = oauthService;
        _capabilities = capabilities;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    public async Task<bool> Connect(CancellationToken ct)
    {
        IsConnecting = true;
        StatusMessage = "Starting authentication...";

        try
        {
            // Start authentication process to get the OAuth URL
            var (oauthUrl, credentialsTask) = await _oauthService.StartAuthenticationAsync(_capabilities, ct);
            OAuthUrl = oauthUrl;
            _credentialsTask = credentialsTask;

            StatusMessage = "Click the link below to authenticate with Google.\nAfter authorizing, return here to complete the connection.";

            // Wait for the authentication to complete
            _credentials = await _credentialsTask;

            IsConnected = true;
            StatusMessage = "Successfully connected to Google!";
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Connection cancelled";
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            return false;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    public GoogleCredentials? GetCredentials() => _credentials;

    public bool IsValid() => IsConnected && _credentials != null;
}
