using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Storage.Models;
using perinma.Utils;

namespace perinma.Views.Settings.AddAccountWizard;

public partial class GoogleConnectionStepViewModel : ViewModelBase
{
    private const string GoogleScope = "https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/calendar.events";

    [ObservableProperty]
    private string _statusMessage = "Click 'Connect' to authenticate with Google";

    [ObservableProperty]
    private bool _isConnecting = false;

    [ObservableProperty]
    private bool _isConnected = false;

    private GoogleCredentials? _credentials;
    private string? _expectedState;

    [RelayCommand(IncludeCancelCommand = true)]
    public async Task<bool> Connect(CancellationToken ct)
    {
        IsConnecting = true;
        StatusMessage = "Starting authentication...";

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            await using var registration = ct.Register(() => tcs.TrySetCanceled());

            // Generate random state for CSRF protection
            _expectedState = Guid.NewGuid().ToString("N");

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

                        // Immediately exchange authorization code for tokens and store them
                        var googleService = new GoogleCalendarService();
                        await googleService.ExchangeAuthorizationCodeAsync(creds, ct, redirectUri);

                        // Ensure we store tokens, not the authorization code
                        _credentials = creds;

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

    public GoogleCredentials? GetCredentials() => _credentials;

    public bool IsValid() => IsConnected && _credentials != null;
}
