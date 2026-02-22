using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using perinma.Storage.Models;

namespace perinma.Services.CalDAV;

public interface ICalDavService
{
    Task<CalendarSyncResult> GetCalendarsAsync(
        CalDavCredentials credentials,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    Task<EventSyncResult> GetEventsAsync(
        CalDavCredentials credentials,
        string calendarUrl,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    Task<bool> TestConnectionAsync(
        CalDavCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the user's response status for an event invitation
    /// </summary>
    /// <param name="credentials">CalDAV credentials</param>
    /// <param name="eventUrl">URL of the event to update</param>
    /// <param name="rawICalendar">Current iCalendar data</param>
    /// <param name="responseStatus">The response status (ACCEPTED, DECLINED, TENTATIVE)</param>
    /// <param name="userEmail">The user's email to identify their attendee entry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated iCalendar data</returns>
    Task<string> RespondToEventAsync(
        CalDavCredentials credentials,
        string eventUrl,
        string rawICalendar,
        string responseStatus,
        string userEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new event in specified calendar.
    /// </summary>
    /// <param name="credentials">CalDAV credentials</param>
    /// <param name="calendarUrl">Calendar URL to create event in</param>
    /// <param name="calendar">Calendar containing the event to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The external ID (URL) of created event</returns>
    Task<string> CreateEventAsync(
        CalDavCredentials credentials,
        string calendarUrl,
        Ical.Net.Calendar calendar,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing calendar.
    /// </summary>
    /// <param name="credentials">CalDAV credentials</param>
    /// <param name="eventUrl">URL of calendar to update</param>
    /// <param name="calendar">Updated calendar data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<string> UpdateEventAsync(
        CalDavCredentials credentials,
        string eventUrl,
        Ical.Net.Calendar calendar,
        CancellationToken cancellationToken = default);

    public class CalendarSyncResult
    {
        public required IList<CalDavCalendar> Calendars { get; init; }
        public string? SyncToken { get; init; }
    }

    public class EventSyncResult
    {
        public required IList<CalDavEvent> Events { get; init; }
        public string? SyncToken { get; init; }
    }
}