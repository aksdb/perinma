using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage.Models;

namespace perinma.Tests.Fakes;

public class FakeGoogleCalendarService : IGoogleCalendarService
{
    private readonly List<CalendarListEntry> _calendars = new();
    private readonly Dictionary<string, List<Event>> _calendarEvents = new();
    private string? _syncToken;
    private readonly Dictionary<string, string?> _eventSyncTokens = new();
    private bool _shouldThrowInvalidSyncToken;
    private bool _shouldThrowInvalidEventSyncToken;

    public void SetCalendars(params CalendarListEntry[] calendars)
    {
        _calendars.Clear();
        _calendars.AddRange(calendars);
    }

    public void SetEvents(string calendarId, params Event[] events)
    {
        if (!_calendarEvents.ContainsKey(calendarId))
        {
            _calendarEvents[calendarId] = new List<Event>();
        }
        _calendarEvents[calendarId].Clear();
        _calendarEvents[calendarId].AddRange(events);
    }

    public void SetInvalidSyncTokenBehavior(bool shouldThrow)
    {
        _shouldThrowInvalidSyncToken = shouldThrow;
    }

    public void SetInvalidEventSyncTokenBehavior(bool shouldThrow)
    {
        _shouldThrowInvalidEventSyncToken = shouldThrow;
    }

    public Task<CalendarService> CreateServiceAsync(GoogleCredentials credentials, CancellationToken cancellationToken = default, string? accountId = null)
    {
        // Return a null CalendarService - we won't actually use it in tests
        // In a real scenario, you might want to create a proper mock CalendarService
        return Task.FromResult<CalendarService>(null!);
    }

    public Task<GoogleCalendarService.CalendarSyncResult> GetCalendarsAsync(
        CalendarService service,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        // Simulate invalid sync token error
        if (_shouldThrowInvalidSyncToken && !string.IsNullOrEmpty(syncToken))
        {
            throw new InvalidOperationException("Sync token is invalid or expired (410)");
        }

        // Generate a new sync token for next request
        _syncToken = Guid.NewGuid().ToString();

        var result = new GoogleCalendarService.CalendarSyncResult
        {
            Calendars = _calendars,
            SyncToken = _syncToken
        };

        return Task.FromResult(result);
    }

    public Task<GoogleCalendarService.EventSyncResult> GetEventsAsync(
        CalendarService service,
        string calendarId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        // Simulate invalid sync token error
        if (_shouldThrowInvalidEventSyncToken && !string.IsNullOrEmpty(syncToken))
        {
            throw new InvalidOperationException("Event sync token is invalid or expired (410)");
        }

        // Get events for this calendar
        var events = _calendarEvents.ContainsKey(calendarId)
            ? _calendarEvents[calendarId]
            : new List<Event>();

        // Generate a new sync token for next request
        var newSyncToken = Guid.NewGuid().ToString();
        _eventSyncTokens[calendarId] = newSyncToken;

        var result = new GoogleCalendarService.EventSyncResult
        {
            Events = events,
            SyncToken = newSyncToken
        };

        return Task.FromResult(result);
    }

    public Task UpdateCalendarSelectedAsync(
        CalendarService service,
        string calendarId,
        bool selected,
        CancellationToken cancellationToken = default)
    {
        // Update the selected state of the calendar in the fake list
        var calendar = _calendars.Find(c => c.Id == calendarId);
        if (calendar != null)
        {
            calendar.Selected = selected;
        }

        return Task.CompletedTask;
    }

    public Task ExchangeAuthorizationCodeAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken,
        string? redirectUri = null)
    {
        // Simulate successful token exchange
        credentials.AccessToken = "fake_access_token";
        credentials.RefreshToken = "fake_refresh_token";
        credentials.ExpiresAt = DateTime.UtcNow.AddHours(1);
        credentials.TokenType = "Bearer";
        credentials.AuthorizationCode = null;

        return Task.CompletedTask;
    }

    public Task RespondToEventAsync(
        CalendarService service,
        string calendarId,
        string eventId,
        string responseStatus,
        CancellationToken cancellationToken = default)
    {
        // For testing, just return completed task
        return Task.CompletedTask;
    }

    public Task<string> CreateEventAsync(
        CalendarService service,
        string calendarId,
        string title,
        string? description,
        string? location,
        DateTime startTime,
        DateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task UpdateEventAsync(
        CalendarService service,
        string calendarId,
        string eventId,
        string title,
        string? description,
        string? location,
        DateTime startTime,
        DateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public static CalendarListEntry CreateCalendar(string id, string summary, bool selected = true, string? color = null)
    {
        return new CalendarListEntry
        {
            Id = id,
            Summary = summary,
            Selected = selected,
            BackgroundColor = color ?? "#9fc6e7",
            Deleted = false
        };
    }

    public static CalendarListEntry CreateDeletedCalendar(string id)
    {
        return new CalendarListEntry
        {
            Id = id,
            Summary = "Deleted Calendar",
            Deleted = true
        };
    }

    public static Event CreateEvent(string id, string summary, DateTime start, DateTime end)
    {
        return new Event
        {
            Id = id,
            Summary = summary,
            Status = "confirmed",
            Start = new EventDateTime { DateTimeRaw = start.ToString("o") },
            End = new EventDateTime { DateTimeRaw = end.ToString("o") }
        };
    }

    public static Event CreateCancelledEvent(string id)
    {
        return new Event
        {
            Id = id,
            Summary = "Cancelled Event",
            Status = "cancelled"
        };
    }

    /// <summary>
    /// Creates a recurring event with RRULE.
    /// </summary>
    /// <param name="id">Event ID</param>
    /// <param name="summary">Event summary/title</param>
    /// <param name="start">Start time of the first occurrence</param>
    /// <param name="end">End time of the first occurrence</param>
    /// <param name="recurrence">List of recurrence rules (e.g., "RRULE:FREQ=WEEKLY;COUNT=10")</param>
    public static Event CreateRecurringEvent(string id, string summary, DateTime start, DateTime end, params string[] recurrence)
    {
        return new Event
        {
            Id = id,
            Summary = summary,
            Status = "confirmed",
            Start = new EventDateTime { DateTimeRaw = start.ToString("o") },
            End = new EventDateTime { DateTimeRaw = end.ToString("o") },
            Recurrence = recurrence.Length > 0 ? new List<string>(recurrence) : null
        };
    }

    /// <summary>
    /// Creates a recurring event with timezone-aware times.
    /// </summary>
    public static Event CreateRecurringEventWithTimezone(
        string id,
        string summary,
        DateTime start,
        DateTime end,
        string timeZone,
        params string[] recurrence)
    {
        return new Event
        {
            Id = id,
            Summary = summary,
            Status = "confirmed",
            Start = new EventDateTime
            {
                DateTimeRaw = start.ToString("o"),
                TimeZone = timeZone
            },
            End = new EventDateTime
            {
                DateTimeRaw = end.ToString("o"),
                TimeZone = timeZone
            },
            Recurrence = recurrence.Length > 0 ? new List<string>(recurrence) : null
        };
    }

    /// <summary>
    /// Creates a modified override event for a recurring event.
    /// </summary>
    public static Event CreateModifiedOverride(
        string id,
        string recurringEventId,
        string summary,
        DateTime originalStartTime,
        DateTime newStart,
        DateTime newEnd)
    {
        return new Event
        {
            Id = id,
            RecurringEventId = recurringEventId,
            Summary = summary,
            Status = "confirmed",
            OriginalStartTime = new EventDateTime { DateTimeRaw = originalStartTime.ToString("o") },
            Start = new EventDateTime { DateTimeRaw = newStart.ToString("o") },
            End = new EventDateTime { DateTimeRaw = newEnd.ToString("o") }
        };
    }

    /// <summary>
    /// Creates a cancelled override event for a recurring event.
    /// </summary>
    public static Event CreateCancelledOverride(
        string id,
        string recurringEventId,
        DateTime originalStartTime)
    {
        return new Event
        {
            Id = id,
            RecurringEventId = recurringEventId,
            Summary = "Cancelled Event",
            Status = "cancelled",
            OriginalStartTime = new EventDateTime { DateTimeRaw = originalStartTime.ToString("o") }
        };
    }
}
