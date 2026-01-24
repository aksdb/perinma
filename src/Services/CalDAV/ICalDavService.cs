using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using perinma.Storage.Models;

namespace perinma.Services.CalDAV;

public interface ICalDavService
{
    Task<CalendarSyncResult> GetCalendarsAsync(
        CalDavCredentials credentials,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    Task<EventSyncResult> GetEventsAsync(
        CalDavCredentials credentials,
        string calendarUrl,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    Task<bool> TestConnectionAsync(
        CalDavCredentials credentials,
        CancellationToken cancellationToken = default);

    public class CalendarSyncResult
    {
        public required IList<CalDavCalendar> Calendars { get; init; }
        public string? SyncToken { get; init; }
    }

    public class EventSyncResult
    {
        public required IList<CalDavEvent> Events { get; init; }
        public string? SyncToken { get; init; }
    }
}
