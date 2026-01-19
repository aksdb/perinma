using System;

namespace perinma.Models;

/// <summary>
/// Represents the user's response status to an event invitation.
/// </summary>
public enum EventResponseStatus
{
    /// <summary>No response status available (not an invitation or unknown).</summary>
    None,
    /// <summary>User has not responded to the invitation yet.</summary>
    NeedsAction,
    /// <summary>User has declined the invitation.</summary>
    Declined,
    /// <summary>User has tentatively accepted the invitation.</summary>
    Tentative,
    /// <summary>User has accepted the invitation.</summary>
    Accepted
}

public class CalendarEvent
{
    public required Calendar Calendar { get; set; }
    public required Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Title { get; set; }
    public DateTime? ChangedAt { get; set; }
    
    /// <summary>
    /// The user's response status to this event invitation.
    /// </summary>
    public EventResponseStatus ResponseStatus { get; set; } = EventResponseStatus.None;
}
