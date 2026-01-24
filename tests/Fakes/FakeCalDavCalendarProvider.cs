using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Storage.Models;

namespace perinma.Tests.Fakes;

/// <summary>
/// Fake implementation of ICalendarProvider for testing CalDAV sync.
/// </summary>
public class FakeCalDavCalendarProvider : ICalendarProvider
{
    private readonly List<CalDavCalendar> _calendars = [];
    private readonly Dictionary<string, List<CalDavEvent>> _calendarEvents = new();

    public void SetCalendars(params CalDavCalendar[] calendars)
    {
        _calendars.Clear();
        _calendars.AddRange(calendars);
    }

    public void SetEvents(string calendarUrl, params CalDavEvent[] events)
    {
        if (!_calendarEvents.ContainsKey(calendarUrl))
        {
            _calendarEvents[calendarUrl] = [];
        }
        _calendarEvents[calendarUrl].Clear();
        _calendarEvents[calendarUrl].AddRange(events);
    }

    public Task<CalendarSyncResult> GetCalendarsAsync(
        AccountCredentials credentials,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        // Validate credentials type
        if (credentials is not CalDavCredentials)
        {
            throw new InvalidOperationException("FakeCalDavCalendarProvider requires CalDavCredentials");
        }

        // Convert to provider-agnostic format
        var calendars = _calendars.Select(c => new ProviderCalendar
        {
            ExternalId = c.Url,
            Name = c.DisplayName,
            Color = c.Color,
            Selected = true, // CalDAV doesn't have "selected" concept
            Deleted = c.Deleted,
            RawData = null
        }).ToList();

        var result = new CalendarSyncResult
        {
            Calendars = calendars,
            SyncToken = null
        };

        return Task.FromResult(result);
    }

    public Task<EventSyncResult> GetEventsAsync(
        AccountCredentials credentials,
        string calendarExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        // Validate credentials type
        if (credentials is not CalDavCredentials)
        {
            throw new InvalidOperationException("FakeCalDavCalendarProvider requires CalDavCredentials");
        }

        // Get events for this calendar
        var caldavEvents = _calendarEvents.TryGetValue(calendarExternalId, out var events)
            ? events
            : [];

        // Convert to provider-agnostic format
        var providerEvents = caldavEvents.Select(ConvertCalDavEvent).Where(e => e != null).Cast<ProviderEvent>().ToList();

        var result = new EventSyncResult
        {
            Events = providerEvents,
            SyncToken = null
        };

        return Task.FromResult(result);
    }

    public Task<bool> TestConnectionAsync(
        AccountCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(credentials is CalDavCredentials);
    }

    public Task<IList<int>> GetReminderMinutesAsync(
        string rawEventData,
        string? rawCalendarData = null,
        CancellationToken cancellationToken = default)
    {
        // Return empty list by default - tests can override if needed
        return Task.FromResult<IList<int>>([]);
    }

    public Task RespondToEventAsync(
        AccountCredentials credentials,
        string calendarId,
        string eventId,
        string rawEventData,
        string responseStatus,
        CancellationToken cancellationToken = default)
    {
        // Validate credentials type
        if (credentials is not CalDavCredentials)
        {
            throw new InvalidOperationException("FakeCalDavCalendarProvider requires CalDavCredentials");
        }

        // For testing, just return completed task
        return Task.CompletedTask;
    }

    private static ProviderEvent? ConvertCalDavEvent(CalDavEvent evt)
    {
        var isDeleted = evt.Status == "CANCELLED" || evt.Deleted;

        if (isDeleted)
        {
            return new ProviderEvent
            {
                ExternalId = evt.Uid,
                Title = evt.Summary,
                Status = evt.Status,
                Deleted = true,
                RawData = evt.RawICalendar
            };
        }

        return new ProviderEvent
        {
            ExternalId = evt.Uid,
            Title = evt.Summary ?? "Untitled Event",
            StartTime = evt.StartTime,
            EndTime = evt.EndTime,
            Status = evt.Status,
            Deleted = false,
            RecurringEventId = null,
            OriginalStartTime = null,
            RawData = evt.RawICalendar
        };
    }

    // Helper methods to create test data
    public static CalDavCalendar CreateCalendar(string url, string displayName, string? color = null)
    {
        return new CalDavCalendar
        {
            Url = url,
            DisplayName = displayName,
            Color = color,
            Deleted = false
        };
    }

    public static CalDavEvent CreateEvent(string uid, string url, string summary, DateTime start, DateTime end)
    {
        var rawICalendar = $@"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:{uid}
DTSTART:{FormatDateTime(start)}
DTEND:{FormatDateTime(end)}
SUMMARY:{summary}
STATUS:CONFIRMED
END:VEVENT
END:VCALENDAR";

        return new CalDavEvent
        {
            Uid = uid,
            Url = url,
            Summary = summary,
            StartTime = start,
            EndTime = end,
            Status = "CONFIRMED",
            RawICalendar = rawICalendar,
            Deleted = false
        };
    }

    public static CalDavEvent CreateRecurringEvent(
        string uid,
        string url,
        string summary,
        DateTime start,
        DateTime end,
        string rrule)
    {
        var rawICalendar = $@"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:{uid}
DTSTART:{FormatDateTime(start)}
DTEND:{FormatDateTime(end)}
SUMMARY:{summary}
STATUS:CONFIRMED
{rrule}
END:VEVENT
END:VCALENDAR";

        return new CalDavEvent
        {
            Uid = uid,
            Url = url,
            Summary = summary,
            StartTime = start,
            EndTime = end,
            Status = "CONFIRMED",
            RawICalendar = rawICalendar,
            Deleted = false
        };
    }

    public static CalDavEvent CreateRecurringEventWithTimezone(
        string uid,
        string url,
        string summary,
        DateTime start,
        DateTime end,
        string tzid,
        string rrule)
    {
        var rawICalendar = $@"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VTIMEZONE
TZID:{tzid}
END:VTIMEZONE
BEGIN:VEVENT
UID:{uid}
DTSTART;TZID={tzid}:{FormatDateTimeLocal(start)}
DTEND;TZID={tzid}:{FormatDateTimeLocal(end)}
SUMMARY:{summary}
STATUS:CONFIRMED
{rrule}
END:VEVENT
END:VCALENDAR";

        return new CalDavEvent
        {
            Uid = uid,
            Url = url,
            Summary = summary,
            StartTime = start,
            EndTime = end,
            Status = "CONFIRMED",
            RawICalendar = rawICalendar,
            Deleted = false
        };
    }

    private static string FormatDateTime(DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'");
    }

    private static string FormatDateTimeLocal(DateTime dt)
    {
        return dt.ToString("yyyyMMdd'T'HHmmss");
    }
}
