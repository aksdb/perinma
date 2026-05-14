using System.Collections.Generic;
using NodaTime;

namespace perinma.Services;

/// <summary>
/// Resolution status for a free/busy query against a single attendee.
/// </summary>
public enum FreeBusyStatus
{
    /// <summary>Busy slots were retrieved successfully (list may still be empty).</summary>
    Ok,

    /// <summary>
    /// The provider authenticated but could not retrieve data for this attendee
    /// (e.g. calendar not shared, scheduling not supported by the server).
    /// </summary>
    Unavailable,

    /// <summary>
    /// The attendee email could not be resolved by this provider at all
    /// (e.g. not a known user, API error).
    /// </summary>
    Unknown,
}

/// <summary>
/// A contiguous blocked interval for a single attendee.
/// Both endpoints are in UTC.
/// </summary>
public record TimeSlot(Instant Start, Instant End);

/// <summary>
/// Free/busy information for a single attendee, as returned by <see cref="ICalendarProvider.GetFreeBusyAsync"/>.
/// </summary>
public record AttendeeFreeBusy
{
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public FreeBusyStatus Status { get; init; } = FreeBusyStatus.Ok;

    /// <summary>
    /// Ordered list of busy intervals within the queried range.
    /// Empty when <see cref="Status"/> is not <see cref="FreeBusyStatus.Ok"/>.
    /// </summary>
    public IReadOnlyList<TimeSlot> BusySlots { get; init; } = [];
}

/// <summary>
/// A single calendar event belonging to the organizer, sourced from the local SQLite cache.
/// Used to show rich own-availability in the timeline (color, title) without an API round-trip.
/// </summary>
public record OwnCalendarEvent
{
    public required string Title { get; init; }
    public required Instant Start { get; init; }
    public required Instant End { get; init; }
    /// <summary>Hex color string (e.g. "#4CAF50") of the originating calendar, or null.</summary>
    public string? CalendarColor { get; init; }
    public string? CalendarName { get; init; }
}
