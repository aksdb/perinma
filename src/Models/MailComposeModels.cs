using System;
using System.Collections.Generic;

namespace perinma.Models;

public enum MailComposeKind
{
    New,
    Reply,
    ReplyAll,
    Forward
}

public enum MailComposeDraftStatus
{
    LocalOnly,
    PendingRemoteSave,
    Synced,
    Conflict,
    SendFailed
}

public enum MailRecipientKind
{
    To,
    Cc,
    Bcc
}

public sealed class MailComposeDraft
{
    public required Guid Id { get; set; }
    public required Guid AccountId { get; set; }
    public MailComposeKind Kind { get; set; } = MailComposeKind.New;
    public Guid? SourceMessageId { get; set; }
    public string? SourceMessageExternalId { get; set; }
    public Guid? SourceThreadId { get; set; }
    public string? SourceThreadExternalId { get; set; }
    public string? SourceInternetMessageId { get; set; }
    public string? RemoteDraftReferenceJson { get; set; }
    public string? SelectedIdentityId { get; set; }
    public string? SelectedIdentityDisplayName { get; set; }
    public string? SelectedIdentityAddress { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string ToText { get; set; } = string.Empty;
    public string CcText { get; set; } = string.Empty;
    public string BccText { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string PlainTextBody { get; set; } = string.Empty;
    public MailComposeDraftStatus Status { get; set; } = MailComposeDraftStatus.LocalOnly;
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastLocalSaveAt { get; set; }
    public DateTimeOffset? LastRemoteSaveAt { get; set; }
    public List<MailComposeRecipient> Recipients { get; set; } = [];
    public List<MailComposeAttachment> Attachments { get; set; } = [];
}

public sealed class MailComposeRecipient
{
    public required Guid Id { get; set; }
    public MailRecipientKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class MailComposeAttachment
{
    public required Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public bool IsInline { get; set; }
    public string? ContentId { get; set; }
    public string? ContentPath { get; set; }
    public string? Hash { get; set; }
    public string? ProviderReferenceJson { get; set; }
    public int SortOrder { get; set; }
}

public sealed class MailComposeSourceMessage
{
    public required Guid AccountId { get; set; }
    public required AccountType AccountType { get; set; }
    public Guid? MessageId { get; set; }
    public string? MessageExternalId { get; set; }
    public Guid? ThreadId { get; set; }
    public string? ThreadExternalId { get; set; }
    public string? InternetMessageId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public MailAddress? Sender { get; set; }
    public List<MailAddress> To { get; set; } = [];
    public List<MailAddress> Cc { get; set; } = [];
    public List<MailAddress> ReplyTo { get; set; } = [];
    public DateTimeOffset? SentAt { get; set; }
    public string HtmlBody { get; set; } = string.Empty;
    public string PlainTextBody { get; set; } = string.Empty;
    public List<MailComposeSourceAttachment> Attachments { get; set; } = [];
}

public sealed class MailComposeSourceAttachment
{
    public string? AttachmentId { get; set; }
    public string? ExternalId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/octet-stream";
    public string? ContentPath { get; set; }
    public bool IsInline { get; set; }
}

public sealed class MailIdentity
{
    public required string Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public required string Address { get; set; }
    public bool IsPrimary { get; set; }
    public bool CanSend { get; set; } = true;

    public string DisplayAddress => string.IsNullOrWhiteSpace(DisplayName)
        ? Address
        : $"{DisplayName} <{Address}>";
}
