using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using ProviderMailAddress = perinma.Models.MailAddress;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using perinma.Models;
using perinma.Storage.Models;

namespace perinma.Services.Google;

public class GoogleMailProvider(
    GoogleMailService googleMailService,
    CredentialManagerService credentialManager)
    : IMailProvider, IMailComposeProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly MailComposeCapabilities ComposeCapabilities = new()
    {
        SupportsDrafts = true,
        SupportsRemoteDrafts = true,
        SupportsSend = true,
        SupportsSenderIdentities = true,
        SupportsInlineAttachments = true
    };

    public CredentialManagerService CredentialManager => credentialManager;

    public Task<MailComposeCapabilities> GetComposeCapabilitiesAsync(
        string accountId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ComposeCapabilities);

    public async Task<IReadOnlyList<MailIdentity>> GetSenderIdentitiesAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        return await WithGoogleCredentialsAsync(accountId, async credentials =>
        {
            var identities = await googleMailService.GetSenderIdentitiesAsync(credentials, cancellationToken, accountId);
            return identities
                .Where(identity => !string.IsNullOrWhiteSpace(identity.SendAsEmail))
                .OrderByDescending(static identity => identity.IsPrimary || identity.IsDefault)
                .ThenBy(identity => identity.SendAsEmail, StringComparer.OrdinalIgnoreCase)
                .Select(MapIdentity)
                .ToList();
        });
    }

    public async Task<ProviderDraftReference> SaveDraftAsync(
        string accountId,
        ProviderComposedMessage message,
        ProviderDraftReference? existingDraft = null,
        CancellationToken cancellationToken = default)
    {
        return await WithGoogleCredentialsAsync(accountId, async credentials =>
        {
            var draftData = ResolveDraftReferenceData(existingDraft);
            var gmailDraft = await googleMailService.SaveDraftAsync(
                credentials,
                MapComposeRequest(message, existingDraft, draftData),
                ResolveDraftId(existingDraft, draftData),
                cancellationToken,
                accountId);

            return MapDraftReference(gmailDraft, message.SenderIdentity.Id);
        });
    }

    public Task DeleteDraftAsync(
        string accountId,
        ProviderDraftReference draft,
        CancellationToken cancellationToken = default) =>
        WithGoogleCredentialsAsync(
            accountId,
            credentials => googleMailService.DeleteDraftAsync(
                credentials,
                RequireDraftId(draft),
                cancellationToken,
                accountId));

    public async Task<ProviderSendResult> SendAsync(
        string accountId,
        ProviderComposedMessage message,
        ProviderDraftReference? existingDraft = null,
        CancellationToken cancellationToken = default)
    {
        return await WithGoogleCredentialsAsync(accountId, async credentials =>
        {
            var draftData = ResolveDraftReferenceData(existingDraft);
            var sentMessage = await googleMailService.SendAsync(
                credentials,
                MapComposeRequest(message, existingDraft, draftData),
                ResolveDraftId(existingDraft, draftData),
                cancellationToken,
                accountId);

            return new ProviderSendResult
            {
                SentMessageExternalId = sentMessage.Id,
                SentThreadExternalId = sentMessage.ThreadId
                    ?? message.ThreadExternalId
                    ?? existingDraft?.ThreadExternalId
                    ?? draftData?.ThreadId,
                RawDataJson = JsonSerializer.Serialize(
                    new GoogleSentReferenceData
                    {
                        MessageId = sentMessage.Id,
                        ThreadId = sentMessage.ThreadId,
                        HistoryId = sentMessage.HistoryId
                    },
                    JsonOptions)
            };
        });
    }

    public async Task<MailboxSyncResult> GetMailboxesAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        return await WithGoogleCredentialsAsync(accountId, async credentials =>
        {
            var labels = await googleMailService.GetLabelsAsync(credentials, cancellationToken, accountId);
            var labelIdsByName = labels
                .Where(label => !string.IsNullOrWhiteSpace(label.Id) && !string.IsNullOrWhiteSpace(label.Name))
                .GroupBy(label => label.Name!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Id!, StringComparer.Ordinal);

            var mailboxes = labels
                .Where(label => !string.IsNullOrWhiteSpace(label.Id))
                .Select(label => MapMailbox(label, labelIdsByName))
                .ToList();

            return new MailboxSyncResult
            {
                Mailboxes = mailboxes,
                SyncToken = null
            };
        });
    }

    public async Task<MailMessageSyncResult> GetMessagesAsync(
        string accountId,
        string mailboxExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        return await WithGoogleCredentialsAsync(accountId, async credentials =>
        {
            var page = await googleMailService.GetMessagesAsync(
                credentials,
                mailboxExternalId,
                syncToken,
                cancellationToken,
                accountId);

            return new MailMessageSyncResult
            {
                Messages = page.Messages
                    .Where(message => !string.IsNullOrWhiteSpace(message.Id))
                    .Select(message => MapMessage(message, includeBodies: false))
                    .ToList(),
                SyncToken = page.NextPageToken,
                MissingMessagesAreAuthoritative = false
            };
        });
    }

    public async Task<HydratedMailMessage> HydrateMessageAsync(
        string accountId,
        string messageExternalId,
        CancellationToken cancellationToken = default)
    {
        return await WithGoogleCredentialsAsync(accountId, async credentials =>
        {
            var message = await googleMailService.GetMessageAsync(
                credentials,
                messageExternalId,
                GoogleMailService.GmailMessageFormat.Full,
                cancellationToken,
                accountId);

            return new HydratedMailMessage
            {
                Message = MapMessage(message, includeBodies: true)
            };
        });
    }

    public async Task<DownloadedMailAttachment> DownloadAttachmentAsync(
        string accountId,
        string messageExternalId,
        string attachmentExternalId,
        CancellationToken cancellationToken = default)
    {
        return await WithGoogleCredentialsAsync(accountId, async credentials =>
        {
            var message = await googleMailService.GetMessageAsync(
                credentials,
                messageExternalId,
                GoogleMailService.GmailMessageFormat.Full,
                cancellationToken,
                accountId);
            var attachmentPart = FindAttachmentPart(message.Payload, attachmentExternalId)
                                 ?? throw new InvalidOperationException(
                                     $"Attachment '{attachmentExternalId}' was not found on message '{messageExternalId}'");
            var attachmentBody = await googleMailService.DownloadAttachmentAsync(
                credentials,
                messageExternalId,
                attachmentExternalId,
                cancellationToken,
                accountId);

            return new DownloadedMailAttachment
            {
                FileName = string.IsNullOrWhiteSpace(attachmentPart.Filename)
                    ? attachmentExternalId
                    : attachmentPart.Filename,
                MimeType = attachmentPart.MimeType ?? "application/octet-stream",
                Content = DecodeBase64Url(attachmentBody.Data)
            };
        });
    }

    public Task SetReadStateAsync(
        string accountId,
        string messageExternalId,
        bool isRead,
        CancellationToken cancellationToken = default) =>
        WithGoogleCredentialsAsync(
            accountId,
            credentials => googleMailService.ModifyMessageAsync(
                credentials,
                messageExternalId,
                addLabelIds: isRead ? null : ["UNREAD"],
                removeLabelIds: isRead ? ["UNREAD"] : null,
                cancellationToken: cancellationToken,
                accountId: accountId));

    public Task SetStarredStateAsync(
        string accountId,
        string messageExternalId,
        bool isStarred,
        CancellationToken cancellationToken = default) =>
        WithGoogleCredentialsAsync(
            accountId,
            credentials => googleMailService.ModifyMessageAsync(
                credentials,
                messageExternalId,
                addLabelIds: isStarred ? ["STARRED"] : null,
                removeLabelIds: isStarred ? null : ["STARRED"],
                cancellationToken: cancellationToken,
                accountId: accountId));

    public Task ArchiveMessageAsync(
        string accountId,
        string messageExternalId,
        CancellationToken cancellationToken = default) =>
        WithGoogleCredentialsAsync(
            accountId,
            credentials => googleMailService.ModifyMessageAsync(
                credentials,
                messageExternalId,
                addLabelIds: null,
                removeLabelIds: ["INBOX"],
                cancellationToken: cancellationToken,
                accountId: accountId));

    public Task DeleteMessageAsync(
        string accountId,
        string messageExternalId,
        CancellationToken cancellationToken = default) =>
        WithGoogleCredentialsAsync(
            accountId,
            credentials => googleMailService.TrashMessageAsync(
                credentials,
                messageExternalId,
                cancellationToken: cancellationToken,
                accountId: accountId));

    public async Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WithGoogleCredentialsAsync(accountId, async credentials =>
            {
                await googleMailService.GetLabelsAsync(credentials, cancellationToken, accountId);
                return true;
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<T> WithGoogleCredentialsAsync<T>(string accountId, Func<GoogleCredentials, Task<T>> action)
    {
        var credentials = credentialManager.GetGoogleCredentials(accountId)
                         ?? throw new InvalidOperationException($"No Google credentials found for account {accountId}");

        try
        {
            return await action(credentials);
        }
        finally
        {
            credentialManager.StoreGoogleCredentials(accountId, credentials);
        }
    }

    private async Task WithGoogleCredentialsAsync(string accountId, Func<GoogleCredentials, Task> action)
    {
        var credentials = credentialManager.GetGoogleCredentials(accountId)
                         ?? throw new InvalidOperationException($"No Google credentials found for account {accountId}");

        try
        {
            await action(credentials);
        }
        finally
        {
            credentialManager.StoreGoogleCredentials(accountId, credentials);
        }
    }

    private static MailIdentity MapIdentity(GoogleMailService.GmailSendAs identity)
    {
        var verificationStatus = identity.VerificationStatus;
        return new MailIdentity
        {
            Id = identity.SendAsEmail!,
            DisplayName = identity.DisplayName ?? string.Empty,
            Address = identity.SendAsEmail!,
            IsPrimary = identity.IsPrimary || identity.IsDefault,
            CanSend = identity.IsPrimary
                      || identity.IsDefault
                      || string.IsNullOrWhiteSpace(verificationStatus)
                      || string.Equals(verificationStatus, "accepted", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static GoogleMailService.GmailComposeRequest MapComposeRequest(
        ProviderComposedMessage message,
        ProviderDraftReference? existingDraft,
        GoogleDraftReferenceData? draftData)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new GoogleMailService.GmailComposeRequest
        {
            SenderAddress = message.SenderIdentity.Address,
            SenderDisplayName = message.SenderIdentity.DisplayName,
            To = message.To,
            Cc = message.Cc,
            Bcc = message.Bcc,
            Subject = message.Subject,
            PlainTextBody = message.PlainTextBody,
            HtmlBody = message.HtmlBody,
            InReplyTo = message.InReplyTo,
            References = message.References,
            ThreadId = message.ThreadExternalId
                ?? existingDraft?.ThreadExternalId
                ?? draftData?.ThreadId,
            Attachments = message.Attachments
                .Select(attachment => new GoogleMailService.GmailComposeAttachment
                {
                    AttachmentId = attachment.AttachmentId,
                    FileName = attachment.FileName,
                    MimeType = attachment.MimeType,
                    ContentPath = attachment.ContentPath,
                    IsInline = attachment.IsInline,
                    ContentId = attachment.ContentId
                })
                .ToList()
        };
    }

    private static ProviderDraftReference MapDraftReference(GoogleMailService.GmailDraft draft, string senderIdentityId)
    {
        var data = new GoogleDraftReferenceData
        {
            DraftId = draft.Id,
            MessageId = draft.Message?.Id,
            ThreadId = draft.Message?.ThreadId,
            IdentityId = senderIdentityId,
            HistoryId = draft.Message?.HistoryId
        };

        return new ProviderDraftReference
        {
            ProviderDraftId = data.DraftId,
            MessageExternalId = data.MessageId,
            ThreadExternalId = data.ThreadId,
            MailboxExternalId = "DRAFT",
            IdentityId = data.IdentityId,
            StateToken = data.HistoryId,
            RawDataJson = JsonSerializer.Serialize(data, JsonOptions)
        };
    }

    private static string RequireDraftId(ProviderDraftReference draft)
    {
        var draftId = ResolveDraftId(draft, ResolveDraftReferenceData(draft));
        if (!string.IsNullOrWhiteSpace(draftId))
            return draftId;
        throw new InvalidOperationException("Google draft reference is missing a provider draft id");
    }

    private static string? ResolveDraftId(ProviderDraftReference? draft, GoogleDraftReferenceData? draftData) =>
        !string.IsNullOrWhiteSpace(draft?.ProviderDraftId) ? draft.ProviderDraftId : draftData?.DraftId;

    private static GoogleDraftReferenceData? ResolveDraftReferenceData(ProviderDraftReference? draft)
    {
        if (string.IsNullOrWhiteSpace(draft?.RawDataJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GoogleDraftReferenceData>(draft.RawDataJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProviderMailbox MapMailbox(
        GoogleMailService.GmailLabel label,
        IReadOnlyDictionary<string, string> labelIdsByName)
    {
        var parentExternalId = GetParentExternalId(label, labelIdsByName);
        return new ProviderMailbox
        {
            ExternalId = label.Id!,
            ParentExternalId = parentExternalId,
            Name = label.Name ?? label.Id!,
            Role = MapMailboxRole(label),
            UnreadCount = label.MessagesUnread ?? label.ThreadsUnread ?? 0,
            TotalCount = label.MessagesTotal ?? label.ThreadsTotal ?? 0,
            Enabled = !string.Equals(label.LabelListVisibility, "labelHide", StringComparison.OrdinalIgnoreCase),
            Deleted = false,
            Data = BuildRawData(label)
        };
    }

    private static string? GetParentExternalId(
        GoogleMailService.GmailLabel label,
        IReadOnlyDictionary<string, string> labelIdsByName)
    {
        if (string.IsNullOrWhiteSpace(label.Name))
            return null;

        var separatorIndex = label.Name.LastIndexOf('/');
        if (separatorIndex <= 0)
            return null;

        var parentName = label.Name[..separatorIndex];
        return labelIdsByName.TryGetValue(parentName, out var parentExternalId) ? parentExternalId : null;
    }

    private static string? MapMailboxRole(GoogleMailService.GmailLabel label) => label.Id switch
    {
        "INBOX" => "inbox",
        "SENT" => "sent",
        "DRAFT" => "drafts",
        "TRASH" => "trash",
        "SPAM" => "junk",
        "STARRED" => "starred",
        "IMPORTANT" => "important",
        "UNREAD" => "unread",
        "CATEGORY_PERSONAL" => "personal",
        "CATEGORY_SOCIAL" => "social",
        "CATEGORY_PROMOTIONS" => "promotions",
        "CATEGORY_UPDATES" => "updates",
        "CATEGORY_FORUMS" => "forums",
        _ => null
    };

    private static ProviderMailMessage MapMessage(
        GoogleMailService.GmailMessage message,
        bool includeBodies)
    {
        var payload = message.Payload;
        var plainTextBuilder = includeBodies ? new StringBuilder() : null;
        var htmlBuilder = includeBodies ? new StringBuilder() : null;
        var attachments = new List<ProviderMailAttachment>();
        CollectParts(payload, plainTextBuilder, htmlBuilder, attachments);

        var sender = ParseSingleAddress(GetHeaderValue(payload, "From"));
        var htmlBody = htmlBuilder is { Length: > 0 } ? htmlBuilder.ToString() : null;
        var plainTextBody = plainTextBuilder is { Length: > 0 } ? plainTextBuilder.ToString() : null;

        return new ProviderMailMessage
        {
            ExternalId = message.Id!,
            ThreadExternalId = string.IsNullOrWhiteSpace(message.ThreadId) ? message.Id! : message.ThreadId!,
            InternetMessageId = GetHeaderValue(payload, "Message-ID"),
            Subject = GetHeaderValue(payload, "Subject"),
            SenderName = sender?.Name,
            SenderAddress = sender?.Address,
            SentAtUnixTime = ParseSentAtUnixTime(message, payload),
            ReceivedAtUnixTime = ParseReceivedAtUnixTime(message),
            Preview = message.Snippet,
            PlainTextBody = includeBodies ? plainTextBody : null,
            HtmlBody = includeBodies ? htmlBody : null,
            HasHtmlBody = !string.IsNullOrWhiteSpace(htmlBody) || HasMimeType(payload, "text/html"),
            HasPlainTextBody = !string.IsNullOrWhiteSpace(plainTextBody) || HasMimeType(payload, "text/plain"),
            HasAttachments = attachments.Count > 0,
            HasExternalResources = includeBodies && ContainsExternalResources(htmlBody),
            HasBlockedContent = false,
            IsUnread = HasLabel(message, "UNREAD"),
            IsStarred = HasLabel(message, "STARRED"),
            IsAnswered = HasLabel(message, "ANSWERED"),
            IsDraft = HasLabel(message, "DRAFT"),
            Deleted = HasLabel(message, "TRASH"),
            MailboxExternalIds = message.LabelIds?.Where(labelId => !string.IsNullOrWhiteSpace(labelId)).ToList() ?? [],
            To = ParseAddresses(GetHeaderValue(payload, "To")),
            Cc = ParseAddresses(GetHeaderValue(payload, "Cc")),
            Bcc = ParseAddresses(GetHeaderValue(payload, "Bcc")),
            ReplyTo = ParseAddresses(GetHeaderValue(payload, "Reply-To")),
            Attachments = attachments,
            Data = BuildRawData(message)
        };
    }

    private static void CollectParts(
        GoogleMailService.GmailMessagePart? part,
        StringBuilder? plainTextBuilder,
        StringBuilder? htmlBuilder,
        List<ProviderMailAttachment> attachments)
    {
        if (part == null)
            return;

        var attachmentId = part.Body?.AttachmentId;
        if (IsAttachment(part, attachmentId))
        {
            attachments.Add(new ProviderMailAttachment
            {
                ExternalId = attachmentId!,
                FileName = string.IsNullOrWhiteSpace(part.Filename) ? null : part.Filename,
                MimeType = part.MimeType,
                Size = part.Body?.Size ?? 0,
                IsInline = IsInline(part),
                ContentId = NormalizeContentId(GetHeaderValue(part, "Content-ID")),
                Data = BuildRawData(part)
            });
        }
        else if (plainTextBuilder != null || htmlBuilder != null)
        {
            var bodyData = part.Body?.Data;
            if (!string.IsNullOrWhiteSpace(bodyData))
            {
                var decoded = DecodeBase64UrlToString(bodyData);
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    if (string.Equals(part.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase) && plainTextBuilder != null)
                    {
                        AppendBodySegment(plainTextBuilder, decoded);
                    }
                    else if (string.Equals(part.MimeType, "text/html", StringComparison.OrdinalIgnoreCase) && htmlBuilder != null)
                    {
                        AppendBodySegment(htmlBuilder, decoded);
                    }
                }
            }
        }

        if (part.Parts is not { Count: > 0 })
            return;

        foreach (var childPart in part.Parts)
            CollectParts(childPart, plainTextBuilder, htmlBuilder, attachments);
    }

    private static bool HasMimeType(GoogleMailService.GmailMessagePart? part, string mimeType)
    {
        if (part == null)
            return false;
        if (string.Equals(part.MimeType, mimeType, StringComparison.OrdinalIgnoreCase))
            return true;
        return part.Parts is { Count: > 0 } && part.Parts.Any(childPart => HasMimeType(childPart, mimeType));
    }

    private static GoogleMailService.GmailMessagePart? FindAttachmentPart(
        GoogleMailService.GmailMessagePart? part,
        string attachmentId)
    {
        if (part == null)
            return null;
        if (string.Equals(part.Body?.AttachmentId, attachmentId, StringComparison.Ordinal))
            return part;
        if (part.Parts is not { Count: > 0 })
            return null;

        foreach (var childPart in part.Parts)
        {
            var match = FindAttachmentPart(childPart, attachmentId);
            if (match != null)
                return match;
        }

        return null;
    }

    private static bool IsAttachment(GoogleMailService.GmailMessagePart part, string? attachmentId)
    {
        if (string.IsNullOrWhiteSpace(attachmentId))
            return false;
        if (!string.IsNullOrWhiteSpace(part.Filename))
            return true;

        var disposition = GetHeaderValue(part, "Content-Disposition");
        if (!string.IsNullOrWhiteSpace(disposition)
            && (disposition.Contains("attachment", StringComparison.OrdinalIgnoreCase)
                || disposition.Contains("inline", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return !string.Equals(part.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(part.MimeType, "text/html", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(part.MimeType, "multipart/alternative", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(part.MimeType, "multipart/mixed", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(part.MimeType, "multipart/related", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInline(GoogleMailService.GmailMessagePart part)
    {
        var disposition = GetHeaderValue(part, "Content-Disposition");
        return !string.IsNullOrWhiteSpace(disposition)
               && disposition.Contains("inline", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetHeaderValue(GoogleMailService.GmailMessagePart? part, string headerName)
    {
        return part?.Headers?
            .FirstOrDefault(header => string.Equals(header.Name, headerName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static bool HasLabel(GoogleMailService.GmailMessage message, string labelId) =>
        message.LabelIds?.Any(label => string.Equals(label, labelId, StringComparison.Ordinal)) == true;

    private static long? ParseReceivedAtUnixTime(GoogleMailService.GmailMessage message)
    {
        if (!long.TryParse(message.InternalDate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var internalDateMs))
            return null;
        return DateTimeOffset.FromUnixTimeMilliseconds(internalDateMs).ToUnixTimeSeconds();
    }

    private static long? ParseSentAtUnixTime(
        GoogleMailService.GmailMessage message,
        GoogleMailService.GmailMessagePart? payload)
    {
        var dateHeader = GetHeaderValue(payload, "Date");
        if (!string.IsNullOrWhiteSpace(dateHeader)
            && DateTimeOffset.TryParse(dateHeader, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var sentAt))
        {
            return sentAt.ToUnixTimeSeconds();
        }

        return ParseReceivedAtUnixTime(message);
    }

    private static List<ProviderMailAddress> ParseAddresses(string? rawHeaderValue)
    {
        if (string.IsNullOrWhiteSpace(rawHeaderValue))
            return [];

        try
        {
            var parsed = new MailAddressCollection();
            parsed.Add(rawHeaderValue);
            return parsed
                .Cast<System.Net.Mail.MailAddress>()
                .Select(address => new ProviderMailAddress
                {
                    Name = address.DisplayName ?? string.Empty,
                    Address = address.Address
                })
                .ToList();
        }
        catch (FormatException)
        {
            return rawHeaderValue
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(address => new ProviderMailAddress
                {
                    Name = string.Empty,
                    Address = address
                })
                .ToList();
        }
    }

    private static ProviderMailAddress? ParseSingleAddress(string? rawHeaderValue) => ParseAddresses(rawHeaderValue).FirstOrDefault();

    private static string? NormalizeContentId(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return null;
        return contentId.Trim().Trim('<', '>');
    }

    private static void AppendBodySegment(StringBuilder builder, string segment)
    {
        if (builder.Length > 0)
            builder.AppendLine();
        builder.Append(segment);
    }

    private static bool ContainsExternalResources(string? htmlBody)
    {
        if (string.IsNullOrWhiteSpace(htmlBody))
            return false;

        return htmlBody.Contains("http://", StringComparison.OrdinalIgnoreCase)
               || htmlBody.Contains("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] DecodeBase64Url(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var normalized = value.Replace('-', '+').Replace('_', '/');
        var remainder = normalized.Length % 4;
        if (remainder != 0)
            normalized = normalized.PadRight(normalized.Length + (4 - remainder), '=');
        return Convert.FromBase64String(normalized);
    }

    private static string? DecodeBase64UrlToString(string? value)
    {
        var bytes = DecodeBase64Url(value);
        return bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes);
    }

    private static Dictionary<string, DataAttribute> BuildRawData<T>(T value) => new()
    {
        ["rawData"] = new DataAttribute.JsonText(JsonSerializer.Serialize(value, JsonOptions))
    };

    private sealed class GoogleDraftReferenceData
    {
        public string? DraftId { get; init; }
        public string? MessageId { get; init; }
        public string? ThreadId { get; init; }
        public string? IdentityId { get; init; }
        public string? HistoryId { get; init; }
    }

    private sealed class GoogleSentReferenceData
    {
        public string? MessageId { get; init; }
        public string? ThreadId { get; init; }
        public string? HistoryId { get; init; }
    }
}
