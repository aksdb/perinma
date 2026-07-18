using System;
using System.Collections.Generic;

namespace perinma.Models;

public enum MailBodyKind
{
    Auto,
    PlainText,
    Html
}

public enum MailActionType
{
    MarkRead,
    MarkUnread,
    Star,
    Unstar,
    Archive,
    Delete
}

public class Mailbox
{
    public required Guid Id { get; set; }
    public required Account Account { get; set; }
    public string? ExternalId { get; set; }
    public string? ParentExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
    public bool Enabled { get; set; } = true;
}

public class MailThread
{
    public required Guid Id { get; set; }
    public required Account Account { get; set; }
    public string? ExternalId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string ParticipantsSummary { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
    public DateTimeOffset? LatestMessageReceivedAt { get; set; }
    public int UnreadCount { get; set; }
    public int MessageCount { get; set; }
    public bool HasAttachments { get; set; }
}

public class MailAddress
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

public class MailBody
{
    public string PlainText { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public bool HasExternalResources { get; set; }
    public bool HasBlockedContent { get; set; }
    public MailBodyKind PreferredBodyKind { get; set; } = MailBodyKind.Auto;
}

public class MailAttachment
{
    public required Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public int Size { get; set; }
    public bool IsInline { get; set; }
    public string? ContentId { get; set; }
    public string? ContentPath { get; set; }
}

public class MailMessage
{
    public required Guid Id { get; set; }
    public required Guid ThreadId { get; set; }
    public required Account Account { get; set; }
    public string? ExternalId { get; set; }
    public string? InternetMessageId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public MailAddress? Sender { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public string Preview { get; set; } = string.Empty;
    public MailBody Body { get; set; } = new();
    public List<MailAttachment> Attachments { get; set; } = [];
    public bool IsUnread { get; set; }
    public bool IsStarred { get; set; }
    public bool IsAnswered { get; set; }
    public bool IsDraft { get; set; }
    public List<MailAddress> To { get; set; } = [];
    public List<MailAddress> Cc { get; set; } = [];
    public List<MailAddress> Bcc { get; set; } = [];
    public List<MailAddress> ReplyTo { get; set; } = [];
}
