using System.Collections.Generic;

namespace perinma.Messaging;

/// <summary>
/// Message sent when sync starts
/// </summary>
public class SyncStartedMessage
{
}

/// <summary>
/// Message sent when syncing an account
/// </summary>
public class SyncAccountProgressMessage
{
    public required string AccountName { get; init; }
    public required int AccountIndex { get; init; }
    public required int TotalAccounts { get; init; }
    public double ProgressPercentage => TotalAccounts > 0 ? (double)AccountIndex / TotalAccounts * 100 : 0;
}

/// <summary>
/// Message sent when syncing a calendar
/// </summary>
public class SyncCalendarProgressMessage
{
    public required string CalendarName { get; init; }
    public required int CalendarIndex { get; init; }
    public required int TotalCalendars { get; init; }
}

/// <summary>
/// Message sent when syncing events for a calendar
/// </summary>
public class SyncEventsProgressMessage
{
    public required string CalendarName { get; init; }
    public required int EventCount { get; init; }
}

/// <summary>
/// Message sent when sync completes successfully
/// </summary>
public class SyncCompletedMessage
{
    public required int SyncedAccounts { get; init; }
}

/// <summary>
/// Message sent when sync fails or has errors
/// </summary>
public class SyncFailedMessage
{
    public required List<string> Errors { get; init; }
    public required int FailedAccounts { get; init; }
}

/// <summary>
/// Message sent when a sync operation completes (success or failure)
/// </summary>
public class SyncEndedMessage
{
}

/// <summary>
/// Message sent when an account requires re-authentication
/// </summary>
public sealed record ReAuthenticationRequiredMessage(string AccountId, string ProviderType);

/// <summary>
/// Message sent when contact sync starts
/// </summary>
public class ContactSyncStartedMessage
{
}

/// <summary>
/// Message sent when syncing an address book
/// </summary>
public class SyncAddressBookProgressMessage
{
    public required string AddressBookName { get; init; }
    public required int AddressBookIndex { get; init; }
    public required int TotalAddressBooks { get; init; }
}

/// <summary>
/// Message sent when syncing contacts for an address book
/// </summary>
public class SyncContactsProgressMessage
{
    public required string AddressBookName { get; init; }
    public required int ContactCount { get; init; }
}

/// <summary>
/// Message sent when processing individual contacts for an address book
/// </summary>
public class SyncContactProcessingProgressMessage
{
    public required string AddressBookName { get; init; }
    public required int ContactIndex { get; init; }
    public required int TotalContacts { get; init; }
    public double ProgressPercentage => TotalContacts > 0 ? (double)ContactIndex / TotalContacts * 100 : 0;
}

/// <summary>
/// Message sent when contact sync completes
/// </summary>
public class ContactSyncEndedMessage
{
}

/// <summary>
/// Message sent when mail sync starts
/// </summary>
public class MailSyncStartedMessage
{
}

/// <summary>
/// Message sent when syncing a mail account
/// </summary>
public class SyncMailAccountProgressMessage
{
    public required string AccountName { get; init; }
    public required int AccountIndex { get; init; }
    public required int TotalAccounts { get; init; }
    public double ProgressPercentage => TotalAccounts > 0 ? (double)AccountIndex / TotalAccounts * 100 : 0;
}

/// <summary>
/// Message sent when syncing a mailbox
/// </summary>
public class SyncMailboxProgressMessage
{
    public required string MailboxName { get; init; }
    public required int MailboxIndex { get; init; }
    public required int TotalMailboxes { get; init; }
}

/// <summary>
/// Message sent when processing individual messages for a mailbox
/// </summary>
public class SyncMailMessageProcessingProgressMessage
{
    public required string MailboxName { get; init; }
    public required int MessageIndex { get; init; }
    public required int TotalMessages { get; init; }
    public double ProgressPercentage => TotalMessages > 0 ? (double)MessageIndex / TotalMessages * 100 : 0;
}

/// <summary>
/// Message sent when message sync completes for a mailbox
/// </summary>
public class SyncMailMessagesProgressMessage
{
    public required string MailboxName { get; init; }
    public required int MessageCount { get; init; }
}

/// <summary>
/// Message sent when mail sync completes
/// </summary>
public class MailSyncEndedMessage
{
}

/// <summary>
/// Message sent when calendar events are changed (created, updated, or deleted)
/// </summary>
public class EventsChangedMessage
{
}

/// <summary>
/// Message sent when accounts are changed (added or deleted)
/// </summary>
public class AccountsChangedMessage
{
}

/// <summary>
/// Message sent when working days or hours settings are changed
/// </summary>
public class WorkingDaysChangedMessage
{
}
