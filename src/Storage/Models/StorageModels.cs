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
    public required string CalendarId { get; init; }
    public string? CalendarExternalId { get; init; }
    public required string CalendarName { get; init; }
    public string? CalendarColor { get; init; }
    public int CalendarEnabled { get; init; }
    public long? CalendarLastSync { get; init; }
    public required string AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountType { get; init; }
}
