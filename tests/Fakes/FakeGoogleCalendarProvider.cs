using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Calendar.v3.Data;
using perinma.Services;
using perinma.Storage.Models;

namespace perinma.Tests.Fakes;

/// <summary>
/// Fake implementation of ICalendarProvider for testing Google Calendar sync.
/// </summary>
public class FakeGoogleCalendarProvider : ICalendarProvider
{
    private readonly List<CalendarListEntry> _calendars = [];
    private readonly Dictionary<string, List<Event>> _calendarEvents = new();
    private string? _syncToken;
    private readonly Dictionary<string, string?> _eventSyncTokens = new();
    private bool _shouldThrowInvalidSyncToken;
    private bool _shouldThrowInvalidEventSyncToken;
    private readonly CredentialManagerService _credentialManager;

    public FakeGoogleCalendarProvider(CredentialManagerService credentialManager)
    {
        _credentialManager = credentialManager;
    }

    /// <inheritdoc/>
    public CredentialManagerService CredentialManager => _credentialManager;

    public void SetCalendars(params CalendarListEntry[] calendars)
    {
        _calendars.Clear();
        _calendars.AddRange(calendars);
    }

    public void SetEvents(string calendarId, params Event[] events)
    {
        if (!_calendarEvents.ContainsKey(calendarId))
        {
            _calendarEvents[calendarId] = [];
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

    public Task<CalendarSyncResult> GetCalendarsAsync(
        string accountId,
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

        // Convert to provider-agnostic format
        var calendars = _calendars.Select(c => new ProviderCalendar
        {
            ExternalId = c.Id,
            Name = c.Summary ?? "Unnamed Calendar",
            Color = c.BackgroundColor,
            Selected = c.Selected == true,
            Deleted = c.Deleted == true
        }).ToList();

        var result = new CalendarSyncResult
        {
            Calendars = calendars,
            SyncToken = _syncToken
        };

        return Task.FromResult(result);
    }

    public Task<EventSyncResult> GetEventsAsync(
        string accountId,
        string calendarExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        // Simulate invalid sync token error
        if (_shouldThrowInvalidEventSyncToken && !string.IsNullOrEmpty(syncToken))
        {
            throw new InvalidOperationException("Event sync token is invalid or expired (410)");
        }

        // Get events for this calendar
        var googleEvents = _calendarEvents.TryGetValue(calendarExternalId, out var events)
            ? events
            : [];

        // Generate a new sync token for next request
        var newSyncToken = Guid.NewGuid().ToString();
        _eventSyncTokens[calendarExternalId] = newSyncToken;

        // Convert to provider-agnostic format
        var providerEvents = googleEvents.Select(ConvertGoogleEvent).Where(e => e != null).Cast<ProviderEvent>().ToList();

        var result = new EventSyncResult
        {
            Events = providerEvents,
            SyncToken = newSyncToken
        };

        return Task.FromResult(result);
    }

    public Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<IList<int>> GetReminderMinutesAsync(
        string rawEventData,
        string? rawCalendarData = null,
        CancellationToken cancellationToken = default)
    {
        // Return empty list by default - tests can override if needed
        return Task.FromResult<IList<int>>([]);
    }

    public Task<DateTimeOffset?> GetEventStartTimeAsync(
        string rawEventData,
        DateTime? occurrenceTime = null,
        CancellationToken cancellationToken = default)
    {
        // Return null by default - tests can override if needed
        return Task.FromResult<DateTimeOffset?>(null);
    }

    public Task<IList<(DateTime Occurrence, DateTime TriggerTime)>> GetNextReminderOccurrencesAsync(
        string rawEventData,
        string? rawCalendarData = null,
        DateTime referenceTime = default,
        CancellationToken cancellationToken = default)
    {
        // Return empty list by default - tests can override if needed
        return Task.FromResult<IList<(DateTime Occurrence, DateTime TriggerTime)>>([]);
    }

    public Task RespondToEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string rawEventData,
        string responseStatus,
        CancellationToken cancellationToken = default)
    {
        // For testing, just return completed task
        return Task.CompletedTask;
    }

    private static ProviderEvent? ConvertGoogleEvent(Event evt)
    {
        var isOverride = !string.IsNullOrEmpty(evt.RecurringEventId);

        // For non-override cancelled events, mark as deleted
        if (!isOverride && evt.Status == "cancelled")
        {
            return new ProviderEvent
            {
                ExternalId = evt.Id,
                Title = evt.Summary,
                Status = evt.Status,
                Deleted = true,
                RawData = null
            };
        }

        DateTime? startTime = null;
        DateTime? endTime = null;
        DateTime? originalStartTime = null;

        // Handle override events
        if (isOverride)
        {
            originalStartTime = ParseGoogleDateTime(evt.OriginalStartTime);

            if (evt.Status == "cancelled")
            {
                startTime = originalStartTime;
                endTime = originalStartTime;
            }
            else
            {
                startTime = ParseGoogleDateTime(evt.Start);
                endTime = ParseGoogleDateTime(evt.End);
            }
        }
        else
        {
            if (evt.Start == null || evt.End == null)
            {
                return null;
            }

            startTime = ParseGoogleDateTime(evt.Start);
            endTime = ParseGoogleDateTime(evt.End);
        }

        return new ProviderEvent
        {
            ExternalId = evt.Id,
            Title = evt.Summary ?? "Untitled Event",
            StartTime = startTime,
            EndTime = endTime,
            Status = evt.Status,
            Deleted = false,
            RecurringEventId = evt.RecurringEventId,
            OriginalStartTime = originalStartTime,
            RawData = null
        };
    }

    private static DateTime? ParseGoogleDateTime(EventDateTime? eventDateTime)
    {
        if (eventDateTime == null)
            return null;

        if (eventDateTime.DateTimeRaw != null && DateTime.TryParse(eventDateTime.DateTimeRaw, out var dateTime))
        {
            return dateTime;
        }

        if (eventDateTime.Date != null && DateTime.TryParse(eventDateTime.Date, out var date))
        {
            return date;
        }

        return null;
    }

    // Helper methods to create test data (same as FakeGoogleCalendarService)
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
