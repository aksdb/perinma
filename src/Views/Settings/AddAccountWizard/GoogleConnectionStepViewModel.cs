using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage.Models;

namespace perinma.Views.Settings.AddAccountWizard;

public partial class GoogleConnectionStepViewModel : ViewModelBase
{
    private readonly GoogleOAuthService _oauthService;
    private GoogleCredentials? _credentials;

    [ObservableProperty]
    private string _statusMessage = "Click 'Connect' to authenticate with Google";

    [ObservableProperty]
    private bool _isConnecting = false;

    [ObservableProperty]
    private bool _isConnected = false;

    public GoogleConnectionStepViewModel(GoogleOAuthService oauthService)
    {
        _oauthService = oauthService;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    public async Task<bool> Connect(CancellationToken ct)
    {
        IsConnecting = true;
        StatusMessage = "Starting authentication...";

        try
        {
            StatusMessage = "Opening browser for authentication...\nIf the browser didn't open, you may need to manually navigate to the OAuth URL.";

            // Authenticate with Google via OAuth service
            _credentials = await _oauthService.AuthenticateAsync(ct);

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
