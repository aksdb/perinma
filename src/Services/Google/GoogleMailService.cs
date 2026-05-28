using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using perinma.Models;
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

    private readonly Func<HttpClient> _httpClientFactory;

    public GoogleMailService(Func<HttpClient>? httpClientFactory = null)
    {
        _httpClientFactory = httpClientFactory ?? (() => new HttpClient());
    }

    public async Task<IReadOnlyList<GmailLabel>> GetLabelsAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        using var httpClient = await CreateAuthenticatedHttpClientAsync(credentials, cancellationToken, accountId);
        var response = await SendAsync<GmailLabelsResponse>(httpClient, "labels", cancellationToken);
        return response.Labels ?? [];
    }

    public async Task<IReadOnlyList<GmailSendAs>> GetSenderIdentitiesAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        using var httpClient = await CreateAuthenticatedHttpClientAsync(credentials, cancellationToken, accountId);
        var response = await SendAsync<GmailSendAsListResponse>(httpClient, "settings/sendAs", cancellationToken);
        return response.SendAs ?? [];
    }

    public async Task<GmailDraft> SaveDraftAsync(
        GoogleCredentials credentials,
        GmailComposeRequest composeRequest,
        string? existingDraftId = null,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        using var httpClient = await CreateAuthenticatedHttpClientAsync(credentials, cancellationToken, accountId);
        var message = await BuildComposeMessageAsync(composeRequest, cancellationToken);
        var requestBody = new GmailDraftRequest
        {
            Id = string.IsNullOrWhiteSpace(existingDraftId) ? null : existingDraftId,
            Message = message
        };

        var relativeUrl = string.IsNullOrWhiteSpace(existingDraftId)
            ? "drafts"
            : $"drafts/{Uri.EscapeDataString(existingDraftId)}";
        using var request = new HttpRequestMessage(
            string.IsNullOrWhiteSpace(existingDraftId) ? HttpMethod.Post : HttpMethod.Put,
            BuildEndpointUrl(relativeUrl))
        {
            Content = CreateJsonContent(requestBody)
        };

        return await SendAsync<GmailDraft>(httpClient, request, cancellationToken);
    }

    public async Task DeleteDraftAsync(
        GoogleCredentials credentials,
        string draftId,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        using var httpClient = await CreateAuthenticatedHttpClientAsync(credentials, cancellationToken, accountId);
        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildEndpointUrl($"drafts/{Uri.EscapeDataString(draftId)}"));
        await SendWithoutResponseAsync(httpClient, request, cancellationToken);
    }

    public async Task<GmailMessage> SendAsync(
        GoogleCredentials credentials,
        GmailComposeRequest composeRequest,
        string? existingDraftId = null,
        CancellationToken cancellationToken = default,
        string? accountId = null)
    {
        using var httpClient = await CreateAuthenticatedHttpClientAsync(credentials, cancellationToken, accountId);
        var message = await BuildComposeMessageAsync(composeRequest, cancellationToken);

        object requestBody = string.IsNullOrWhiteSpace(existingDraftId)
            ? new GmailMessageRequest { Raw = message.Raw, ThreadId = message.ThreadId }
            : new GmailDraftRequest { Id = existingDraftId, Message = message };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildEndpointUrl(string.IsNullOrWhiteSpace(existingDraftId) ? "messages/send" : "drafts/send"))
        {
            Content = CreateJsonContent(requestBody)
        };

        return await SendAsync<GmailMessage>(httpClient, request, cancellationToken);
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
            Content = CreateJsonContent(
                new ModifyMessageRequest
                {
                    AddLabelIds = addLabelIds?.Where(static labelId => !string.IsNullOrWhiteSpace(labelId)).ToList() ?? [],
                    RemoveLabelIds = removeLabelIds?.Where(static labelId => !string.IsNullOrWhiteSpace(labelId)).ToList() ?? []
                })
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

    private async Task<GmailMessageRequest> BuildComposeMessageAsync(
        GmailComposeRequest composeRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(composeRequest);

        var attachments = composeRequest.Attachments.Count == 0
            ? []
            : await LoadAttachmentsAsync(composeRequest.Attachments, cancellationToken);
        var mime = MimeBuilder.Build(composeRequest, attachments);

        return new GmailMessageRequest
        {
            Raw = EncodeBase64Url(Encoding.UTF8.GetBytes(mime)),
            ThreadId = string.IsNullOrWhiteSpace(composeRequest.ThreadId) ? null : composeRequest.ThreadId
        };
    }

    private static async Task<IReadOnlyList<PreparedAttachment>> LoadAttachmentsAsync(
        IReadOnlyList<GmailComposeAttachment> attachments,
        CancellationToken cancellationToken)
    {
        var preparedAttachments = new List<PreparedAttachment>(attachments.Count);
        foreach (var attachment in attachments)
        {
            var content = await File.ReadAllBytesAsync(attachment.ContentPath, cancellationToken);
            preparedAttachments.Add(
                new PreparedAttachment
                {
                    AttachmentId = attachment.AttachmentId,
                    FileName = attachment.FileName,
                    MimeType = attachment.MimeType,
                    IsInline = attachment.IsInline,
                    ContentId = attachment.ContentId,
                    Base64Content = Convert.ToBase64String(content)
                });
        }

        return preparedAttachments;
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

    private static StringContent CreateJsonContent<T>(T value) =>
        new(
            JsonSerializer.Serialize(value, JsonOptions),
            Encoding.UTF8,
            "application/json");

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

        var httpClient = _httpClientFactory();
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

    public sealed class GmailComposeRequest
    {
        public required string SenderAddress { get; init; }
        public string SenderDisplayName { get; init; } = string.Empty;
        public required IReadOnlyList<MailAddress> To { get; init; }
        public required IReadOnlyList<MailAddress> Cc { get; init; }
        public required IReadOnlyList<MailAddress> Bcc { get; init; }
        public required string Subject { get; init; }
        public required string PlainTextBody { get; init; }
        public required string HtmlBody { get; init; }
        public string? InReplyTo { get; init; }
        public IReadOnlyList<string> References { get; init; } = [];
        public string? ThreadId { get; init; }
        public IReadOnlyList<GmailComposeAttachment> Attachments { get; init; } = [];
    }

    public sealed class GmailComposeAttachment
    {
        public required string AttachmentId { get; init; }
        public required string FileName { get; init; }
        public required string MimeType { get; init; }
        public required string ContentPath { get; init; }
        public bool IsInline { get; init; }
        public string? ContentId { get; init; }
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

    public sealed class GmailDraft
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("message")]
        public GmailMessage? Message { get; init; }
    }

    public sealed class GmailSendAs
    {
        [JsonPropertyName("sendAsEmail")]
        public string? SendAsEmail { get; init; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("replyToAddress")]
        public string? ReplyToAddress { get; init; }

        [JsonPropertyName("isPrimary")]
        public bool IsPrimary { get; init; }

        [JsonPropertyName("isDefault")]
        public bool IsDefault { get; init; }

        [JsonPropertyName("verificationStatus")]
        public string? VerificationStatus { get; init; }
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

    private sealed class GmailSendAsListResponse
    {
        [JsonPropertyName("sendAs")]
        public List<GmailSendAs>? SendAs { get; init; }
    }

    private sealed class GmailDraftRequest
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("message")]
        public GmailMessageRequest? Message { get; init; }
    }

    private sealed class GmailMessageRequest
    {
        [JsonPropertyName("raw")]
        public string? Raw { get; init; }

        [JsonPropertyName("threadId")]
        public string? ThreadId { get; init; }
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

    private sealed class PreparedAttachment
    {
        public required string AttachmentId { get; init; }
        public required string FileName { get; init; }
        public required string MimeType { get; init; }
        public required string Base64Content { get; init; }
        public bool IsInline { get; init; }
        public string? ContentId { get; init; }
    }

    private static class MimeBuilder
    {
        public static string Build(GmailComposeRequest composeRequest, IReadOnlyList<PreparedAttachment> attachments)
        {
            var builder = new StringBuilder();
            AppendHeaders(builder, composeRequest);

            var plainTextBody = composeRequest.PlainTextBody ?? string.Empty;
            var htmlBody = composeRequest.HtmlBody ?? string.Empty;
            if (plainTextBody.Length == 0 && htmlBody.Length == 0)
                plainTextBody = string.Empty;

            var inlineAttachments = attachments.Where(static attachment => attachment.IsInline).ToList();
            var regularAttachments = attachments.Where(static attachment => !attachment.IsInline).ToList();

            if (inlineAttachments.Count == 0 && regularAttachments.Count == 0)
            {
                AppendBodyEntity(builder, plainTextBody, htmlBody);
                return builder.ToString();
            }

            if (regularAttachments.Count == 0)
            {
                AppendRelatedEntity(builder, plainTextBody, htmlBody, inlineAttachments);
                return builder.ToString();
            }

            var mixedBoundary = CreateBoundary("mixed");
            builder.Append("Content-Type: multipart/mixed; boundary=\"")
                .Append(mixedBoundary)
                .Append("\"\r\n\r\n");

            builder.Append("--").Append(mixedBoundary).Append("\r\n");
            if (inlineAttachments.Count > 0)
            {
                AppendRelatedEntity(builder, plainTextBody, htmlBody, inlineAttachments);
            }
            else
            {
                AppendBodyEntity(builder, plainTextBody, htmlBody);
            }

            foreach (var attachment in regularAttachments)
            {
                builder.Append("--").Append(mixedBoundary).Append("\r\n");
                AppendAttachmentEntity(builder, attachment);
            }

            AppendClosingBoundary(builder, mixedBoundary);
            return builder.ToString();
        }

        private static void AppendHeaders(StringBuilder builder, GmailComposeRequest composeRequest)
        {
            builder.Append("From: ")
                .Append(FormatAddress(composeRequest.SenderDisplayName, composeRequest.SenderAddress))
                .Append("\r\n");
            AppendAddressHeader(builder, "To", composeRequest.To);
            AppendAddressHeader(builder, "Cc", composeRequest.Cc);
            AppendAddressHeader(builder, "Bcc", composeRequest.Bcc);
            builder.Append("Subject: ")
                .Append(EncodeUnstructuredHeader(composeRequest.Subject))
                .Append("\r\n");

            var inReplyTo = NormalizeMessageId(composeRequest.InReplyTo);
            if (!string.IsNullOrWhiteSpace(inReplyTo))
            {
                builder.Append("In-Reply-To: ")
                    .Append(inReplyTo)
                    .Append("\r\n");
            }

            var references = composeRequest.References
                .Select(NormalizeMessageId)
                .Where(static reference => !string.IsNullOrWhiteSpace(reference))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (references.Length > 0)
            {
                builder.Append("References: ")
                    .Append(string.Join(' ', references))
                    .Append("\r\n");
            }

            builder.Append("MIME-Version: 1.0\r\n");
        }

        private static void AppendAddressHeader(StringBuilder builder, string headerName, IReadOnlyList<MailAddress> addresses)
        {
            if (addresses.Count == 0)
                return;

            builder.Append(headerName)
                .Append(": ")
                .Append(string.Join(", ", addresses.Select(FormatAddress)))
                .Append("\r\n");
        }

        private static string FormatAddress(MailAddress address) => FormatAddress(address.Name, address.Address);

        private static string FormatAddress(string? displayName, string address)
        {
            var sanitizedAddress = SanitizeHeader(address);
            if (string.IsNullOrWhiteSpace(displayName))
                return sanitizedAddress;

            var trimmedDisplayName = displayName.Trim();
            if (RequiresEncodedWord(trimmedDisplayName))
                return $"{EncodeWord(trimmedDisplayName)} <{sanitizedAddress}>";
            if (NeedsQuotedPhrase(trimmedDisplayName))
                return $"\"{EscapeQuotedString(trimmedDisplayName)}\" <{sanitizedAddress}>";
            return $"{trimmedDisplayName} <{sanitizedAddress}>";
        }

        private static void AppendBodyEntity(StringBuilder builder, string plainTextBody, string htmlBody)
        {
            if (!string.IsNullOrWhiteSpace(htmlBody) && !string.IsNullOrWhiteSpace(plainTextBody))
            {
                var alternativeBoundary = CreateBoundary("alternative");
                builder.Append("Content-Type: multipart/alternative; boundary=\"")
                    .Append(alternativeBoundary)
                    .Append("\"\r\n\r\n");
                AppendTextPart(builder, alternativeBoundary, "text/plain", plainTextBody);
                AppendTextPart(builder, alternativeBoundary, "text/html", htmlBody);
                AppendClosingBoundary(builder, alternativeBoundary);
                return;
            }

            if (!string.IsNullOrWhiteSpace(htmlBody))
            {
                AppendSingleTextPart(builder, "text/html", htmlBody);
                return;
            }

            AppendSingleTextPart(builder, "text/plain", plainTextBody);
        }

        private static void AppendRelatedEntity(
            StringBuilder builder,
            string plainTextBody,
            string htmlBody,
            IReadOnlyList<PreparedAttachment> inlineAttachments)
        {
            var relatedBoundary = CreateBoundary("related");
            builder.Append("Content-Type: multipart/related; boundary=\"")
                .Append(relatedBoundary)
                .Append("\"\r\n\r\n");

            builder.Append("--").Append(relatedBoundary).Append("\r\n");
            AppendBodyEntity(builder, plainTextBody, htmlBody);

            foreach (var inlineAttachment in inlineAttachments)
            {
                builder.Append("--").Append(relatedBoundary).Append("\r\n");
                AppendAttachmentEntity(builder, inlineAttachment);
            }

            AppendClosingBoundary(builder, relatedBoundary);
        }

        private static void AppendSingleTextPart(StringBuilder builder, string mediaType, string body)
        {
            builder.Append("Content-Type: ")
                .Append(mediaType)
                .Append("; charset=utf-8\r\n")
                .Append("Content-Transfer-Encoding: 8bit\r\n\r\n")
                .Append(NormalizeBody(body))
                .Append("\r\n");
        }

        private static void AppendTextPart(StringBuilder builder, string boundary, string mediaType, string body)
        {
            builder.Append("--").Append(boundary).Append("\r\n");
            AppendSingleTextPart(builder, mediaType, body);
        }

        private static void AppendAttachmentEntity(StringBuilder builder, PreparedAttachment attachment)
        {
            builder.Append("Content-Type: ")
                .Append(string.IsNullOrWhiteSpace(attachment.MimeType) ? "application/octet-stream" : attachment.MimeType);
            AppendFileNameParameter(builder, "name", attachment.FileName);
            builder.Append("\r\n")
                .Append("Content-Transfer-Encoding: base64\r\n")
                .Append("Content-Disposition: ")
                .Append(attachment.IsInline ? "inline" : "attachment");
            AppendFileNameParameter(builder, "filename", attachment.FileName);
            builder.Append("\r\n");

            if (attachment.IsInline)
            {
                var contentId = ResolveContentId(attachment);
                builder.Append("Content-ID: <")
                    .Append(contentId)
                    .Append(">\r\n");
            }

            builder.Append("\r\n");
            AppendWrappedBase64(builder, attachment.Base64Content);
        }

        private static void AppendFileNameParameter(StringBuilder builder, string parameterName, string fileName)
        {
            var sanitizedFileName = SanitizeHeader(fileName);
            if (RequiresEncodedWord(sanitizedFileName))
            {
                builder.Append($"; {parameterName}*=utf-8''")
                    .Append(Uri.EscapeDataString(sanitizedFileName));
                return;
            }

            builder.Append($"; {parameterName}=\"")
                .Append(EscapeQuotedString(sanitizedFileName))
                .Append('\"');
        }

        private static void AppendWrappedBase64(StringBuilder builder, string base64Content)
        {
            if (string.IsNullOrEmpty(base64Content))
            {
                builder.Append("\r\n");
                return;
            }

            const int lineLength = 76;
            for (var offset = 0; offset < base64Content.Length; offset += lineLength)
            {
                var count = Math.Min(lineLength, base64Content.Length - offset);
                builder.Append(base64Content, offset, count).Append("\r\n");
            }
        }

        private static void AppendClosingBoundary(StringBuilder builder, string boundary) =>
            builder.Append("--").Append(boundary).Append("--\r\n");

        private static string CreateBoundary(string prefix) => $"perinma_{prefix}_{Guid.NewGuid():N}";

        private static string NormalizeBody(string value) =>
            (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);

        private static string ResolveContentId(PreparedAttachment attachment)
        {
            var contentId = SanitizeHeader(attachment.ContentId);
            if (!string.IsNullOrWhiteSpace(contentId))
                return contentId;

            var fallback = attachment.AttachmentId;
            if (fallback.Contains('@', StringComparison.Ordinal))
                return fallback;
            return $"{fallback}@perinma.local";
        }

        private static string NormalizeMessageId(string? value)
        {
            var sanitized = SanitizeHeader(value);
            return string.IsNullOrWhiteSpace(sanitized) ? string.Empty : sanitized;
        }

        private static string EncodeUnstructuredHeader(string value)
        {
            var sanitized = SanitizeHeader(value);
            return RequiresEncodedWord(sanitized) ? EncodeWord(sanitized) : sanitized;
        }

        private static bool RequiresEncodedWord(string value) => value.Any(static ch => ch > 127);

        private static bool NeedsQuotedPhrase(string value) => value.Any(static ch => ch is ',' or ';' or '<' or '>' or '"');

        private static string EncodeWord(string value) => $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";

        private static string EscapeQuotedString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        private static string SanitizeHeader(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("\r", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal)
                    .Trim();
    }

    private static string EncodeBase64Url(byte[] value)
    {
        if (value.Length == 0)
            return string.Empty;

        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
