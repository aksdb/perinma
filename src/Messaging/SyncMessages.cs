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
