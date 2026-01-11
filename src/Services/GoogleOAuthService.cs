using System;
using System.Threading;
using System.Threading.Tasks;
using perinma.Storage.Models;
using perinma.Utils;

namespace perinma.Services;

public class GoogleOAuthService
{
    private const string GoogleScope = "https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/calendar.events";

    private readonly GoogleCalendarService _googleCalendarService;

    public GoogleOAuthService(GoogleCalendarService googleCalendarService)
    {
        _googleCalendarService = googleCalendarService;
    }
    
    public async Task<GoogleCredentials> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<GoogleCredentials>();
        await using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());

        // Generate random state for CSRF protection
        var expectedState = Guid.NewGuid().ToString("N");

        // Start HTTP callback listener
        string? redirectUri = null;
        var callbackUrl = HttpUtil.StartHttpCallbackListener(async result =>
        {
            if (result.IsSuccess && result.Value != null)
            {
                var queryParams = result.Value;

                // Validate state parameter
                var receivedState = queryParams["state"];
                if (receivedState != expectedState)
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

                    // Exchange authorization code for tokens
                    await _googleCalendarService.ExchangeAuthorizationCodeAsync(creds, cancellationToken, redirectUri);

                    tcs.TrySetResult(creds);
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
        }, cancellationToken);

        // Assign redirectUri after listener creation; the callback will run later
        redirectUri = callbackUrl;

        // Build and open OAuth URL
        var oauthUrl = BuildOAuthUrl(callbackUrl, expectedState);
        PlatformUtil.OpenBrowser(oauthUrl);

        return await tcs.Task;
    }

    private static string BuildOAuthUrl(string redirectUri, string state)
    {
        return $"https://accounts.google.com/o/oauth2/v2/auth?" +
               $"client_id={Uri.EscapeDataString(BuildSecrets.GoogleClientId)}&" +
               $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
               $"response_type=code&" +
               $"scope={Uri.EscapeDataString(GoogleScope)}&" +
               $"state={Uri.EscapeDataString(state)}&" +
               $"access_type=offline&" +
               $"prompt=consent";
    }
}
