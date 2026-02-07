using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using perinma.Models;

namespace perinma.Services;

/// <summary>
/// Interface for calendar providers (Google Calendar, CalDAV, etc.).
/// Provides a unified abstraction for syncing calendars and events from different sources.
/// </summary>
public interface ICalendarProvider
{
    /// <summary>
    /// Gets the credential manager service used by this provider.
    /// </summary>
    CredentialManagerService CredentialManager { get; }

    /// <summary>
    /// Syncs calendars for an account, optionally using incremental sync.
    /// </summary>
    /// <param name="accountId">Account ID to sync calendars for</param>
    /// <param name="syncToken">Optional sync token for incremental sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing calendars and new sync token</returns>
    Task<CalendarSyncResult> GetCalendarsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs events for a specific calendar, optionally using incremental sync.
    /// </summary>
    /// <param name="accountId">Account ID to sync events for</param>
    /// <param name="calendarExternalId">External ID of the calendar to sync</param>
    /// <param name="syncToken">Optional sync token for incremental sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing events and new sync token</returns>
    Task<EventSyncResult> GetEventsAsync(
        string accountId,
        string calendarExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests whether the connection to the provider is working with the given account.
    /// </summary>
    /// <param name="accountId">Account ID to test connection for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connection is successful, false otherwise</returns>
    Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts reminder trigger times from raw event data.
    /// </summary>
    /// <param name="rawEventData">Raw event data (JSON for Google, iCalendar for CalDAV)</param>
    /// <param name="rawCalendarData">Optional raw calendar data for default reminders (JSON for Google)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of reminder minutes before event start</returns>
    Task<IList<int>> GetReminderMinutesAsync(
        string rawEventData,
        string? rawCalendarData = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all reminder occurrences with their trigger times for an event.
    /// Includes future occurrences for recurring events, filtered by the reminder minutes.
    /// </summary>
    /// <param name="rawEventData">Raw event data (JSON for Google, iCalendar for CalDAV)</param>
    /// <param name="rawCalendarData">Optional raw calendar data for default reminders (JSON for Google)</param>
    /// <param name="referenceTime">Reference time for filtering (defaults to UTC now)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of tuples containing occurrence time and trigger time for each reminder</returns>
    Task<IList<(ZonedDateTime Occurrence, ZonedDateTime TriggerTime)>> GetNextReminderOccurrencesAsync(
        string rawEventData,
        string? rawCalendarData = null,
        ZonedDateTime referenceTime = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the event start time from raw event data, preserving timezone information.
    /// </summary>
    /// <param name="rawEventData">Raw event data (JSON for Google, iCalendar for CalDAV)</param>
    /// <param name="occurrenceTime">Optional occurrence time for recurring events. If provided, returns the start time for this specific occurrence.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Event start time with timezone information, or null if parsing fails</returns>
    Task<DateTimeOffset?> GetEventStartTimeAsync(
        string rawEventData,
        DateTime? occurrenceTime = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Responds to an event invitation with the specified status.
    /// </summary>
    /// <param name="accountId">Account ID to respond with</param>
    /// <param name="calendarId">External ID of the calendar</param>
    /// <param name="eventId">External ID of the event</param>
    /// <param name="rawEventData">Raw event data (JSON for Google, iCalendar for CalDAV)</param>
    /// <param name="responseStatus">The response status (e.g., "accepted", "declined", "tentative")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RespondToEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string rawEventData,
        string responseStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new event in the specified calendar.
    /// </summary>
    /// <param name="accountId">Account ID to create event for</param>
    /// <param name="calendarId">External ID of the calendar</param>
    /// <param name="title">Event title</param>
    /// <param name="description">Event description (optional)</param>
    /// <param name="location">Event location (optional)</param>
    /// <param name="startTime">Event start time with timezone</param>
    /// <param name="endTime">Event end time with timezone</param>
    /// <param name="rawEventData">Raw event data for context (e.g., for preserving provider-specific fields)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The external ID of the created event</returns>
    Task<string> CreateEventAsync(
        string accountId,
        string calendarId,
        string title,
        string? description,
        string? location,
        ZonedDateTime startTime,
        ZonedDateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing event.
    /// </summary>
    /// <param name="accountId">Account ID to update event for</param>
    /// <param name="calendarId">External ID of the calendar</param>
    /// <param name="eventId">External ID of the event to update</param>
    /// <param name="title">Event title</param>
    /// <param name="description">Event description (optional)</param>
    /// <param name="location">Event location (optional)</param>
    /// <param name="startTime">Event start time with timezone</param>
    /// <param name="endTime">Event end time with timezone</param>
    /// <param name="rawEventData">Raw event data for context (e.g., for preserving provider-specific fields)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string title,
        string? description,
        string? location,
        ZonedDateTime startTime,
        ZonedDateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of syncing calendars from a provider.
/// </summary>
public class CalendarSyncResult
{
    /// <summary>
    /// Calendars returned from the sync operation.
    /// May include deleted calendars with Deleted=true for incremental sync.
    /// </summary>
    public required IList<ProviderCalendar> Calendars { get; init; }

    /// <summary>
    /// Sync token to use for the next incremental sync.
    /// </summary>
    public string? SyncToken { get; init; }
}

/// <summary>
/// Result of syncing events from a provider.
/// </summary>
public class EventSyncResult
{
    /// <summary>
    /// Events returned from the sync operation.
    /// May include deleted/cancelled events for incremental sync.
    /// </summary>
    public required IList<ProviderEvent> Events { get; init; }

    /// <summary>
    /// Sync token to use for the next incremental sync.
    /// </summary>
    public string? SyncToken { get; init; }
}

/// <summary>
/// Provider-agnostic calendar representation for sync operations.
/// </summary>
public class ProviderCalendar
{
    /// <summary>
    /// External ID of the calendar (provider-specific identifier).
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// Display name of the calendar.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Calendar color as hex string (e.g., "#9fc6e7").
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Whether the calendar is selected/enabled by default.
    /// </summary>
    public bool Selected { get; init; }

    /// <summary>
    /// Whether the calendar has been deleted (for incremental sync).
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// Provider specific data.
    /// </summary>
    public Dictionary<string, DataAttribute> Data { get; init; } = new();
}

public abstract record DataAttribute
{
    public record Text(string value) : DataAttribute;
    public record JsonText(string value) : DataAttribute;
}

/// <summary>
/// Provider-agnostic event representation for sync operations.
/// </summary>
public class ProviderEvent
{
    /// <summary>
    /// External ID of the event (provider-specific identifier).
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// Event title/summary.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Start time of the event (or first occurrence for recurring events).
    /// </summary>
    public ZonedDateTime? StartTime { get; init; }

    /// <summary>
    /// End time of the event. For recurring events, this is the end of the recurrence span.
    /// </summary>
    public ZonedDateTime? EndTime { get; init; }

    /// <summary>
    /// Event status (e.g., "confirmed", "cancelled", "tentative").
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Whether this event is deleted/cancelled (for incremental sync).
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// For override events: the ID of the parent recurring event.
    /// </summary>
    public string? RecurringEventId { get; init; }

    /// <summary>
    /// For override events: the original start time of the occurrence being modified.
    /// </summary>
    public ZonedDateTime? OriginalStartTime { get; init; }

    /// <summary>
    /// Raw provider data serialized as string for later use (JSON or iCalendar).
    /// </summary>
    public string? RawData { get; init; }
}
