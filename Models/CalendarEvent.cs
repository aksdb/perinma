using System;

namespace perinma.Models;

public class CalendarEvent
{
    public required Calendar Calendar { get; set; }
    public required Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Title { get; set; }
    public DateTime? ChangedAt { get; set; }
}
