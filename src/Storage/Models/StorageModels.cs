using System;

namespace perinma.Storage.Models;

public class AccountDbo
{
    public required string AccountId { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
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
    public byte[]? Data { get; set; }
}

public class CalendarEventDbo
{
    public required string calendar_id { get; set; }
    public required string event_id { get; set; }
    public string? external_id { get; set; }
    public long? start_time { get; set; }
    public long? end_time { get; set; }
    public string? title { get; set; }
    public long? changed_at { get; set; }
    public byte[]? data { get; set; }
}

public class CalendarEventRelationDbo
{
    public required string parent_event_id { get; set; }
    public required string child_event_id { get; set; }
}

public class CalendarEventRelationBacklogDbo
{
    public required string calendar_id { get; set; }
    public required string parent_external_id { get; set; }
    public required string child_external_id { get; set; }
}

public class ReminderDbo
{
    public required string reminder_id { get; set; }
    public int target_type { get; set; }
    public required string target_id { get; set; }
    public long target_time { get; set; }
    public long trigger_time { get; set; }
}

/// <summary>
/// Sync data stored in calendar.data field as JSON
/// </summary>
public class CalendarSyncData
{
    /// <summary>
    /// Sync token from Google Calendar API for incremental sync
    /// </summary>
    public string? SyncToken { get; set; }

    /// <summary>
    /// Page token for paginated results
    /// </summary>
    public string? PageToken { get; set; }
}
