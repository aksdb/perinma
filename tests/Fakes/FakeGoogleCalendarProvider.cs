using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using Ical.Net.DataTypes;
using perinma.Models;
using perinma.Services;
using perinma.Storage.Models;
using Calendar = Ical.Net.Calendar;
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;

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
    private readonly List<(string AccountId, string CalendarId, string Title, string? Description, string? Location, DateTime StartTime, DateTime EndTime)> _createdEvents = [];

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

    public IList<int> GetReminderMinutes(
        string rawEventData,
        string? rawCalendarData = null)
    {
        return [];
    }

    public ZonedDateTime? GetEventStartTime(
        string rawEventData,
        DateTime? occurrenceTime = null)
    {
        return null;
    }

    public IList<(ZonedDateTime Occurrence, ZonedDateTime TriggerTime)> GetNextReminderOccurrences(
        string rawEventData,
        string? rawCalendarData = null,
        ZonedDateTime referenceTime = default)
    {
        return [];
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

    public Task<string> CreateEventAsync(
        string accountId,
        string calendarId,
        string title,
        string? description,
        string? location,
        ZonedDateTime startTime,
        ZonedDateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        var eventId = Guid.NewGuid().ToString();
        _createdEvents.Add((accountId, calendarId, title, description, location, startTime.DateTime, endTime.DateTime));
        return Task.FromResult(eventId);
    }

    public Task UpdateEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string title,
        string? description,
        string? location,
        ZonedDateTime startTime,
        ZonedDateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        _createdEvents.Add((accountId, calendarId, title, description, location, startTime.DateTime, endTime.DateTime));
        return Task.CompletedTask;
    }

    public List<CalendarEvent> ParseCalendarEvents(List<RawEvent> rawEvents, TimeRange timeRange)
    {
        var googleEvents = rawEvents
            .Select(e => (e.Reference, Event: NewtonsoftJsonSerializer.Instance.Deserialize<Event>(e.RawData)))
            .Where(t => t.Event != null)
            .ToList();

        var overrides = googleEvents
            .Where(t => !string.IsNullOrEmpty(t.Event.RecurringEventId))
            .ToList();

        return googleEvents
            .Where(t => string.IsNullOrEmpty(t.Event.RecurringEventId)) // Main events (regular or master recurring)
            .SelectMany(t =>
            {
                if (t.Event.Recurrence is { Count: > 0 })
                {
                    var foo = DetermineOccurrences(t.Event, timeRange);
                    // Generate occurrences for recurring events
                    return foo
                        .Where(occurrenceStart => !overrides.Any(ov =>
                            ov.Event.RecurringEventId == t.Event.Id &&
                            ParseGoogleDateTime(ov.Event.OriginalStartTime) == occurrenceStart))
                        .Select(occurrenceStart => MapToCalendarEvent(t.Reference, t.Event, occurrenceStart));
                }

                // Regular non-recurring event
                return [MapToCalendarEvent(t.Reference, t.Event, null)];
            })
            .Concat(overrides.Select(ov => MapToCalendarEvent(ov.Reference, ov.Event, null))) // Include the overrides themselves
            .Where(ce => ce.StartTime.ToUtc().DateTime <= timeRange.End.ToUtc().DateTime && ce.EndTime.ToUtc().DateTime >= timeRange.Start.ToUtc().DateTime)
            .ToList();
    }

    private static CalendarEvent MapToCalendarEvent(EventReference reference, Event googleEvent,
        ZonedDateTime? occurrenceStart)
    {
        var start = occurrenceStart ?? ParseGoogleDateTime(googleEvent.Start) ?? default;

        // Calculate duration if it's an occurrence
        var duration = TimeSpan.Zero;
        if (googleEvent is { Start: not null, End: not null })
            duration = googleEvent.End.DateTimeDateTimeOffset - googleEvent.Start.DateTimeDateTimeOffset ?? TimeSpan.Zero;
        var end = start.Add(duration);

        var relevantStatus = googleEvent.Attendees
            ?.FirstOrDefault(a => a.Self == true)
            ?.ResponseStatus;

        return new CalendarEvent
        {
            Reference = reference,
            Title = googleEvent.Summary,
            StartTime = start,
            EndTime = end,
            ChangedAt = googleEvent.UpdatedDateTimeOffset?.DateTime,
            ResponseStatus = MapResponseStatus(relevantStatus),
            Extensions = new ExtensionValues()
        };
    }

    private static EventResponseStatus MapResponseStatus(string? status) => status?.ToLower() switch
    {
        "needsaction" => EventResponseStatus.NeedsAction,
        "declined" => EventResponseStatus.Declined,
        "tentative" => EventResponseStatus.Tentative,
        "accepted" => EventResponseStatus.Accepted,
        _ => EventResponseStatus.None
    };

    private static List<ZonedDateTime> DetermineOccurrences(GoogleEvent evt, TimeRange timeRange,
        int max = Int32.MaxValue)
    {
        if (evt.Recurrence == null || evt.Recurrence.Count == 0)
            return [];

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("BEGIN:VEVENT");
        sb.AppendLine($"DTSTART;TZID={evt.Start.TimeZone}:{evt.Start.DateTimeDateTimeOffset:yyyyMMdd'T'HHmmss}");

        foreach (var r in evt.Recurrence)
            sb.AppendLine(r);

        sb.AppendLine("END:VEVENT");
        sb.Append("END:VCALENDAR");

        var calendar = Calendar.Load(sb.ToString());
        var icalEvent = calendar?.Events.FirstOrDefault();

        if (icalEvent == null)
            throw new InvalidOperationException("failed to parse recurrence");

        var occurrences = icalEvent.GetOccurrences(
            new CalDateTime(timeRange.Start.DateTime, timeRange.Start.TimeZone.Id));

        return occurrences
            .Select(o =>
                new ZonedDateTime(o.Period.StartTime.AsUtc, TimeZoneInfo.Utc).ConvertTo(timeRange.Start.TimeZone))
            .TakeWhile(z => z.DateTime <= timeRange.End.DateTime)
            .Take(max)
            .ToList();
    }

    public IReadOnlyList<(string AccountId, string CalendarId, string Title, string? Description, string? Location, DateTime StartTime, DateTime EndTime)> GetCreatedEvents()
    {
        return _createdEvents.AsReadOnly();
    }

    public void ClearCreatedEvents()
    {
        _createdEvents.Clear();
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

        ZonedDateTime? startTime = null;
        ZonedDateTime? endTime = null;
        ZonedDateTime? originalStartTime = null;

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

    private static ZonedDateTime? ParseGoogleDateTime(EventDateTime? eventDateTime)
    {
        if (eventDateTime == null)
            return null;

        if (eventDateTime.DateTimeRaw != null && DateTime.TryParse(eventDateTime.DateTimeRaw, out var dateTime))
        {
            var timeZoneId = eventDateTime.TimeZone ?? TimeZoneInfo.Local.Id;
            TimeZoneInfo timeZone;
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch
            {
                timeZone = TimeZoneInfo.Local;
            }
            return new ZonedDateTime(dateTime, timeZone);
        }

        if (eventDateTime.Date != null && DateTime.TryParse(eventDateTime.Date, out var date))
        {
            return new ZonedDateTime(date, TimeZoneInfo.Local);
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
