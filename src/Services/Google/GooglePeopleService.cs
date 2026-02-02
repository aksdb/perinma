using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;
using Google.Apis.Services;
using perinma.Storage.Models;

namespace perinma.Services.Google;

public class GooglePeopleService : IGooglePeopleService
{
    // Fields to request from the People API
    private const string PersonFields = "names,emailAddresses,phoneNumbers,addresses,photos,memberships";
    private const string GroupFields = "name,groupType,memberCount";

    private sealed record CombinedSyncToken(string? Personal, string? Directory);

    /// <summary>
    /// Creates a PeopleServiceService from GoogleCredentials
    /// </summary>
    public async Task<PeopleServiceService> CreateServiceAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        // Proactive refresh: if token is missing or near expiry, try to refresh using the refresh token.
        var needsRefresh = string.IsNullOrEmpty(credentials.AccessToken)
                           || credentials.ExpiresAt == null
                           || (credentials.ExpiresAt.Value - DateTime.UtcNow) <= TimeSpan.FromMinutes(2);

        if (needsRefresh && !string.IsNullOrEmpty(credentials.RefreshToken))
        {
            try
            {
                await RefreshAccessTokenAsync(credentials, cancellationToken, accountId);
            }
            catch (ReAuthenticationRequiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Access token refresh failed: {ex.Message}");
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
            Scopes = [PeopleServiceService.Scope.ContactsReadonly]
        });

        var credential = new UserCredential(flow, "user", tokenResponse);

        return new PeopleServiceService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "perinma"
        });
    }

    /// <summary>
    /// Uses the refresh token to obtain a new access token.
    /// </summary>
    private async Task RefreshAccessTokenAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken,
        string? accountId = null)
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

            if (!string.IsNullOrEmpty(accountId))
            {
                throw new ReAuthenticationRequiredException("Google", accountId,
                    $"Token refresh failed with status {response.StatusCode}: {responseContent}");
            }

            throw new InvalidOperationException(
                $"Token refresh failed with status {response.StatusCode}: {responseContent}");
        }

        var tokenResponse = JsonSerializer.Deserialize(responseContent, GoogleCalendarContext.Default.TokenExchangeResponse);
        if (tokenResponse == null)
        {
            throw new InvalidOperationException("Failed to parse token refresh response");
        }

        credentials.AccessToken = tokenResponse.AccessToken;
        credentials.TokenType = tokenResponse.TokenType;
        if (tokenResponse.ExpiresIn > 0)
        {
            credentials.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        }
        if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
        {
            credentials.RefreshToken = tokenResponse.RefreshToken;
        }
    }

    /// <summary>
    /// Creates a combined sync token from personal and directory sync tokens
    /// </summary>
    private string? CreateCombinedSyncToken(string? personalSyncToken, string? directorySyncToken)
    {
        if (string.IsNullOrEmpty(personalSyncToken) && string.IsNullOrEmpty(directorySyncToken))
        {
            return null;
        }

        var combined = new CombinedSyncToken(personalSyncToken, directorySyncToken);
        return JsonSerializer.Serialize(combined);
    }

    /// <summary>
    /// Parses a combined sync token into personal and directory sync tokens
    /// </summary>
    private (string? PersonalSyncToken, string? DirectorySyncToken) ParseCombinedSyncToken(string? combinedToken)
    {
        if (string.IsNullOrEmpty(combinedToken))
        {
            return (null, null);
        }

        try
        {
            var combined = JsonSerializer.Deserialize<CombinedSyncToken>(combinedToken);
            if (combined != null)
            {
                return (combined.Personal, combined.Directory);
            }
        }
        catch (JsonException)
        {
            // Not a JSON token, treat as legacy personal-only token
            return (combinedToken, null);
        }

        return (null, null);
    }

    /// <summary>
    /// Fetches all contacts for the authenticated user, including personal contacts and directory contacts
    /// </summary>
    public async Task<ContactSyncResult> GetContactsAsync(
        PeopleServiceService service,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var allContacts = new List<Person>();
        string? pageToken = null;
        string? newPersonalSyncToken = null;

        // Parse combined sync token
        var (personalSyncToken, directorySyncToken) = ParseCombinedSyncToken(syncToken);

        // Fetch personal contacts (connections)
        do
        {
            var request = service.People.Connections.List("people/me");
            request.PersonFields = PersonFields;
            request.PageSize = 1000; // Max allowed

            // Use sync token for incremental sync if provided
            request.RequestSyncToken = true;
            if (!string.IsNullOrEmpty(personalSyncToken))
            {
                request.SyncToken = personalSyncToken;
            }

            if (!string.IsNullOrEmpty(pageToken))
            {
                request.PageToken = pageToken;
            }

            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Connections != null)
            {
                allContacts.AddRange(response.Connections);
            }

            pageToken = response.NextPageToken;
            newPersonalSyncToken = response.NextSyncToken;

        } while (!string.IsNullOrEmpty(pageToken));

        // Fetch directory contacts (corporate directory)
        string? directoryPageToken = null;
        string? newDirectorySyncToken = null;
        do
        {
            var directoryRequest = service.People.ListDirectoryPeople();
            directoryRequest.ReadMask = PersonFields;
            directoryRequest.Sources = PeopleResource.ListDirectoryPeopleRequest.SourcesEnum.DIRECTORYSOURCETYPEDOMAINPROFILE;
            directoryRequest.PageSize = 1000;

            directoryRequest.RequestSyncToken = true;
            if (!string.IsNullOrEmpty(directorySyncToken))
            {
                directoryRequest.SyncToken = directorySyncToken;
            }
            
            if (!string.IsNullOrEmpty(directoryPageToken))
            {
                directoryRequest.PageToken = directoryPageToken;
            }

            try
            {
                var directoryResponse = await directoryRequest.ExecuteAsync(cancellationToken);

                if (directoryResponse.People != null)
                {
                    allContacts.AddRange(directoryResponse.People);
                }

                directoryPageToken = directoryResponse.NextPageToken;
                newDirectorySyncToken = directoryResponse.NextSyncToken;
            }
            catch (Exception ex) when (ex.Message.Contains("403") || ex.Message.Contains("Forbidden"))
            {
                // Directory contacts may not be available for all accounts
                Console.WriteLine($"Directory contacts not available: {ex.Message}");
                break;
            }

        } while (!string.IsNullOrEmpty(directoryPageToken));

        return new ContactSyncResult
        {
            Contacts = allContacts,
            SyncToken = CreateCombinedSyncToken(newPersonalSyncToken, newDirectorySyncToken)
        };
    }

    /// <summary>
    /// Fetches contact groups for the authenticated user
    /// </summary>
    public async Task<ContactGroupSyncResult> GetContactGroupsAsync(
        PeopleServiceService service,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var allGroups = new List<ContactGroup>();
        string? pageToken = null;

        do
        {
            var request = service.ContactGroups.List();
            request.PageSize = 1000;
            request.GroupFields = GroupFields;

            if (!string.IsNullOrEmpty(pageToken))
            {
                request.PageToken = pageToken;
            }

            var response = await request.ExecuteAsync(cancellationToken);

            if (response.ContactGroups != null)
            {
                allGroups.AddRange(response.ContactGroups);
            }

            pageToken = response.NextPageToken;

        } while (!string.IsNullOrEmpty(pageToken));

        return new ContactGroupSyncResult
        {
            Groups = allGroups,
            SyncToken = null // Contact groups don't support sync tokens
        };
    }

    public class ContactSyncResult
    {
        public required IList<Person> Contacts { get; init; }
        public string? SyncToken { get; init; }
    }

    public class ContactGroupSyncResult
    {
        public required IList<ContactGroup> Groups { get; init; }
        public string? SyncToken { get; init; }
    }
}
