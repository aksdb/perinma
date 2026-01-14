using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using perinma.Services;
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
}
