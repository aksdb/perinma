using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CredentialStore;
using NodaTime;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Storage.Models;

namespace tests.Fakes;

/// <summary>
/// Simple stub for ICalDavService that returns predefined raw data.
/// Used for testing real providers without making actual API calls.
/// </summary>
public class CalDavServiceStub : ICalDavService
{
    private readonly List<CalDavCalendar> _calendars = new();
    private readonly Dictionary<string, List<CalDavEvent>> _eventsByCalendar = new();
    private readonly List<Ical.Net.Calendar> _createdCalendars = new();
    private IList<AttendeeFreeBusy>? _freeBusyResult;
    private bool _deleteLeavesDetachedOverrides;
    private readonly List<string> _deletedEventUrls = new();

    public void SetFreeBusyResult(IList<AttendeeFreeBusy> result) => _freeBusyResult = result;

    public void SetDeleteLeavesDetachedOverrides(bool enabled) => _deleteLeavesDetachedOverrides = enabled;

    /// <summary>
    /// Sets the calendars to return.
    /// </summary>
    public void SetCalendars(params CalDavCalendar[] calendars)
    {
        _calendars.Clear();
        _calendars.AddRange(calendars);
    }

    /// <summary>
    /// Sets the events to return for a specific calendar.
    /// </summary>
    public void SetEvents(string calendarUrl, params CalDavEvent[] events)
    {
        if (!_eventsByCalendar.ContainsKey(calendarUrl))
        {
            _eventsByCalendar[calendarUrl] = new List<CalDavEvent>();
        }
        _eventsByCalendar[calendarUrl].Clear();
        _eventsByCalendar[calendarUrl].AddRange(events);
    }

    public IReadOnlyList<CalDavEvent> GetEvents(string calendarUrl) =>
        _eventsByCalendar.TryGetValue(calendarUrl, out var events)
            ? events.AsReadOnly()
            : Array.Empty<CalDavEvent>();

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
        var events = _eventsByCalendar.ContainsKey(calendarUrl)
            ? _eventsByCalendar[calendarUrl]
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
        Ical.Net.Calendar calendar,
        CancellationToken cancellationToken = default)
    {
        var evt = calendar?.Events.FirstOrDefault();
        var eventUid = evt?.Uid ?? Guid.NewGuid().ToString();
        var eventUrl = calendarUrl.EndsWith("/")
            ? calendarUrl + $"{eventUid}.ics"
            : calendarUrl + $"/{eventUid}.ics";

        if (calendar != null)
        {
            _createdCalendars.Add(calendar);
            UpsertEvent(calendarUrl, eventUrl, calendar);
        }

        return Task.FromResult(eventUrl);
    }

    public Task<string> UpdateEventAsync(
        CalDavCredentials credentials,
        string eventUrl,
        Ical.Net.Calendar calendar,
        CancellationToken cancellationToken = default)
    {
        _createdCalendars.Add(calendar);
        UpsertEvent(GetCalendarUrl(eventUrl), eventUrl, calendar);
        var serializer = new Ical.Net.Serialization.CalendarSerializer();
        return Task.FromResult(serializer.SerializeToString(calendar) ?? string.Empty);
    }

    public Task DeleteEventAsync(
        CalDavCredentials credentials,
        string eventUrl,
        CancellationToken cancellationToken = default)
    {
        _deletedEventUrls.Add(eventUrl);
        var calendarUrl = GetCalendarUrl(eventUrl);
        if (_eventsByCalendar.TryGetValue(calendarUrl, out var events))
        {
            var index = events.FindIndex(e => e.Url == eventUrl);
            if (index >= 0)
            {
                if (_deleteLeavesDetachedOverrides)
                {
                    var replacement = BuildDetachedOverrideOnlyEvent(events[index]);
                    if (replacement != null)
                        events[index] = replacement;
                    else
                        events.RemoveAt(index);
                }
                else
                {
                    events.RemoveAt(index);
                }
            }
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<Ical.Net.Calendar> GetCreatedCalendars()
    {
        return _createdCalendars.AsReadOnly();
    }

    public void ClearCreatedCalendars()
    {
        _createdCalendars.Clear();
    }

    public IReadOnlyList<string> GetDeletedEventUrls()
    {
        return _deletedEventUrls.AsReadOnly();
    }

    public void ClearDeletedEventUrls()
    {
        _deletedEventUrls.Clear();
    }

    private void UpsertEvent(string calendarUrl, string eventUrl, Ical.Net.Calendar calendar)
    {
        if (!_eventsByCalendar.TryGetValue(calendarUrl, out var events))
        {
            events = [];
            _eventsByCalendar[calendarUrl] = events;
        }

        var serializer = new Ical.Net.Serialization.CalendarSerializer();
        var rawICalendar = serializer.SerializeToString(calendar) ?? string.Empty;
        var primaryEvent = calendar.Events.First();
        var entry = new CalDavEvent
        {
            Uid = primaryEvent.Uid ?? Guid.NewGuid().ToString(),
            Url = eventUrl,
            Summary = primaryEvent.Summary,
            StartTime = primaryEvent.Start?.AsUtc,
            EndTime = primaryEvent.End?.AsUtc,
            Status = primaryEvent.Status,
            RawICalendar = rawICalendar,
            Deleted = false,
            ICalendar = calendar
        };

        var index = events.FindIndex(e => e.Url == eventUrl);
        if (index >= 0)
            events[index] = entry;
        else
            events.Add(entry);
    }

    private static string GetCalendarUrl(string eventUrl)
    {
        var slashIndex = eventUrl.LastIndexOf('/');
        return slashIndex >= 0 ? eventUrl[..slashIndex] : eventUrl;
    }

    private static CalDavEvent? BuildDetachedOverrideOnlyEvent(CalDavEvent source)
    {
        if (string.IsNullOrEmpty(source.RawICalendar))
            return null;

        var calendar = Ical.Net.Calendar.Load(source.RawICalendar);
        var overrides = calendar?.Events.Where(e => e.RecurrenceIdentifier != null).ToList();
        if (overrides is not { Count: > 0 })
            return null;

        var replacementCalendar = new Ical.Net.Calendar();
        foreach (var @event in overrides)
            replacementCalendar.Events.Add(@event);

        var serializer = new Ical.Net.Serialization.CalendarSerializer();
        var rawICalendar = serializer.SerializeToString(replacementCalendar) ?? string.Empty;
        var primaryEvent = overrides.First();
        return new CalDavEvent
        {
            Uid = source.Uid,
            Url = source.Url,
            Summary = primaryEvent.Summary,
            StartTime = primaryEvent.Start?.AsUtc,
            EndTime = primaryEvent.End?.AsUtc,
            Status = primaryEvent.Status,
            RawICalendar = rawICalendar,
            Deleted = false,
            ICalendar = replacementCalendar
        };
    }

    public Task<IList<AttendeeFreeBusy>> GetFreeBusyAsync(
        CalDavCredentials credentials,
        string accountId,
        string organizerEmail,
        IList<string> attendeeEmails,
        Interval timeRange,
        IList<TimeSlot> organizerBusySlots,
        CancellationToken cancellationToken = default)
    {
        var result = _freeBusyResult
            ?? attendeeEmails
                .Select(e => new AttendeeFreeBusy { Email = e, Status = FreeBusyStatus.Unknown })
                .ToList<AttendeeFreeBusy>();
        return Task.FromResult(result);
    }
}
