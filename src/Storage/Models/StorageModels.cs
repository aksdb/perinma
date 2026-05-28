using System;
using perinma.Models;

namespace perinma.Storage.Models;

public class AccountDbo
{
    public required string AccountId { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public int Capabilities { get; set; }
    public int SortOrder { get; set; }

    public AccountType AccountTypeEnum =>
        Enum.TryParse<AccountType>(Type, ignoreCase: true, out var result)
            ? result
            : throw new ArgumentException("Unknown account type.");

    public AccountCapability AccountCapabilities => (AccountCapability)Capabilities;

    public bool SupportsCalendar => AccountCapabilities.HasFlag(AccountCapability.Calendar);
    public bool SupportsContacts => AccountCapabilities.HasFlag(AccountCapability.Contacts);
    public bool SupportsMail => AccountCapabilities.HasFlag(AccountCapability.Mail);

    public static AccountCapability GetDefaultCapabilities(AccountType accountType) => accountType switch
    {
        AccountType.Google => AccountCapability.Calendar | AccountCapability.Contacts,
        AccountType.CalDav => AccountCapability.Calendar,
        AccountType.CardDav => AccountCapability.Contacts,
        AccountType.Jmap => AccountCapability.Mail,
        _ => AccountCapability.None
    };
}

public class CalendarDbo
{
    public required string AccountId { get; set; }
    public required string CalendarId { get; set; }
    public string? ExternalId { get; set; }
    public required string Name { get; set; }
    public string? Color { get; set; }
    public int Enabled { get; set; }
    public long? LastSync { get; set; }
}

public class CalendarEventDbo
{
    public string CalendarId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public long? StartTime { get; set; }
    public long? EndTime { get; set; }
    public string? Title { get; set; }
    public long? ChangedAt { get; set; }
}

public class CalendarEventQueryResult
{
    public required string EventId { get; init; }
    public string? ExternalId { get; init; }
    public long? StartTime { get; init; }
    public long? EndTime { get; init; }
    public string? Title { get; init; }
    public long? ChangedAt { get; init; }
    public string? RawData { get; init; }
    public required string CalendarId { get; init; }
    public string? CalendarExternalId { get; init; }
    public required string CalendarName { get; init; }
    public string? CalendarColor { get; init; }
    public int CalendarEnabled { get; init; }
    public long? CalendarLastSync { get; init; }
    public required string AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountType { get; init; }

    public AccountType AccountTypeEnum => Enum.TryParse<AccountType>(AccountType, ignoreCase: true, out var result) ? result : perinma.Models.AccountType.Google;
}

public class AddressBookDbo
{
    public required string AccountId { get; set; }
    public required string AddressBookId { get; set; }
    public string? ExternalId { get; set; }
    public required string Name { get; set; }
    public int Enabled { get; set; }
    public long? LastSync { get; set; }
}

public class ContactDbo
{
    public string AddressBookId { get; set; } = string.Empty;
    public string ContactId { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? DisplayName { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? PrimaryEmail { get; set; }
    public string? PrimaryPhone { get; set; }
    public string? PhotoUrl { get; set; }
    public long? ChangedAt { get; set; }
}

public class ContactGroupDbo
{
    public required string AccountId { get; set; }
    public required string GroupId { get; set; }
    public string? ExternalId { get; set; }
    public required string Name { get; set; }
    public int SystemGroup { get; set; }
}

public class ContactQueryResult
{
    public required string ContactId { get; init; }
    public string? ExternalId { get; init; }
    public string? DisplayName { get; init; }
    public string? GivenName { get; init; }
    public string? FamilyName { get; init; }
    public string? PrimaryEmail { get; init; }
    public string? PrimaryPhone { get; init; }
    public string? PhotoUrl { get; init; }
    public long? ChangedAt { get; init; }
    public string? RawData { get; init; }
    public required string AddressBookId { get; init; }
    public string? AddressBookExternalId { get; init; }
    public required string AddressBookName { get; init; }
    public int AddressBookEnabled { get; init; }
    public long? AddressBookLastSync { get; init; }
    public required string AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountType { get; init; }

    public AccountType AccountTypeEnum => Enum.TryParse<AccountType>(AccountType, ignoreCase: true, out var result) ? result : perinma.Models.AccountType.Google;
}

public class AddressBookQueryResult
{
    public required string AddressBookId { get; init; }
    public string? ExternalId { get; init; }
    public required string Name { get; init; }
    public int Enabled { get; init; }
    public long? LastSync { get; init; }
    public required string AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountType { get; init; }
    public int AccountSortOrder { get; init; }
    public int ContactCount { get; init; }

    public bool IsEnabled => Enabled == 1;
    public AccountType AccountTypeEnum => Enum.TryParse<AccountType>(AccountType, ignoreCase: true, out var result) ? result : perinma.Models.AccountType.Google;
}

public class ContactGroupQueryResult
{
    public required string GroupId { get; init; }
    public string? ExternalId { get; init; }
    public required string Name { get; init; }
    public int SystemGroup { get; init; }
    public required string AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountType { get; init; }
    public int AccountSortOrder { get; init; }
    public int MemberCount { get; init; }

    public bool IsSystemGroup => SystemGroup == 1;
    public AccountType AccountTypeEnum => Enum.TryParse<AccountType>(AccountType, ignoreCase: true, out var result) ? result : perinma.Models.AccountType.Google;
}

public class MailboxDbo
{
    public required string AccountId { get; set; }
    public required string MailboxId { get; set; }
    public string? ExternalId { get; set; }
    public string? ParentExternalId { get; set; }
    public required string Name { get; set; }
    public string? Role { get; set; }
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
    public int Enabled { get; set; }
    public long? LastSync { get; set; }
}

public class MailThreadDbo
{
    public required string AccountId { get; set; }
    public required string ThreadId { get; set; }
    public string? ExternalId { get; set; }
    public string? Subject { get; set; }
    public string? ParticipantsSummary { get; set; }
    public string? Preview { get; set; }
    public long? LatestMessageReceivedAt { get; set; }
    public int UnreadCount { get; set; }
    public int MessageCount { get; set; }
    public int HasAttachments { get; set; }
}

public class MailMessageDbo
{
    public required string AccountId { get; set; }
    public required string ThreadId { get; set; }
    public required string MessageId { get; set; }
    public string? ExternalId { get; set; }
    public string? InternetMessageId { get; set; }
    public string? Subject { get; set; }
    public string? SenderName { get; set; }
    public string? SenderAddress { get; set; }
    public long? SentAt { get; set; }
    public long? ReceivedAt { get; set; }
    public string? Preview { get; set; }
    public string? PlainTextBody { get; set; }
    public string? HtmlBody { get; set; }
    public long? BodyFetchedAt { get; set; }
    public int HasHtmlBody { get; set; }
    public int HasPlainTextBody { get; set; }
    public int HasAttachments { get; set; }
    public int HasExternalResources { get; set; }
    public int HasBlockedContent { get; set; }
    public int IsUnread { get; set; }
    public int IsStarred { get; set; }
    public int IsAnswered { get; set; }
    public int IsDraft { get; set; }
    public long? ChangedAt { get; set; }
}

public class MailAttachmentDbo
{
    public required string MessageId { get; set; }
    public required string AttachmentId { get; set; }
    public string? ExternalId { get; set; }
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public int Size { get; set; }
    public int IsInline { get; set; }
    public string? ContentId { get; set; }
    public string? ContentPath { get; set; }
    public long? DownloadedAt { get; set; }
}

public class MailboxQueryResult
{
    public required string MailboxId { get; init; }
    public string? ExternalId { get; init; }
    public string? ParentExternalId { get; init; }
    public required string Name { get; init; }
    public string? Role { get; init; }
    public int UnreadCount { get; init; }
    public int TotalCount { get; init; }
    public int Enabled { get; init; }
    public long? LastSync { get; init; }
    public required string AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountType { get; init; }
    public int AccountCapabilities { get; init; }
    public int AccountSortOrder { get; init; }

    public bool IsEnabled => Enabled == 1;
    public AccountType AccountTypeEnum => Enum.TryParse<AccountType>(AccountType, ignoreCase: true, out var result) ? result : perinma.Models.AccountType.Google;
    public AccountCapability Capabilities => (AccountCapability)AccountCapabilities;
}

public class MailThreadQueryResult
{
    public required string ThreadId { get; init; }
    public string? ExternalId { get; init; }
    public string? Subject { get; init; }
    public string? ParticipantsSummary { get; init; }
    public string? Preview { get; init; }
    public long? LatestMessageReceivedAt { get; init; }
    public int UnreadCount { get; init; }
    public int MessageCount { get; init; }
    public int HasAttachments { get; init; }
    public required string AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountType { get; init; }
    public required string MailboxId { get; init; }
    public required string MailboxName { get; init; }

    public bool ThreadHasAttachments => HasAttachments == 1;
    public AccountType AccountTypeEnum => Enum.TryParse<AccountType>(AccountType, ignoreCase: true, out var result) ? result : perinma.Models.AccountType.Google;
}

public class MailMessageQueryResult
{
    public required string MessageId { get; init; }
    public string? ExternalId { get; init; }
    public string? InternetMessageId { get; init; }
    public string? Subject { get; init; }
    public string? SenderName { get; init; }
    public string? SenderAddress { get; init; }
    public long? SentAt { get; init; }
    public long? ReceivedAt { get; init; }
    public string? Preview { get; init; }
    public string? PlainTextBody { get; init; }
    public string? HtmlBody { get; init; }
    public long? BodyFetchedAt { get; init; }
    public int HasHtmlBody { get; init; }
    public int HasPlainTextBody { get; init; }
    public int HasAttachments { get; init; }
    public int HasExternalResources { get; init; }
    public int HasBlockedContent { get; init; }
    public int IsUnread { get; init; }
    public int IsStarred { get; init; }
    public int IsAnswered { get; init; }
    public int IsDraft { get; init; }
    public long? ChangedAt { get; init; }
    public string? RawData { get; init; }
    public required string ThreadId { get; init; }
    public required string AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountType { get; init; }

    public bool MessageHasHtmlBody => HasHtmlBody == 1;
    public bool MessageHasPlainTextBody => HasPlainTextBody == 1;
    public bool MessageHasAttachments => HasAttachments == 1;
    public bool MessageHasExternalResources => HasExternalResources == 1;
    public bool MessageHasBlockedContent => HasBlockedContent == 1;
    public bool MessageIsUnread => IsUnread == 1;
    public bool MessageIsStarred => IsStarred == 1;
    public bool MessageIsAnswered => IsAnswered == 1;
    public bool MessageIsDraft => IsDraft == 1;
    public AccountType AccountTypeEnum => Enum.TryParse<AccountType>(AccountType, ignoreCase: true, out var result) ? result : perinma.Models.AccountType.Google;
}

public class MailComposeDraftDbo
{
    public required string DraftId { get; set; }
    public required string AccountId { get; set; }
    public string ComposeKind { get; set; } = MailComposeKind.New.ToString();
    public string? SourceMessageId { get; set; }
    public string? SourceMessageExternalId { get; set; }
    public string? SourceThreadId { get; set; }
    public string? SourceThreadExternalId { get; set; }
    public string? SourceInternetMessageId { get; set; }
    public string? RemoteDraftReferenceJson { get; set; }
    public string? SelectedIdentityId { get; set; }
    public string? SelectedIdentityDisplayName { get; set; }
    public string? SelectedIdentityAddress { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string PlainTextBody { get; set; } = string.Empty;
    public string Status { get; set; } = MailComposeDraftStatus.LocalOnly.ToString();
    public long? LastLocalSaveAt { get; set; }
    public long? LastRemoteSaveAt { get; set; }
    public long UpdatedAt { get; set; }

    public MailComposeKind ComposeKindEnum => Enum.TryParse<MailComposeKind>(ComposeKind, ignoreCase: true, out var result)
        ? result
        : MailComposeKind.New;

    public MailComposeDraftStatus StatusEnum => Enum.TryParse<MailComposeDraftStatus>(Status, ignoreCase: true, out var result)
        ? result
        : MailComposeDraftStatus.LocalOnly;
}

public class MailComposeRecipientDbo
{
    public required string DraftId { get; set; }
    public required string RecipientId { get; set; }
    public string RecipientKind { get; set; } = MailRecipientKind.To.ToString();
    public string? DisplayName { get; set; }
    public required string Address { get; set; }
    public int SortOrder { get; set; }

    public MailRecipientKind RecipientKindEnum => Enum.TryParse<MailRecipientKind>(RecipientKind, ignoreCase: true, out var result)
        ? result
        : MailRecipientKind.To;
}

public class MailComposeAttachmentDbo
{
    public required string DraftId { get; set; }
    public required string AttachmentId { get; set; }
    public required string FileName { get; set; }
    public required string MimeType { get; set; }
    public long Size { get; set; }
    public int IsInline { get; set; }
    public string? ContentId { get; set; }
    public required string StagedFilePath { get; set; }
    public string? ContentHash { get; set; }
    public string? ProviderAttachmentReferenceJson { get; set; }
    public int SortOrder { get; set; }

    public bool Inline => IsInline == 1;
}

public class MailComposeDraftQueryResult
{
    public required string DraftId { get; init; }
    public required string AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountType { get; init; }
    public required string ComposeKind { get; init; }
    public string? SourceMessageId { get; init; }
    public string? SourceMessageExternalId { get; init; }
    public string? SourceThreadId { get; init; }
    public string? SourceThreadExternalId { get; init; }
    public string? SourceInternetMessageId { get; init; }
    public string? RemoteDraftReferenceJson { get; init; }
    public string? SelectedIdentityId { get; init; }
    public string? SelectedIdentityDisplayName { get; init; }
    public string? SelectedIdentityAddress { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string HtmlBody { get; init; } = string.Empty;
    public string PlainTextBody { get; init; } = string.Empty;
    public required string Status { get; init; }
    public long? LastLocalSaveAt { get; init; }
    public long? LastRemoteSaveAt { get; init; }
    public long UpdatedAt { get; init; }

    public MailComposeKind ComposeKindEnum => Enum.TryParse<MailComposeKind>(ComposeKind, ignoreCase: true, out var result)
        ? result
        : MailComposeKind.New;

    public MailComposeDraftStatus StatusEnum => Enum.TryParse<MailComposeDraftStatus>(Status, ignoreCase: true, out var result)
        ? result
        : MailComposeDraftStatus.LocalOnly;

    public AccountType AccountTypeEnum => Enum.TryParse<AccountType>(AccountType, ignoreCase: true, out var result)
        ? result
        : perinma.Models.AccountType.Google;
}
