using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using perinma.Storage.Models;

namespace perinma.Services;

public class GoogleCalendarService
{
    /// <summary>
    /// Creates a CalendarService from GoogleCredentials
    /// </summary>
    public async Task<CalendarService> CreateServiceAsync(GoogleCredentials credentials, CancellationToken cancellationToken = default)
    {
        // During sync, we should not exchange authorization codes. Only refresh tokens if needed.
        // Proactive refresh: if token is missing or near expiry, try to refresh using the refresh token.
        var needsRefresh = string.IsNullOrEmpty(credentials.AccessToken)
                           || credentials.ExpiresAt == null
                           || (credentials.ExpiresAt.Value - DateTime.UtcNow) <= TimeSpan.FromMinutes(2);

        if (needsRefresh && !string.IsNullOrEmpty(credentials.RefreshToken))
        {
            try
            {
                await RefreshAccessTokenAsync(credentials, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Access token refresh failed: {ex.Message}");
                // Continue; the following may still work if token is valid, otherwise callers can handle 401 and retry.
            }
        }

        var tokenResponse = new TokenResponse
        {
            AccessToken = credentials.AccessToken,
            RefreshToken = credentials.RefreshToken,
            ExpiresInSeconds = credentials.ExpiresAt.HasValue
                ? (long)(credentials.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds
                : 3600,
            TokenType = credentials.TokenType ?? "Bearer"
        };

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = BuildSecrets.GoogleClientId
            },
            Scopes = new[] { CalendarService.Scope.CalendarReadonly }
        });

        var credential = new UserCredential(flow, "user", tokenResponse);

        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "perinma"
        });
    }

    /// <summary>
    /// Exchanges authorization code for access and refresh tokens
    /// </summary>
    public async Task ExchangeAuthorizationCodeAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken,
        string? redirectUri = null)
    {
        if (string.IsNullOrEmpty(credentials.AuthorizationCode))
        {
            throw new InvalidOperationException("Authorization code is required");
        }

        var tokenRequest = new Dictionary<string, string>
        {
            ["code"] = credentials.AuthorizationCode,
            ["client_id"] = BuildSecrets.GoogleClientId,
            ["client_secret"] = BuildSecrets.GoogleClientSecret,
            // Must match the redirect URI used during auth
            ["redirect_uri"] = string.IsNullOrEmpty(redirectUri) ? "http://localhost:8080/callback" : redirectUri,
            ["grant_type"] = "authorization_code"
        };

        using var httpClient = new HttpClient();
        var response = await httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(tokenRequest),
            cancellationToken);

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Token exchange failed: {response.StatusCode}");
            Console.WriteLine($"Response body: {responseContent}");
            throw new InvalidOperationException(
                $"Token exchange failed with status {response.StatusCode}: {responseContent}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenExchangeResponse>(responseContent);

        if (tokenResponse == null)
        {
            throw new InvalidOperationException("Failed to parse token response");
        }

        // Update credentials with tokens
        credentials.AccessToken = tokenResponse.access_token;
        credentials.RefreshToken = tokenResponse.refresh_token;
        credentials.TokenType = tokenResponse.token_type;
        credentials.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in);
        // Clear the authorization code once exchanged
        credentials.AuthorizationCode = null;
    }

    /// <summary>
    /// Uses the refresh token to obtain a new access token. Does not modify the refresh token unless
    /// the server returns a new one (rare for Google).
    /// </summary>
    public async Task RefreshAccessTokenAsync(GoogleCredentials credentials, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(credentials.RefreshToken))
        {
            throw new InvalidOperationException("Refresh token is required to refresh access token");
        }

        var tokenRequest = new Dictionary<string, string>
        {
            ["client_id"] = BuildSecrets.GoogleClientId,
            ["client_secret"] = BuildSecrets.GoogleClientSecret,
            ["refresh_token"] = credentials.RefreshToken,
            ["grant_type"] = "refresh_token"
        };

        using var httpClient = new HttpClient();
        var response = await httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(tokenRequest),
            cancellationToken);

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Token refresh failed: {response.StatusCode}");
            Console.WriteLine($"Response body: {responseContent}");
            throw new InvalidOperationException(
                $"Token refresh failed with status {response.StatusCode}: {responseContent}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenExchangeResponse>(responseContent);
        if (tokenResponse == null)
        {
            throw new InvalidOperationException("Failed to parse token refresh response");
        }

        // Update credentials with new access token
        credentials.AccessToken = tokenResponse.access_token;
        credentials.TokenType = tokenResponse.token_type;
        if (tokenResponse.expires_in > 0)
        {
            credentials.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in);
        }
        // Some providers (not Google) might return a new refresh_token; keep if provided and non-empty
        if (!string.IsNullOrEmpty(tokenResponse.refresh_token))
        {
            credentials.RefreshToken = tokenResponse.refresh_token;
        }
    }

    /// <summary>
    /// Fetches all calendars for the authenticated user
    /// </summary>
    public async Task<IList<Google.Apis.Calendar.v3.Data.CalendarListEntry>> GetCalendarsAsync(
        CalendarService service,
        CancellationToken cancellationToken = default)
    {
        var request = service.CalendarList.List();
        var response = await request.ExecuteAsync(cancellationToken);
        return response.Items ?? new List<Google.Apis.Calendar.v3.Data.CalendarListEntry>();
    }

    private class TokenExchangeResponse
    {
        public string access_token { get; set; } = string.Empty;
        public string refresh_token { get; set; } = string.Empty;
        public int expires_in { get; set; }
        public string token_type { get; set; } = string.Empty;
    }
}
