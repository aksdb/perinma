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
