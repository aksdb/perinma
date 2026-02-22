using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodaTime;
using perinma.Models;
using perinma.Services;

namespace tests.Fakes;

public class CalDavCalendarProviderStub : ICalendarProvider
{
    public List<CalendarEvent> ParseCalendarEvents(List<RawEvent> rawEvents, Interval timeRange)
    {
        return [];
    }

    public Task<CalendarSyncResult> GetCalendarsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CalendarSyncResult
        {
            Calendars = [],
            SyncToken = null
        });
    }

    public Task<EventSyncResult> GetEventsAsync(
        string accountId,
        string calendarExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EventSyncResult
        {
            Events = [],
            SyncToken = null
        });
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

    public IList<(Instant Occurrence, Instant TriggerTime)> GetNextReminderOccurrences(
        string rawEventData,
        string? rawCalendarData = null,
        Instant referenceTime = default)
    {
        return [];
    }

    public Instant? GetEventStartTime(
        string rawEventData,
        Instant? occurrenceTime = null)
    {
        return null;
    }

    public Task RespondToEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string rawEventData,
        string responseStatus,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<string> CreateEventAsync(
        string accountId,
        string calendarId,
        string title,
        ModelExtensions extensions,
        Instant startTime,
        Instant endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Guid.NewGuid().ToString());
    }

    public Task UpdateEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string title,
        ModelExtensions extensions,
        Instant startTime,
        Instant endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public IList<object> GetSupportedExtensions() =>
    [
        CalendarEventExtensions.FullDay,
        CalendarEventExtensions.TimeZone,
        CalendarEventExtensions.Location,
        CalendarEventExtensions.Description,
        CalendarEventExtensions.Attachments
    ];
}
