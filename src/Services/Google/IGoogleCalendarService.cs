using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using perinma.Storage.Models;

namespace perinma.Services.Google;

/// <summary>
/// Interface for Google Calendar API operations
/// </summary>
public interface IGoogleCalendarService
{
    /// <summary>
    /// Creates a CalendarService from GoogleCredentials
    /// </summary>
    Task<CalendarService> CreateServiceAsync(GoogleCredentials credentials, CancellationToken cancellationToken = default, string? accountId = null);

    /// <summary>
    /// Fetches calendars for the authenticated user, optionally using incremental sync
    /// </summary>
    Task<GoogleCalendarService.CalendarSyncResult> GetCalendarsAsync(
        CalendarService service,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches events for a specific calendar, optionally using incremental sync
    /// </summary>
    Task<GoogleCalendarService.EventSyncResult> GetEventsAsync(
        CalendarService service,
        string calendarId,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a calendar's selected (enabled/disabled) state in Google Calendar
    /// </summary>
    Task UpdateCalendarSelectedAsync(
        CalendarService service,
        string calendarId,
        bool selected,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges authorization code for access and refresh tokens
    /// </summary>
    Task ExchangeAuthorizationCodeAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken,
        string? redirectUri = null);

    /// <summary>
    /// Updates user's response status for an event invitation
    /// </summary>
    /// <param name="service">Authenticated CalendarService</param>
    /// <param name="calendarId">Calendar ID containing event</param>
    /// <param name="eventId">Event ID to respond to</param>
    /// <param name="responseStatus">The response status (accepted, declined, tentative)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RespondToEventAsync(
        CalendarService service,
        string calendarId,
        string eventId,
        string responseStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new event in the specified calendar.
    /// </summary>
    /// <param name="service">Authenticated CalendarService</param>
    /// <param name="calendarId">Calendar ID to create event in</param>
    /// <param name="event">Event data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The external ID of the created event</returns>
    Task<string> CreateEventAsync(
        CalendarService service,
        string calendarId,
        Event @event,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing event.
    /// </summary>
    /// <param name="service">Authenticated CalendarService</param>
    /// <param name="calendarId">Calendar ID containing event</param>
    /// <param name="eventId">Event ID to update</param>
    /// <param name="event">Event data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateEventAsync(
        CalendarService service,
        string calendarId,
        string eventId,
        Event @event,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an existing event.
    /// </summary>
    /// <param name="service">Authenticated CalendarService</param>
    /// <param name="calendarId">Calendar ID containing event</param>
    /// <param name="eventId">Event ID to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteEventAsync(
        CalendarService service,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken = default);
}
