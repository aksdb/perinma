using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using perinma.Storage.Models;

namespace perinma.Services.Jmap;

public class JmapMailService(HttpClient? httpClient = null)
{
    private const string CoreCapability = "urn:ietf:params:jmap:core";
    private const string MailCapability = "urn:ietf:params:jmap:mail";
    private const int QueryPageSize = 250;
    private const int GetBatchSize = 100;
    private const string MethodCallId = "c0";

    private readonly HttpClient _httpClient = httpClient ?? CreateHttpClient();

    public async Task<JmapMailboxSyncResult> GetMailboxesAsync(
        JmapCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var session = await DiscoverSessionAsync(credentials, cancellationToken);
        var response = await CallAsync(
            session,
            "Mailbox/get",
            new JsonObject
            {
                ["accountId"] = session.AccountId,
                ["properties"] = CreateStringArray("id", "parentId", "name", "role", "isSubscribed", "totalEmails", "unreadEmails")
            },
            cancellationToken);

        return new JmapMailboxSyncResult
        {
            SyncToken = GetString(response, "state"),
            Mailboxes = GetArray(response, "list")
                .Select(ParseMailbox)
                .ToList()
        };
    }

    public async Task<JmapMessageSyncResult> GetMessageSummariesAsync(
        JmapCredentials credentials,
        string mailboxExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var session = await DiscoverSessionAsync(credentials, cancellationToken);
        var queryResult = await QueryEmailIdsAsync(session, mailboxExternalId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(syncToken) && string.Equals(syncToken, queryResult.QueryState, StringComparison.Ordinal))
        {
            return new JmapMessageSyncResult
            {
                Messages = [],
                SyncToken = queryResult.QueryState,
                MissingMessagesAreAuthoritative = false
            };
        }

        var messages = await GetEmailsAsync(session, queryResult.Ids, fetchBodies: false, cancellationToken);
        return new JmapMessageSyncResult
        {
            Messages = messages,
            SyncToken = queryResult.QueryState,
            MissingMessagesAreAuthoritative = true
        };
    }

    public async Task<JmapMailMessage> GetMessageAsync(
        JmapCredentials credentials,
        string messageExternalId,
        bool fetchBodies,
        CancellationToken cancellationToken = default)
    {
        var session = await DiscoverSessionAsync(credentials, cancellationToken);
        var response = await CallAsync(
            session,
            "Email/get",
            BuildEmailGetArguments(session.AccountId, [messageExternalId], fetchBodies),
            cancellationToken);

        var notFound = GetArray(response, "notFound");
        if (notFound.Count > 0)
            throw new InvalidOperationException($"JMAP message '{messageExternalId}' was not found.");

        var list = GetArray(response, "list");
        if (list.Count == 0)
            throw new InvalidOperationException($"JMAP message '{messageExternalId}' was not returned.");

        return ParseMessage(list[0], fetchBodies);
    }

    public async Task<byte[]> DownloadBlobAsync(
        JmapCredentials credentials,
        string blobId,
        string? fileName,
        string? mimeType,
        CancellationToken cancellationToken = default)
    {
        var session = await DiscoverSessionAsync(credentials, cancellationToken);
        if (string.IsNullOrWhiteSpace(session.DownloadUrlTemplate))
            throw new InvalidOperationException("JMAP session did not provide a download URL template.");

        var downloadUrl = ExpandDownloadUrl(
            session.DownloadUrlTemplate,
            session.AccountId,
            blobId,
            fileName ?? blobId,
            mimeType ?? "application/octet-stream");

        using var request = CreateRequest(HttpMethod.Get, downloadUrl, credentials, acceptJson: false);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw CreateHttpException("download JMAP blob", response, Encoding.UTF8.GetString(content));

        return content;
    }

    public async Task SetReadStateAsync(
        JmapCredentials credentials,
        string messageExternalId,
        bool isRead,
        CancellationToken cancellationToken = default)
    {
        var session = await DiscoverSessionAsync(credentials, cancellationToken);
        var state = await GetMessageStateAsync(session, messageExternalId, cancellationToken);
        if (isRead)
            state.Keywords.Add("$seen");
        else
            state.Keywords.Remove("$seen");

        await UpdateEmailAsync(
            session,
            messageExternalId,
            new JsonObject
            {
                ["keywords"] = CreateTrueMap(state.Keywords)
            },
            cancellationToken);
    }

    public async Task SetStarredStateAsync(
        JmapCredentials credentials,
        string messageExternalId,
        bool isStarred,
        CancellationToken cancellationToken = default)
    {
        var session = await DiscoverSessionAsync(credentials, cancellationToken);
        var state = await GetMessageStateAsync(session, messageExternalId, cancellationToken);
        if (isStarred)
            state.Keywords.Add("$flagged");
        else
            state.Keywords.Remove("$flagged");

        await UpdateEmailAsync(
            session,
            messageExternalId,
            new JsonObject
            {
                ["keywords"] = CreateTrueMap(state.Keywords)
            },
            cancellationToken);
    }

    public async Task ArchiveMessageAsync(
        JmapCredentials credentials,
        string messageExternalId,
        CancellationToken cancellationToken = default)
    {
        var session = await DiscoverSessionAsync(credentials, cancellationToken);
        var state = await GetMessageStateAsync(session, messageExternalId, cancellationToken);
        var mailboxes = await GetMailboxesAsync(credentials, cancellationToken);
        var inboxMailboxIds = mailboxes.Mailboxes
            .Where(mailbox => string.Equals(mailbox.Role, "inbox", StringComparison.OrdinalIgnoreCase))
            .Select(mailbox => mailbox.ExternalId)
            .ToHashSet(StringComparer.Ordinal);

        var retainedMailboxIds = state.MailboxIds
            .Where(mailboxId => !inboxMailboxIds.Contains(mailboxId))
            .ToHashSet(StringComparer.Ordinal);

        if (retainedMailboxIds.Count == 0)
        {
            var archiveMailbox = mailboxes.Mailboxes.FirstOrDefault(mailbox =>
                string.Equals(mailbox.Role, "archive", StringComparison.OrdinalIgnoreCase));
            if (archiveMailbox != null)
                retainedMailboxIds.Add(archiveMailbox.ExternalId);
        }

        await UpdateEmailAsync(
            session,
            messageExternalId,
            new JsonObject
            {
                ["mailboxIds"] = CreateTrueMap(retainedMailboxIds)
            },
            cancellationToken);
    }

    public async Task DeleteMessageAsync(
        JmapCredentials credentials,
        string messageExternalId,
        CancellationToken cancellationToken = default)
    {
        var session = await DiscoverSessionAsync(credentials, cancellationToken);
        var response = await CallAsync(
            session,
            "Email/set",
            new JsonObject
            {
                ["accountId"] = session.AccountId,
                ["destroy"] = CreateStringArray(messageExternalId)
            },
            cancellationToken);

        EnsureDestroyed(response, messageExternalId);
    }

    public async Task<bool> TestConnectionAsync(
        JmapCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await DiscoverSessionAsync(credentials, cancellationToken);
            await CallAsync(
                session,
                "Mailbox/get",
                new JsonObject
                {
                    ["accountId"] = session.AccountId,
                    ["properties"] = CreateStringArray("id")
                },
                cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<JmapSession> DiscoverSessionAsync(
        JmapCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, credentials.SessionUrl, credentials);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw CreateHttpException("discover JMAP session", response, content);

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        var apiUrl = GetRequiredString(root, "apiUrl");
        var downloadUrl = GetString(root, "downloadUrl");
        var sessionState = GetString(root, "state");
        var capabilities = root.TryGetProperty("capabilities", out var capabilitiesElement)
            ? capabilitiesElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        if (!capabilities.Contains(MailCapability))
            throw new InvalidOperationException("JMAP session does not advertise mail capability.");

        var accountId = ResolveMailAccountId(root);
        return new JmapSession
        {
            AccountId = accountId,
            ApiUrl = apiUrl,
            DownloadUrlTemplate = downloadUrl,
            SessionState = sessionState,
            Credentials = credentials,
            Capabilities = [CoreCapability, MailCapability]
        };
    }

    private async Task<List<JmapMailMessage>> GetEmailsAsync(
        JmapSession session,
        IReadOnlyList<string> ids,
        bool fetchBodies,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return [];

        var messagesById = new Dictionary<string, JmapMailMessage>(StringComparer.Ordinal);
        foreach (var batch in Chunk(ids, GetBatchSize))
        {
            var response = await CallAsync(
                session,
                "Email/get",
                BuildEmailGetArguments(session.AccountId, batch, fetchBodies),
                cancellationToken);

            foreach (var email in GetArray(response, "list"))
            {
                var message = ParseMessage(email, fetchBodies);
                messagesById[message.ExternalId] = message;
            }
        }

        return ids
            .Where(messagesById.ContainsKey)
            .Select(id => messagesById[id])
            .ToList();
    }

    private async Task<EmailQueryResult> QueryEmailIdsAsync(
        JmapSession session,
        string mailboxExternalId,
        CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        var position = 0;
        string? queryState = null;

        while (true)
        {
            var response = await CallAsync(
                session,
                "Email/query",
                new JsonObject
                {
                    ["accountId"] = session.AccountId,
                    ["filter"] = new JsonObject
                    {
                        ["inMailbox"] = mailboxExternalId
                    },
                    ["sort"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["property"] = "receivedAt",
                            ["isAscending"] = false
                        }
                    },
                    ["position"] = position,
                    ["limit"] = QueryPageSize,
                    ["collapseThreads"] = false,
                    ["calculateTotal"] = false
                },
                cancellationToken);

            queryState ??= GetString(response, "queryState");
            var batchIds = GetArray(response, "ids")
                .Select(GetElementString)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            if (batchIds.Count == 0)
                break;

            ids.AddRange(batchIds);
            if (batchIds.Count < QueryPageSize)
                break;

            position += batchIds.Count;
        }

        return new EmailQueryResult
        {
            Ids = ids,
            QueryState = queryState
        };
    }

    private async Task<MessageState> GetMessageStateAsync(
        JmapSession session,
        string messageExternalId,
        CancellationToken cancellationToken)
    {
        var response = await CallAsync(
            session,
            "Email/get",
            new JsonObject
            {
                ["accountId"] = session.AccountId,
                ["ids"] = CreateStringArray(messageExternalId),
                ["properties"] = CreateStringArray("id", "keywords", "mailboxIds")
            },
            cancellationToken);

        var notFound = GetArray(response, "notFound");
        if (notFound.Count > 0)
            throw new InvalidOperationException($"JMAP message '{messageExternalId}' was not found.");

        var email = GetArray(response, "list").FirstOrDefault();
        if (email.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"JMAP message '{messageExternalId}' was not returned.");

        return new MessageState
        {
            Keywords = ParseTrueMap(email, "keywords"),
            MailboxIds = ParseTrueMap(email, "mailboxIds")
        };
    }

    private async Task UpdateEmailAsync(
        JmapSession session,
        string messageExternalId,
        JsonObject patch,
        CancellationToken cancellationToken)
    {
        var response = await CallAsync(
            session,
            "Email/set",
            new JsonObject
            {
                ["accountId"] = session.AccountId,
                ["update"] = new JsonObject
                {
                    [messageExternalId] = patch
                }
            },
            cancellationToken);

        EnsureUpdated(response, messageExternalId);
    }

    private static JsonObject BuildEmailGetArguments(string accountId, IReadOnlyList<string> ids, bool fetchBodies)
    {
        return new JsonObject
        {
            ["accountId"] = accountId,
            ["ids"] = CreateStringArray(ids),
            ["properties"] = CreateStringArray(
                "id",
                "threadId",
                "messageId",
                "mailboxIds",
                "keywords",
                "from",
                "to",
                "cc",
                "bcc",
                "replyTo",
                "subject",
                "sentAt",
                "receivedAt",
                "preview",
                "textBody",
                "htmlBody",
                "bodyStructure",
                "hasAttachment"),
            ["bodyProperties"] = CreateStringArray(
                "partId",
                "blobId",
                "size",
                "name",
                "type",
                "charset",
                "disposition",
                "cid",
                "subParts"),
            ["fetchAllBodyValues"] = fetchBodies
        };
    }

    private static JmapMailbox ParseMailbox(JsonElement element)
    {
        return new JmapMailbox
        {
            ExternalId = GetRequiredString(element, "id"),
            ParentExternalId = GetString(element, "parentId"),
            Name = GetString(element, "name") ?? "Unnamed Mailbox",
            Role = GetString(element, "role"),
            UnreadCount = GetInt32(element, "unreadEmails") ?? 0,
            TotalCount = GetInt32(element, "totalEmails") ?? 0,
            Enabled = GetBoolean(element, "isSubscribed") ?? true,
            RawDataJson = element.GetRawText()
        };
    }

    private static JmapMailMessage ParseMessage(JsonElement element, bool includeBodies)
    {
        var from = ParseMailAddresses(element, "from");
        var sender = from.FirstOrDefault();
        var keywords = ParseTrueMap(element, "keywords");
        var mailboxIds = ParseTrueMap(element, "mailboxIds");
        var textPartIds = GetBodyPartIds(element, "textBody");
        var htmlPartIds = GetBodyPartIds(element, "htmlBody");
        var bodyPartIds = new HashSet<string>(textPartIds, StringComparer.Ordinal);
        bodyPartIds.UnionWith(htmlPartIds);

        string? plainTextBody = null;
        string? htmlBody = null;
        if (includeBodies && element.TryGetProperty("bodyValues", out var bodyValues))
        {
            plainTextBody = CombineBodyValues(textPartIds, bodyValues);
            htmlBody = CombineBodyValues(htmlPartIds, bodyValues);
        }

        element.TryGetProperty("bodyStructure", out var bodyStructure);
        var attachments = ParseAttachments(bodyStructure, bodyPartIds);
        var hasPlainTextBody = textPartIds.Count > 0 || ContainsMimeType(bodyStructure, "text/plain");
        var hasHtmlBody = htmlPartIds.Count > 0 || ContainsMimeType(bodyStructure, "text/html");

        return new JmapMailMessage
        {
            ExternalId = GetRequiredString(element, "id"),
            ThreadExternalId = GetString(element, "threadId") ?? GetRequiredString(element, "id"),
            InternetMessageId = GetString(element, "messageId"),
            Subject = GetString(element, "subject"),
            SenderName = sender?.Name,
            SenderAddress = sender?.Address,
            SentAtUnixTime = ParseUnixTimeSeconds(GetString(element, "sentAt")),
            ReceivedAtUnixTime = ParseUnixTimeSeconds(GetString(element, "receivedAt")),
            Preview = GetString(element, "preview"),
            PlainTextBody = plainTextBody,
            HtmlBody = htmlBody,
            HasHtmlBody = hasHtmlBody,
            HasPlainTextBody = hasPlainTextBody,
            HasAttachments = attachments.Count > 0 || GetBoolean(element, "hasAttachment") == true,
            HasExternalResources = !string.IsNullOrWhiteSpace(htmlBody) && ContainsExternalResources(htmlBody),
            HasBlockedContent = false,
            IsUnread = !keywords.Contains("$seen"),
            IsStarred = keywords.Contains("$flagged"),
            IsAnswered = keywords.Contains("$answered"),
            IsDraft = keywords.Contains("$draft"),
            MailboxExternalIds = mailboxIds.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            To = ParseMailAddresses(element, "to"),
            Cc = ParseMailAddresses(element, "cc"),
            Bcc = ParseMailAddresses(element, "bcc"),
            ReplyTo = ParseMailAddresses(element, "replyTo"),
            Attachments = attachments,
            RawDataJson = element.GetRawText()
        };
    }

    private static List<JmapMailAttachment> ParseAttachments(JsonElement bodyStructure, HashSet<string> bodyPartIds)
    {
        var attachments = new List<JmapMailAttachment>();
        if (bodyStructure.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return attachments;

        var seenBlobIds = new HashSet<string>(StringComparer.Ordinal);
        TraverseParts(bodyStructure, part =>
        {
            var blobId = GetString(part, "blobId");
            if (string.IsNullOrWhiteSpace(blobId) || !seenBlobIds.Add(blobId))
                return;

            var partId = GetString(part, "partId");
            if (!string.IsNullOrWhiteSpace(partId) && bodyPartIds.Contains(partId))
                return;

            var disposition = GetString(part, "disposition");
            var name = GetString(part, "name");
            var cid = GetString(part, "cid");
            if (string.IsNullOrWhiteSpace(name)
                && string.IsNullOrWhiteSpace(cid)
                && !string.Equals(disposition, "attachment", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            attachments.Add(new JmapMailAttachment
            {
                ExternalId = blobId,
                FileName = name,
                MimeType = GetString(part, "type"),
                Size = GetInt32(part, "size") ?? 0,
                IsInline = string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase),
                ContentId = NormalizeContentId(cid),
                RawDataJson = part.GetRawText()
            });
        });

        return attachments;
    }

    private async Task<JsonElement> CallAsync(
        JmapSession session,
        string methodName,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        var requestBody = new JsonObject
        {
            ["using"] = CreateStringArray(session.Capabilities),
            ["methodCalls"] = new JsonArray
            {
                new JsonArray
                {
                    methodName,
                    arguments,
                    MethodCallId
                }
            }
        };

        using var request = CreateRequest(HttpMethod.Post, session.ApiUrl, session.Credentials);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw CreateHttpException($"invoke JMAP method '{methodName}'", response, content);

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        if (!root.TryGetProperty("methodResponses", out var methodResponses) || methodResponses.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"JMAP response for '{methodName}' did not contain methodResponses.");

        foreach (var item in methodResponses.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2)
                continue;

            var responseName = item[0].GetString();
            var responseBody = item[1];
            if (string.Equals(responseName, methodName, StringComparison.Ordinal))
                return responseBody.Clone();

            if (string.Equals(responseName, "error", StringComparison.Ordinal))
                throw CreateMethodException(methodName, responseBody);
        }

        throw new InvalidOperationException($"JMAP response for '{methodName}' did not include a matching method result.");
    }

    private static Exception CreateMethodException(string methodName, JsonElement responseBody)
    {
        var type = GetString(responseBody, "type") ?? "unknown";
        var description = GetString(responseBody, "description") ?? responseBody.GetRawText();
        return new InvalidOperationException($"JMAP method '{methodName}' failed with error '{type}': {description}");
    }

    private static Exception CreateHttpException(string operation, HttpResponseMessage response, string content)
    {
        return new InvalidOperationException(
            $"Failed to {operation}: {(int)response.StatusCode} {response.ReasonPhrase}. Response: {content}");
    }

    private static void EnsureUpdated(JsonElement response, string messageExternalId)
    {
        if (HasStringProperty(response, "updated", messageExternalId))
            return;

        throw new InvalidOperationException(BuildSetFailureMessage(response, messageExternalId));
    }

    private static void EnsureDestroyed(JsonElement response, string messageExternalId)
    {
        if (HasStringProperty(response, "destroyed", messageExternalId))
            return;

        throw new InvalidOperationException(BuildSetFailureMessage(response, messageExternalId));
    }

    private static bool HasStringProperty(JsonElement response, string propertyName, string target)
    {
        if (!response.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return false;

        return property.EnumerateArray().Any(item => string.Equals(item.GetString(), target, StringComparison.Ordinal));
    }

    private static string BuildSetFailureMessage(JsonElement response, string messageExternalId)
    {
        if (response.TryGetProperty("notUpdated", out var notUpdated)
            && notUpdated.ValueKind == JsonValueKind.Object
            && notUpdated.TryGetProperty(messageExternalId, out var error))
        {
            var type = GetString(error, "type") ?? "unknown";
            var description = GetString(error, "description") ?? error.GetRawText();
            return $"JMAP update failed for message '{messageExternalId}' with error '{type}': {description}";
        }

        if (response.TryGetProperty("notDestroyed", out var notDestroyed)
            && notDestroyed.ValueKind == JsonValueKind.Object
            && notDestroyed.TryGetProperty(messageExternalId, out var deleteError))
        {
            var type = GetString(deleteError, "type") ?? "unknown";
            var description = GetString(deleteError, "description") ?? deleteError.GetRawText();
            return $"JMAP delete failed for message '{messageExternalId}' with error '{type}': {description}";
        }

        return $"JMAP mutation did not report success for message '{messageExternalId}'.";
    }

    private static string ResolveMailAccountId(JsonElement session)
    {
        if (session.TryGetProperty("primaryAccounts", out var primaryAccounts)
            && primaryAccounts.ValueKind == JsonValueKind.Object
            && primaryAccounts.TryGetProperty(MailCapability, out var primaryMailAccount)
            && !string.IsNullOrWhiteSpace(primaryMailAccount.GetString()))
        {
            return primaryMailAccount.GetString()!;
        }

        if (session.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Object)
        {
            foreach (var accountProperty in accounts.EnumerateObject())
            {
                if (accountProperty.Value.TryGetProperty("accountCapabilities", out var accountCapabilities)
                    && accountCapabilities.ValueKind == JsonValueKind.Object
                    && accountCapabilities.TryGetProperty(MailCapability, out _))
                {
                    return accountProperty.Name;
                }
            }
        }

        throw new InvalidOperationException("JMAP session does not expose a mail-capable account.");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, JmapCredentials credentials, bool acceptJson = true)
    {
        var request = new HttpRequestMessage(method, url);
        if (acceptJson)
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("perinma/1.0");

        if (!string.IsNullOrWhiteSpace(credentials.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.BearerToken);
            return request;
        }

        if (!string.IsNullOrWhiteSpace(credentials.Username) && credentials.Password != null)
        {
            var authBytes = Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
            return request;
        }

        throw new InvalidOperationException("JMAP credentials must include either a bearer token or username/password.");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("perinma/1.0");
        return client;
    }

    private static JsonArray CreateStringArray(params string[] values) => CreateStringArray((IEnumerable<string>)values);

    private static JsonArray CreateStringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }

    private static JsonObject CreateTrueMap(IEnumerable<string> values)
    {
        var map = new JsonObject();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
            map[value] = true;
        return map;
    }

    private static List<string> GetBodyPartIds(JsonElement email, string propertyName)
    {
        if (!email.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        var ids = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            var partId = GetString(item, "partId");
            if (!string.IsNullOrWhiteSpace(partId))
                ids.Add(partId);
        }

        return ids;
    }

    private static string? CombineBodyValues(IEnumerable<string> partIds, JsonElement bodyValues)
    {
        if (bodyValues.ValueKind != JsonValueKind.Object)
            return null;

        var values = new List<string>();
        foreach (var partId in partIds)
        {
            if (!bodyValues.TryGetProperty(partId, out var bodyValue))
                continue;

            var value = GetString(bodyValue, "value");
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value);
        }

        return values.Count == 0 ? null : string.Join("\n\n", values);
    }

    private static HashSet<string> ParseTrueMap(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
            return new HashSet<string>(StringComparer.Ordinal);

        return property.EnumerateObject()
            .Where(item => item.Value.ValueKind == JsonValueKind.True)
            .Select(item => item.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static List<JmapMailAddress> ParseMailAddresses(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        var addresses = new List<JmapMailAddress>();
        foreach (var item in property.EnumerateArray())
        {
            var address = GetString(item, "email");
            if (string.IsNullOrWhiteSpace(address))
                continue;

            addresses.Add(new JmapMailAddress
            {
                Name = GetString(item, "name"),
                Address = address
            });
        }

        return addresses;
    }

    private static bool ContainsMimeType(JsonElement part, string mimeType)
    {
        if (part.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return false;

        var type = GetString(part, "type");
        if (string.Equals(type, mimeType, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!part.TryGetProperty("subParts", out var subParts) || subParts.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var subPart in subParts.EnumerateArray())
        {
            if (ContainsMimeType(subPart, mimeType))
                return true;
        }

        return false;
    }

    private static bool ContainsExternalResources(string html)
    {
        return html.Contains("src=\"http", StringComparison.OrdinalIgnoreCase)
            || html.Contains("src='http", StringComparison.OrdinalIgnoreCase)
            || html.Contains("href=\"http", StringComparison.OrdinalIgnoreCase)
            || html.Contains("href='http", StringComparison.OrdinalIgnoreCase)
            || html.Contains("url(http", StringComparison.OrdinalIgnoreCase)
            || html.Contains("url('http", StringComparison.OrdinalIgnoreCase)
            || html.Contains("url(\"http", StringComparison.OrdinalIgnoreCase);
    }

    private static void TraverseParts(JsonElement part, Action<JsonElement> visit)
    {
        if (part.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return;

        visit(part);
        if (!part.TryGetProperty("subParts", out var subParts) || subParts.ValueKind != JsonValueKind.Array)
            return;

        foreach (var subPart in subParts.EnumerateArray())
            TraverseParts(subPart, visit);
    }

    private static string ExpandDownloadUrl(string template, string accountId, string blobId, string fileName, string mimeType)
    {
        return template
            .Replace("{accountId}", Uri.EscapeDataString(accountId), StringComparison.Ordinal)
            .Replace("{blobId}", Uri.EscapeDataString(blobId), StringComparison.Ordinal)
            .Replace("{name}", Uri.EscapeDataString(fileName), StringComparison.Ordinal)
            .Replace("{type}", Uri.EscapeDataString(mimeType), StringComparison.Ordinal);
    }

    private static IReadOnlyList<JsonElement> GetArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static string GetElementString(JsonElement element) => element.GetString() ?? string.Empty;

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"JMAP payload did not contain required property '{propertyName}'.");

        return value;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Null => null,
            _ => property.GetRawText()
        };
    }

    private static bool? GetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int? GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
            return value;

        return null;
    }

    private static long? ParseUnixTimeSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return null;

        return parsed.ToUnixTimeSeconds();
    }

    private static string? NormalizeContentId(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return null;

        return contentId.Trim().Trim('<', '>');
    }

    private static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyList<string> values, int chunkSize)
    {
        for (var index = 0; index < values.Count; index += chunkSize)
            yield return values.Skip(index).Take(chunkSize).ToList();
    }

    private sealed class EmailQueryResult
    {
        public required List<string> Ids { get; init; }
        public string? QueryState { get; init; }
    }

    private sealed class MessageState
    {
        public required HashSet<string> Keywords { get; init; }
        public required HashSet<string> MailboxIds { get; init; }
    }
}

public sealed class JmapSession
{
    public required string AccountId { get; init; }
    public required string ApiUrl { get; init; }
    public required JmapCredentials Credentials { get; init; }
    public required IReadOnlyList<string> Capabilities { get; init; }
    public string? DownloadUrlTemplate { get; init; }
    public string? SessionState { get; init; }
}

public sealed class JmapMailboxSyncResult
{
    public required IList<JmapMailbox> Mailboxes { get; init; }
    public string? SyncToken { get; init; }
}

public sealed class JmapMessageSyncResult
{
    public required IList<JmapMailMessage> Messages { get; init; }
    public string? SyncToken { get; init; }
    public bool MissingMessagesAreAuthoritative { get; init; }
}

public sealed class JmapMailbox
{
    public required string ExternalId { get; init; }
    public string? ParentExternalId { get; init; }
    public required string Name { get; init; }
    public string? Role { get; init; }
    public int UnreadCount { get; init; }
    public int TotalCount { get; init; }
    public bool Enabled { get; init; }
    public required string RawDataJson { get; init; }
}

public sealed class JmapMailMessage
{
    public required string ExternalId { get; init; }
    public required string ThreadExternalId { get; init; }
    public string? InternetMessageId { get; init; }
    public string? Subject { get; init; }
    public string? SenderName { get; init; }
    public string? SenderAddress { get; init; }
    public long? SentAtUnixTime { get; init; }
    public long? ReceivedAtUnixTime { get; init; }
    public string? Preview { get; init; }
    public string? PlainTextBody { get; init; }
    public string? HtmlBody { get; init; }
    public bool HasHtmlBody { get; init; }
    public bool HasPlainTextBody { get; init; }
    public bool HasAttachments { get; init; }
    public bool HasExternalResources { get; init; }
    public bool HasBlockedContent { get; init; }
    public bool IsUnread { get; init; }
    public bool IsStarred { get; init; }
    public bool IsAnswered { get; init; }
    public bool IsDraft { get; init; }
    public required IList<string> MailboxExternalIds { get; init; }
    public required IList<JmapMailAddress> To { get; init; }
    public required IList<JmapMailAddress> Cc { get; init; }
    public required IList<JmapMailAddress> Bcc { get; init; }
    public required IList<JmapMailAddress> ReplyTo { get; init; }
    public required IList<JmapMailAttachment> Attachments { get; init; }
    public required string RawDataJson { get; init; }
}

public sealed class JmapMailAttachment
{
    public required string ExternalId { get; init; }
    public string? FileName { get; init; }
    public string? MimeType { get; init; }
    public int Size { get; init; }
    public bool IsInline { get; init; }
    public string? ContentId { get; init; }
    public required string RawDataJson { get; init; }
}

public sealed class JmapMailAddress
{
    public string? Name { get; init; }
    public required string Address { get; init; }
}
