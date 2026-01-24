using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using perinma.Storage.Models;

namespace perinma.Services.Google;

public class GoogleCalendarService : IGoogleCalendarService
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
                ClientId = BuildSecrets.GoogleClientId,
                ClientSecret = BuildSecrets.GoogleClientSecret,
            },
            Scopes = [CalendarService.Scope.CalendarCalendarlist]
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
        credentials.AccessToken = tokenResponse.AccessToken;
        credentials.RefreshToken = tokenResponse.RefreshToken;
        credentials.TokenType = tokenResponse.TokenType;
        credentials.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        // Clear the authorization code once exchanged
        credentials.AuthorizationCode = null;
    }

    /// <summary>
    /// Uses the refresh token to obtain a new access token. Does not modify the refresh token unless
    /// the server returns a new one (rare for Google).
    /// </summary>
    private async Task RefreshAccessTokenAsync(GoogleCredentials credentials, CancellationToken cancellationToken)
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
        credentials.AccessToken = tokenResponse.AccessToken;
        credentials.TokenType = tokenResponse.TokenType;
        if (tokenResponse.ExpiresIn > 0)
        {
            credentials.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        }
        // Some providers (not Google) might return a new refresh_token; keep if provided and non-empty
        if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
        {
            credentials.RefreshToken = tokenResponse.RefreshToken;
        }
    }

    /// <summary>
    /// Fetches calendars for the authenticated user, optionally using incremental sync
    /// </summary>
    /// <param name="service">Authenticated CalendarService</param>
    /// <param name="syncToken">Optional sync token for incremental sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing calendars and new sync token</returns>
    public async Task<CalendarSyncResult> GetCalendarsAsync(
        CalendarService service,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var allCalendars = new List<CalendarListEntry>();
        string? pageToken = null;
        string? newSyncToken = null;

        do
        {
            var request = service.CalendarList.List();
            request.MaxResults = 100; // Max allowed by Google API

            // Use sync token for incremental sync if provided
            if (!string.IsNullOrEmpty(syncToken))
            {
                request.SyncToken = syncToken;
            }

            // Handle pagination
            if (!string.IsNullOrEmpty(pageToken))
            {
                request.PageToken = pageToken;
            }

            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Items != null)
            {
                allCalendars.AddRange(response.Items);
            }

            pageToken = response.NextPageToken;
            newSyncToken = response.NextSyncToken;

        } while (!string.IsNullOrEmpty(pageToken));

        return new CalendarSyncResult
        {
            Calendars = allCalendars,
            SyncToken = newSyncToken
        };
    }

    /// <summary>
    /// Fetches events for a specific calendar, optionally using incremental sync
    /// </summary>
    /// <param name="service">Authenticated CalendarService</param>
    /// <param name="calendarId">Calendar ID to fetch events from</param>
    /// <param name="syncToken">Optional sync token for incremental sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing events and new sync token</returns>
    public async Task<EventSyncResult> GetEventsAsync(
        CalendarService service,
        string calendarId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var allEvents = new List<Event>();
        string? pageToken = null;
        string? newSyncToken = null;

        do
        {
            var request = service.Events.List(calendarId);
            request.MaxResults = 250; // Max allowed by Google API
            request.SingleEvents = false; // Get master recurring events with RRULE data

            // Use sync token for incremental sync if provided
            if (!string.IsNullOrEmpty(syncToken))
            {
                request.SyncToken = syncToken;
            }

            // Handle pagination
            if (!string.IsNullOrEmpty(pageToken))
            {
                request.PageToken = pageToken;
            }

            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Items != null)
            {
                allEvents.AddRange(response.Items);
            }

            pageToken = response.NextPageToken;
            newSyncToken = response.NextSyncToken;

        } while (!string.IsNullOrEmpty(pageToken));

        return new EventSyncResult
        {
            Events = allEvents,
            SyncToken = newSyncToken
        };
    }

    /// <summary>
    /// Updates a calendar's selected (enabled/disabled) state in Google Calendar
    /// </summary>
    /// <param name="service">Authenticated CalendarService</param>
    /// <param name="calendarId">External calendar ID (e.g., email address or calendar identifier)</param>
    /// <param name="selected">True to show the calendar, false to hide it</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task UpdateCalendarSelectedAsync(
        CalendarService service,
        string calendarId,
        bool selected,
        CancellationToken cancellationToken = default)
    {
        // Get the current calendar list entry
        var calendarListEntry = await service.CalendarList.Get(calendarId).ExecuteAsync(cancellationToken);

        // Update the Selected property
        calendarListEntry.Selected = selected;

        // Send the update to Google
        await service.CalendarList.Update(calendarListEntry, calendarId).ExecuteAsync(cancellationToken);
    }

    public class CalendarSyncResult
    {
        public required IList<CalendarListEntry> Calendars { get; init; }
        public string? SyncToken { get; init; }
    }

    public class EventSyncResult
    {
        public required IList<Event> Events { get; init; }
        public string? SyncToken { get; init; }
    }

    private class TokenExchangeResponse
    {
        [JsonPropertyName( "access_token")]
        public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName( "refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName( "expires_in")]
        public int ExpiresIn { get; set; }
        [JsonPropertyName( "token_type")]
        public string TokenType { get; set; } = string.Empty;
    }
}
