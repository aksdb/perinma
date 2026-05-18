using System;

namespace perinma.Models;

public class Calendar
{
    public required Account Account { get; set; }
    public required Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool Enabled { get; set; }
    public DateTime? LastSync { get; set; }
    public ModelExtensions Extensions { get; } = new();
}

public static class CalendarExtensions
{
    /// <summary>
    /// True when the user has read-only access to this calendar (e.g. a subscribed/shared
    /// calendar the user cannot write to). Events from read-only calendars do not block
    /// the organizer's time in free/busy calculations.
    /// Absent (default false) = owned/writable — the safe fallback.
    /// </summary>
    public static readonly ModelExtension<bool> IsReadOnly = new();
}
