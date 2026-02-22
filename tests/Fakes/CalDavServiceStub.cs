using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CredentialStore;
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
        var serializer = new Ical.Net.Serialization.CalendarSerializer();
        return Task.FromResult(serializer.SerializeToString(calendar) ?? string.Empty);
    }

    public IReadOnlyList<Ical.Net.Calendar> GetCreatedCalendars()
    {
        return _createdCalendars.AsReadOnly();
    }

    public void ClearCreatedCalendars()
    {
        _createdCalendars.Clear();
    }
}
