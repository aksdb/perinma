using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using perinma.Storage.Models;

namespace perinma.Services.Google;

public class GoogleMailService
{
    private const string GmailApiBaseUrl = "https://gmail.googleapis.com/gmail/v1/users/me/";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] MetadataHeaders =
    [
        "Subject",
        "From",
        "To",
        "Cc",
        "Bcc",
        "Reply-To",
        "Date",
        "Message-ID",
        "In-Reply-To",
        "References",
        "Content-Type",
        "Content-Disposition",
        "Content-ID"
    ];

    public async Task<IReadOnlyList<GmailLabel>> GetLabelsAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        using var httpClient = await CreateAuthenticatedHttpClientAsync(credentials, cancellationToken, accountId);
        var response = await SendAsync<GmailLabelsResponse>(httpClient, "labels", cancellationToken);
        return response.Labels ?? [];
    }

    public async Task<GmailMessagePage> GetMessagesAsync(
        GoogleCredentials credentials,
        string labelId,
        string? pageToken = null,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        using var httpClient = await CreateAuthenticatedHttpClientAsync(credentials, cancellationToken, accountId);
        var listResponse = await SendAsync<GmailMessageListResponse>(
            httpClient,
            BuildListMessagesUrl(labelId, pageToken),
            cancellationToken);

        if (listResponse.Messages is not { Count: > 0 })
        {
            return new GmailMessagePage
            {
                Messages = [],
                NextPageToken = listResponse.NextPageToken,
                ResultSizeEstimate = listResponse.ResultSizeEstimate ?? 0
            };
        }

        var messageTasks = listResponse.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Id))
            .Select(message => GetMessageAsync(httpClient, message.Id!, GmailMessageFormat.Metadata, cancellationToken));

        var messages = await Task.WhenAll(messageTasks);
        return new GmailMessagePage
        {
            Messages = messages,
            NextPageToken = listResponse.NextPageToken,
            ResultSizeEstimate = listResponse.ResultSizeEstimate ?? messages.Length
        };
    }

    public async Task<GmailMessage> GetMessageAsync(
        GoogleCredentials credentials,
        string messageId,
        GmailMessageFormat format = GmailMessageFormat.Full,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        using var httpClient = await CreateAuthenticatedHttpClientAsync(credentials, cancellationToken, accountId);
        return await GetMessageAsync(httpClient, messageId, format, cancellationToken);
    }

    public async Task ModifyMessageAsync(
        GoogleCredentials credentials,
        string messageId,
        IEnumerable<string>? addLabelIds = null,
        IEnumerable<string>? removeLabelIds = null,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        using var httpClient = await CreateAuthenticatedHttpClientAsync(credentials, cancellationToken, accountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpointUrl($"messages/{Uri.EscapeDataString(messageId)}/modify"))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new ModifyMessageRequest
                    {
                        AddLabelIds = addLabelIds?.Where(static labelId => !string.IsNullOrWhiteSpace(labelId)).ToList() ?? [],
                        RemoveLabelIds = removeLabelIds?.Where(static labelId => !string.IsNullOrWhiteSpace(labelId)).ToList() ?? []
                    },
                    JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        await SendWithoutResponseAsync(httpClient, request, cancellationToken);
    }

    public async Task TrashMessageAsync(
        GoogleCredentials credentials,
        string messageId,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        using var httpClient = await CreateAuthenticatedHttpClientAsync(credentials, cancellationToken, accountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpointUrl($"messages/{Uri.EscapeDataString(messageId)}/trash"))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        await SendWithoutResponseAsync(httpClient, request, cancellationToken);
    }

    public async Task<GmailAttachmentBody> DownloadAttachmentAsync(
        GoogleCredentials credentials,
        string messageId,
        string attachmentId,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        using var httpClient = await CreateAuthenticatedHttpClientAsync(credentials, cancellationToken, accountId);
        return await SendAsync<GmailAttachmentBody>(
            httpClient,
            $"messages/{Uri.EscapeDataString(messageId)}/attachments/{Uri.EscapeDataString(attachmentId)}",
            cancellationToken);
    }

    private async Task<GmailMessage> GetMessageAsync(
        HttpClient httpClient,
        string messageId,
        GmailMessageFormat format,
        CancellationToken cancellationToken)
    {
        return await SendAsync<GmailMessage>(
            httpClient,
            BuildGetMessageUrl(messageId, format),
            cancellationToken);
    }

    private static string BuildListMessagesUrl(string labelId, string? pageToken)
    {
        var builder = new StringBuilder("messages?maxResults=100&includeSpamTrash=true");
        if (!string.IsNullOrWhiteSpace(labelId))
        {
            builder.Append("&labelIds=");
            builder.Append(Uri.EscapeDataString(labelId));
        }

        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            builder.Append("&pageToken=");
            builder.Append(Uri.EscapeDataString(pageToken));
        }

        return builder.ToString();
    }

    private static string BuildGetMessageUrl(string messageId, GmailMessageFormat format)
    {
        var builder = new StringBuilder($"messages/{Uri.EscapeDataString(messageId)}?format=");
        builder.Append(format == GmailMessageFormat.Metadata ? "metadata" : "full");

        if (format == GmailMessageFormat.Metadata)
        {
            foreach (var header in MetadataHeaders)
            {
                builder.Append("&metadataHeaders=");
                builder.Append(Uri.EscapeDataString(header));
            }
        }

        return builder.ToString();
    }

    private static string BuildEndpointUrl(string relativeUrl) => $"{GmailApiBaseUrl}{relativeUrl}";

    private async Task<HttpClient> CreateAuthenticatedHttpClientAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken,
        string? accountId)
    {
        await EnsureValidAccessTokenAsync(credentials, cancellationToken, accountId);

        if (string.IsNullOrWhiteSpace(credentials.AccessToken))
            throw new InvalidOperationException("Google access token is missing");

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            credentials.TokenType ?? "Bearer",
            credentials.AccessToken);
        return httpClient;
    }

    private async Task EnsureValidAccessTokenAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken,
        string? accountId)
    {
        var needsRefresh = string.IsNullOrWhiteSpace(credentials.AccessToken)
                           || credentials.ExpiresAt == null
                           || (credentials.ExpiresAt.Value - DateTime.UtcNow) <= TimeSpan.FromMinutes(2);

        if (needsRefresh && !string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            await RefreshAccessTokenAsync(credentials, cancellationToken, accountId);
        }
    }

    private async Task RefreshAccessTokenAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken,
        string? accountId)
    {
        if (string.IsNullOrWhiteSpace(credentials.RefreshToken))
            throw new InvalidOperationException("Refresh token is required to refresh access token");

        var tokenRequest = new Dictionary<string, string>
        {
            ["client_id"] = BuildSecrets.GoogleClientId,
            ["client_secret"] = BuildSecrets.GoogleClientSecret,
            ["refresh_token"] = credentials.RefreshToken,
            ["grant_type"] = "refresh_token"
        };

        using var httpClient = new HttpClient();
        using var response = await httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(tokenRequest),
            cancellationToken);

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                throw new ReAuthenticationRequiredException(
                    "Google",
                    accountId,
                    $"Token refresh failed with status {response.StatusCode}: {responseContent}");
            }

            throw new InvalidOperationException(
                $"Token refresh failed with status {response.StatusCode}: {responseContent}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenRefreshResponse>(responseContent, JsonOptions)
                            ?? throw new InvalidOperationException("Failed to parse token refresh response");

        credentials.AccessToken = tokenResponse.AccessToken;
        credentials.TokenType = tokenResponse.TokenType;
        if (tokenResponse.ExpiresIn > 0)
            credentials.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
            credentials.RefreshToken = tokenResponse.RefreshToken;
    }

    private async Task<T> SendAsync<T>(HttpClient httpClient, string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildEndpointUrl(relativeUrl));
        return await SendAsync<T>(httpClient, request, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpClient httpClient, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Gmail API request failed with status {response.StatusCode}: {responseContent}");
        }

        var payload = JsonSerializer.Deserialize<T>(responseContent, JsonOptions);
        return payload ?? throw new InvalidOperationException("Failed to parse Gmail API response");
    }

    private async Task SendWithoutResponseAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Gmail API request failed with status {response.StatusCode}: {responseContent}");
    }

    public enum GmailMessageFormat
    {
        Metadata,
        Full
    }

    public sealed class GmailMessagePage
    {
        public required IReadOnlyList<GmailMessage> Messages { get; init; }
        public string? NextPageToken { get; init; }
        public int ResultSizeEstimate { get; init; }
    }

    public sealed class GmailLabel
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("messagesTotal")]
        public int? MessagesTotal { get; init; }

        [JsonPropertyName("messagesUnread")]
        public int? MessagesUnread { get; init; }

        [JsonPropertyName("threadsTotal")]
        public int? ThreadsTotal { get; init; }

        [JsonPropertyName("threadsUnread")]
        public int? ThreadsUnread { get; init; }

        [JsonPropertyName("labelListVisibility")]
        public string? LabelListVisibility { get; init; }

        [JsonPropertyName("messageListVisibility")]
        public string? MessageListVisibility { get; init; }
    }

    public sealed class GmailMessage
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("threadId")]
        public string? ThreadId { get; init; }

        [JsonPropertyName("labelIds")]
        public List<string>? LabelIds { get; init; }

        [JsonPropertyName("snippet")]
        public string? Snippet { get; init; }

        [JsonPropertyName("historyId")]
        public string? HistoryId { get; init; }

        [JsonPropertyName("internalDate")]
        public string? InternalDate { get; init; }

        [JsonPropertyName("sizeEstimate")]
        public int? SizeEstimate { get; init; }

        [JsonPropertyName("payload")]
        public GmailMessagePart? Payload { get; init; }
    }

    public sealed class GmailMessagePart
    {
        [JsonPropertyName("partId")]
        public string? PartId { get; init; }

        [JsonPropertyName("mimeType")]
        public string? MimeType { get; init; }

        [JsonPropertyName("filename")]
        public string? Filename { get; init; }

        [JsonPropertyName("headers")]
        public List<GmailMessageHeader>? Headers { get; init; }

        [JsonPropertyName("body")]
        public GmailMessageBody? Body { get; init; }

        [JsonPropertyName("parts")]
        public List<GmailMessagePart>? Parts { get; init; }
    }

    public sealed class GmailMessageHeader
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }

    public sealed class GmailMessageBody
    {
        [JsonPropertyName("attachmentId")]
        public string? AttachmentId { get; init; }

        [JsonPropertyName("size")]
        public int Size { get; init; }

        [JsonPropertyName("data")]
        public string? Data { get; init; }
    }

    public sealed class GmailAttachmentBody
    {
        [JsonPropertyName("attachmentId")]
        public string? AttachmentId { get; init; }

        [JsonPropertyName("size")]
        public int Size { get; init; }

        [JsonPropertyName("data")]
        public string? Data { get; init; }
    }

    private sealed class GmailLabelsResponse
    {
        [JsonPropertyName("labels")]
        public List<GmailLabel>? Labels { get; init; }
    }

    private sealed class GmailMessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<GmailMessageReference>? Messages { get; init; }

        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; init; }

        [JsonPropertyName("resultSizeEstimate")]
        public int? ResultSizeEstimate { get; init; }
    }

    private sealed class GmailMessageReference
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }

    private sealed class ModifyMessageRequest
    {
        [JsonPropertyName("addLabelIds")]
        public List<string> AddLabelIds { get; init; } = [];

        [JsonPropertyName("removeLabelIds")]
        public List<string> RemoveLabelIds { get; init; } = [];
    }

    private sealed class TokenRefreshResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }
    }
}
