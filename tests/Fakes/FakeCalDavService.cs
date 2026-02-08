using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Storage.Models;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace perinma.Tests.Fakes;

public class FakeCalDavService : ICalDavService
{
    private readonly List<CalDavCalendar> _calendars = new();
    private readonly Dictionary<string, List<CalDavEvent>> _calendarEvents = new();
    private readonly List<(string CalendarUrl, string Title, string? Description, string? Location, DateTime StartTime, DateTime EndTime)> _createdEvents = new();

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

    public Task<string> RespondToEventAsync(
        CalDavCredentials credentials,
        string eventUrl,
        string rawICalendar,
        string responseStatus,
        string userEmail,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(eventUrl);
    }

    public Task<string> CreateEventAsync(
        CalDavCredentials credentials,
        string calendarUrl,
        string title,
        string? description,
        string? location,
        DateTime startTime,
        DateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        var eventUid = Guid.NewGuid().ToString();
        var eventUrl = $"{TrimTrailingSlash(calendarUrl)}{eventUid}.ics";

        _createdEvents.Add((calendarUrl, title, description, location, startTime, endTime));

        var calendar = new Ical.Net.Calendar();
        var evt = new CalendarEvent
        {
            Summary = title,
            Description = description,
            Location = location,
            Start = ToCalDateTime(startTime),
            End = ToCalDateTime(endTime),
            Uid = eventUid
        };

        calendar.Events.Add(evt);

        var serializer = new Ical.Net.Serialization.CalendarSerializer();
        var iCalendarData = serializer.SerializeToString(calendar)
            ?? throw new InvalidOperationException("Failed to serialize calendar");

        if (ShouldAddTimezone(startTime, endTime))
        {
            iCalendarData = AddVTimezoneComponent(iCalendarData, TimeZoneInfo.Local.Id);
        }

        return Task.FromResult(eventUrl);
    }

    public Task<string> UpdateEventAsync(
        CalDavCredentials credentials,
        string eventUrl,
        string rawICalendar,
        string title,
        string? description,
        string? location,
        DateTime startTime,
        DateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        var calendar = Ical.Net.Calendar.Load(rawICalendar);
        var evt = calendar?.Events.FirstOrDefault();

        if (evt == null)
        {
            throw new InvalidOperationException("Could not parse event from iCalendar data");
        }

        evt.Summary = title;
        evt.Description = description;
        evt.Location = location;
        evt.Start = ToCalDateTime(startTime);
        evt.End = ToCalDateTime(endTime);

        var serializer = new Ical.Net.Serialization.CalendarSerializer();
        var updatedICalendar = serializer.SerializeToString(calendar)
            ?? throw new InvalidOperationException("Failed to serialize updated calendar");

        if (ShouldAddTimezone(startTime, endTime))
        {
            updatedICalendar = AddVTimezoneComponent(updatedICalendar, TimeZoneInfo.Local.Id);
        }

        return Task.FromResult(updatedICalendar);
    }

    public Task<string> UpdateEventAsync(
        CalDavCredentials credentials,
        string eventUrl,
        string rawICalendar,
        string title,
        string? description,
        string? location,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        return UpdateEventAsync(
            credentials,
            eventUrl,
            rawICalendar,
            title,
            description,
            location,
            startTime,
            endTime,
            null,
            cancellationToken);
    }

    public IReadOnlyList<(string CalendarUrl, string Title, string? Description, string? Location, DateTime StartTime, DateTime EndTime)> GetCreatedEvents()
    {
        return _createdEvents.AsReadOnly();
    }

    public void ClearCreatedEvents()
    {
        _createdEvents.Clear();
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
    /// Creates a recurring CalDAV event with RRULE in raw iCalendar data.
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
        var rawICalendar = $@"BEGIN:VTIMEZONE
TZID:{tzid}
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

    private static string TrimTrailingSlash(string url)
    {
        return url.EndsWith("/") ? url.TrimEnd('/') : url;
    }

    private static CalDateTime ToCalDateTime(DateTime dateTime)
    {
        var adjustedDateTime = dateTime.Kind == DateTimeKind.Local
            ? dateTime.ToUniversalTime()
            : (dateTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dateTime, DateTimeKind.Local) : dateTime);
        return new CalDateTime(adjustedDateTime, true);
    }

    private static bool ShouldAddTimezone(DateTime startTime, DateTime endTime)
    {
        return startTime.Kind == DateTimeKind.Local || endTime.Kind == DateTimeKind.Local;
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

    private static string AddVTimezoneComponent(string iCalendarData, string tzId)
    {
        var vtimezoneComponent = $"""
            BEGIN:VTIMEZONE
            TZID:{tzId}
            BEGIN:STANDARD
            DTSTART:19700101T000000
            TZOFFSETFROM:+0000
            RRULE:FREQ=YEARLY
            END:STANDARD
            END:VTIMEZONE
            """;

        var endIndex = iCalendarData.LastIndexOf("END:VEVENT");
        if (endIndex == -1)
        {
            return iCalendarData;
        }

        return iCalendarData.Insert(endIndex + 1, vtimezoneComponent + "\r\n");
    }
}
