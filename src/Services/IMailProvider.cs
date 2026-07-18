using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using perinma.Models;

namespace perinma.Services;

public interface IMailProvider
{
    CredentialManagerService CredentialManager { get; }

    Task<MailboxSyncResult> GetMailboxesAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    Task<MailMessageSyncResult> GetMessagesAsync(
        string accountId,
        string mailboxExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    Task<HydratedMailMessage> HydrateMessageAsync(
        string accountId,
        string messageExternalId,
        CancellationToken cancellationToken = default);

    Task<DownloadedMailAttachment> DownloadAttachmentAsync(
        string accountId,
        string messageExternalId,
        string attachmentExternalId,
        CancellationToken cancellationToken = default);

    Task SetReadStateAsync(
        string accountId,
        string messageExternalId,
        bool isRead,
        CancellationToken cancellationToken = default);

    Task SetStarredStateAsync(
        string accountId,
        string messageExternalId,
        bool isStarred,
        CancellationToken cancellationToken = default);

    Task ArchiveMessageAsync(
        string accountId,
        string messageExternalId,
        CancellationToken cancellationToken = default);

    Task DeleteMessageAsync(
        string accountId,
        string messageExternalId,
        CancellationToken cancellationToken = default);

    Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default);
}

public class MailboxSyncResult
{
    public required IList<ProviderMailbox> Mailboxes { get; init; }
    public string? SyncToken { get; init; }
}

public class MailMessageSyncResult
{
    public required IList<ProviderMailMessage> Messages { get; init; }
    public string? SyncToken { get; init; }
    public bool MissingMessagesAreAuthoritative { get; init; }
}

public class ProviderMailbox
{
    public required string ExternalId { get; init; }
    public string? ParentExternalId { get; init; }
    public required string Name { get; init; }
    public string? Role { get; init; }
    public int UnreadCount { get; init; }
    public int TotalCount { get; init; }
    public bool Enabled { get; init; } = true;
    public bool Deleted { get; init; }
    public Dictionary<string, DataAttribute> Data { get; init; } = new();
}

public class ProviderMailAttachment
{
    public required string ExternalId { get; init; }
    public string? FileName { get; init; }
    public string? MimeType { get; init; }
    public int Size { get; init; }
    public bool IsInline { get; init; }
    public string? ContentId { get; init; }
    public Dictionary<string, DataAttribute> Data { get; init; } = new();
}

public class ProviderMailMessage
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
    public bool Deleted { get; init; }
    public IList<string> MailboxExternalIds { get; init; } = [];
    public IList<MailAddress> To { get; init; } = [];
    public IList<MailAddress> Cc { get; init; } = [];
    public IList<MailAddress> Bcc { get; init; } = [];
    public IList<MailAddress> ReplyTo { get; init; } = [];
    public IList<ProviderMailAttachment> Attachments { get; init; } = [];
    public Dictionary<string, DataAttribute> Data { get; init; } = new();
}

public class HydratedMailMessage
{
    public required ProviderMailMessage Message { get; init; }
}

public class DownloadedMailAttachment
{
    public required string FileName { get; init; }
    public required string MimeType { get; init; }
    public required byte[] Content { get; init; }
}
