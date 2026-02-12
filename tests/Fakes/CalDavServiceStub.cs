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
    private readonly List<(string CalendarUrl, string Title, string? Description, string? Location, DateTime StartTime, DateTime EndTime)> _createdEvents = new();

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
        string title,
        string? description,
        string? location,
        DateTime startTime,
        DateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        var eventUid = Guid.NewGuid().ToString();
        var eventUrl = calendarUrl.EndsWith("/")
            ? calendarUrl + $"{eventUid}.ics"
            : calendarUrl + $"/{eventUid}.ics";

        _createdEvents.Add((calendarUrl, title, description, location, startTime, endTime));

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
        return Task.FromResult(rawICalendar);
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
        return UpdateEventAsync(credentials, eventUrl, rawICalendar, title, description, location, startTime, endTime, null, cancellationToken);
    }

    public IReadOnlyList<(string CalendarUrl, string Title, string? Description, string? Location, DateTime StartTime, DateTime EndTime)> GetCreatedEvents()
    {
        return _createdEvents.AsReadOnly();
    }

    public void ClearCreatedEvents()
    {
        _createdEvents.Clear();
    }
}
