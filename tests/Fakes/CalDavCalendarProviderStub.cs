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

    public CalendarEvent ParseEventForEdit(RawEvent rawEvent)
    {
        return new CalendarEvent
        {
            Reference = rawEvent.Reference,
            StartTime = new LocalDateTime(2025, 1, 1, 9, 0),
            EndTime = new LocalDateTime(2025, 1, 1, 10, 0),
            Title = "Stub Event"
        };
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
            SyncToken = null,
            MissingEventsAreAuthoritative = string.IsNullOrEmpty(syncToken)
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

    public IList<(Instant Occurrence, Instant TriggerTime, string? TargetEventId)> GetNextReminderOccurrences(
        string rawEventData,
        string? rawCalendarData = null,
        Instant referenceTime = default,
        IList<string>? overrides = null)
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

    public Task<(string externalId, string rawData)> CreateEventAsync(
        string accountId,
        string calendarId,
        string title,
        ModelExtensions extensions,
        LocalDateTime startTime,
        LocalDateTime endTime,
        SendInvitesResult sendUpdates = SendInvitesResult.SendToAll,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult((Guid.NewGuid().ToString(), string.Empty));
    }

    public Task UpdateEventAsync(
        CalendarEvent calendarEvent,
        EventEditScope scope,
        SendInvitesResult sendUpdates = SendInvitesResult.SendToAll,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task DeleteEventAsync(
        CalendarEvent calendarEvent,
        EventDeleteAction action,
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
        CalendarEventExtensions.Attachments,
        CalendarEventExtensions.RecurrenceInfo
    ];

    public Task<IList<AttendeeFreeBusy>> GetFreeBusyAsync(
        string accountId,
        IList<string> attendeeEmails,
        Interval timeRange,
        CancellationToken cancellationToken = default)
    {
        IList<AttendeeFreeBusy> result = attendeeEmails
            .Select(e => new AttendeeFreeBusy { Email = e, Status = FreeBusyStatus.Unknown })
            .ToList<AttendeeFreeBusy>();
        return Task.FromResult(result);
    }
}
