using System;

namespace perinma.Services.CalDAV;

public class CalDavCalendar
{
    public required string Url { get; init; }
    public required string DisplayName { get; init; }
    public string? Color { get; init; }
    public string? CTag { get; init; }
    public bool Deleted { get; init; }
}

public class CalDavEvent
{
    public required string Uid { get; init; }
    public required string Url { get; init; }
    public string? Summary { get; init; }
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public string? Status { get; init; }
    public string? ETag { get; init; }
    public string? RawICalendar { get; init; }
    public bool Deleted { get; init; }
}
