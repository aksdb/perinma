using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Storage.Models;

namespace perinma.Tests.Fakes;

public class FakeCalDavService : ICalDavService
{
    private readonly List<CalDavCalendar> _calendars = new();
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
            _calendarEvents[calendarUrl] = new List<CalDavEvent>();
        }
        _calendarEvents[calendarUrl].Clear();
        _calendarEvents[calendarUrl].AddRange(events);
    }

    public Task<ICalDavService.CalendarSyncResult> GetCalendarsAsync(
        CalDavCredentials credentials,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ICalDavService.CalendarSyncResult
        {
            Calendars = _calendars,
            SyncToken = null
        };
        return Task.FromResult(result);
    }

    public Task<ICalDavService.EventSyncResult> GetEventsAsync(
        CalDavCredentials credentials,
        string calendarUrl,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var events = _calendarEvents.ContainsKey(calendarUrl)
            ? _calendarEvents[calendarUrl]
            : new List<CalDavEvent>();

        var result = new ICalDavService.EventSyncResult
        {
            Events = events,
            SyncToken = null
        };
        return Task.FromResult(result);
    }

    public Task<bool> TestConnectionAsync(
        CalDavCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Creates a simple CalDAV event without recurrence.
    /// </summary>
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

    /// <summary>
    /// Creates a recurring CalDAV event with RRULE in the raw iCalendar data.
    /// </summary>
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

    /// <summary>
    /// Creates a recurring CalDAV event with timezone-aware times.
    /// </summary>
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
