using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Apis.Auth.OAuth2;
using perinma.Services;
using perinma.Storage.Models;
using perinma.Utils;

namespace perinma.Views.Settings;

public partial class ReauthenticateAccountViewModel : ViewModelBase
{
    private const string GoogleScope = "https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/calendar.events";

    private readonly string _accountId;
    private readonly CredentialManagerService _credentialManager;

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Click 'Reauthenticate' to start the authentication process";

    [ObservableProperty]
    private bool _isConnecting = false;

    private string? _expectedState;

    public event EventHandler? ReauthenticationCompleted;
    public event EventHandler? CloseRequested;

    public ReauthenticateAccountViewModel(string accountId, string accountName, CredentialManagerService credentialManager)
    {
        _accountId = accountId;
        AccountName = accountName;
        _credentialManager = credentialManager;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    public async Task Reauthenticate(CancellationToken ct)
    {
        IsConnecting = true;
        StatusMessage = "Starting authentication...";

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            await using var registration = ct.Register(() => tcs.TrySetCanceled());

            // Generate random state for CSRF protection
            _expectedState = Guid.NewGuid().ToString("N");
            GoogleCredentials? credential = null;

            // Start HTTP callback listener
            string? redirectUri = null;
            var callbackUrl = HttpUtil.StartHttpCallbackListener(async result =>
            {
                if (result.IsSuccess && result.Value != null)
                {
                    var queryParams = result.Value;

                    // Validate state parameter
                    var receivedState = queryParams["state"];
                    if (receivedState != _expectedState)
                    {
                        tcs.TrySetException(new InvalidOperationException("State mismatch - potential CSRF attack"));
                        return;
                    }

                    // Extract authorization code
                    var code = queryParams["code"];
                    if (string.IsNullOrEmpty(code))
                    {
                        tcs.TrySetException(new InvalidOperationException("No authorization code received"));
                        return;
                    }

                    try
                    {
                        // Create credentials with auth code
                        var creds = new GoogleCredentials
                        {
                            Type = "Google",
                            AuthorizationCode = code,
                            Scope = GoogleScope
                        };

                        // Immediately exchange authorization code for tokens
                        var googleService = new GoogleCalendarService();
                        await googleService.ExchangeAuthorizationCodeAsync(creds, ct, redirectUri);

                        // Get existing credentials to preserve any other data
                        credential = _credentialManager.GetGoogleCredentials(_accountId);

                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }
                else if (result.Error != null)
                {
                    tcs.TrySetException(result.Error);
                }
            }, ct);

            // Assign redirectUri after listener creation; the callback will run later
            redirectUri = callbackUrl;

            // Build OAuth URL
            var oauthUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                          $"client_id={Uri.EscapeDataString(BuildSecrets.GoogleClientId)}&" +
                          $"redirect_uri={Uri.EscapeDataString(callbackUrl)}&" +
                          $"response_type=code&" +
                          $"scope={Uri.EscapeDataString(GoogleScope)}&" +
                          $"state={Uri.EscapeDataString(_expectedState)}&" +
                          $"access_type=offline&" +
                          $"prompt=consent";

            StatusMessage = "Opening browser for authentication...";

            // Open browser (platform-specific)
            OpenBrowser(oauthUrl);

            StatusMessage = $"Waiting for authentication callback...\nIf the browser didn't open, navigate to:\n{callbackUrl}";

            await tcs.Task;

            // Store the updated credentials
            if (credential != null)
            {
                _credentialManager.StoreGoogleCredentials(_accountId, credential);
                StatusMessage = "Successfully reauthenticated! Credentials have been updated.";
                ReauthenticationCompleted?.Invoke(this, EventArgs.Empty);

                // Close the window after a short delay
                await Task.Delay(1500, ct);
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
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

    private static void OpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
        catch
        {
            // If browser opening fails, the user can still use the callback URL displayed in the status message
        }
    }
}
