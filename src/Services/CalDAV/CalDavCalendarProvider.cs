using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ical.Net.DataTypes;
using NodaTime;
using NodaTime.Extensions;
using perinma.Models;
using perinma.Utils;
using Calendar = Ical.Net.Calendar;
using Duration = NodaTime.Duration;
using ICalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace perinma.Services.CalDAV;

/// <summary>
/// CalDAV implementation of ICalendarProvider.
/// </summary>
public class CalDavCalendarProvider(
    ICalDavService calDavService,
    CredentialManagerService credentialManager)
    : ICalendarProvider
{
    /// <inheritdoc/>
    public List<CalendarEvent> ParseCalendarEvents(List<RawEvent> rawEvents, Interval timeRange) =>
        rawEvents
            .Select(t => (t.Reference, Calendar: Calendar.Load(t.RawData)))
            .Where(t => t.Calendar is { Events.Count: > 0 })
            .SelectMany(t => t.Calendar!.Events.Select(evt => (t.Reference, evt)))
            .SelectMany(t =>
            {
                if (t.evt.RecurrenceRules.Count > 0)
                {
                    return t.evt.GetOccurrences(new CalDateTime(timeRange.Start.ToDateTimeUtc()))
                        .TakeWhile(o => o.Period.StartTime.Value <= timeRange.End.ToDateTimeUtc())
                        .Select(occurrence =>
                        {
                            var startTime = Instant.FromDateTimeOffset(occurrence.Period.StartTime.AsUtc);
                            string? timeZone = occurrence.Period.StartTime.TzId ??
                                               t.evt.Start?.TzId;
                            
                            Instant endTime;
                            if (occurrence.Period.EndTime is {} occurrenceEndTime)
                                endTime = Instant.FromDateTimeOffset(occurrenceEndTime.AsUtc);
                            else if (t.evt.Duration is {} eventDuration)
                                endTime = startTime.Plus(Duration.FromTimeSpan(eventDuration.ToTimeSpan(occurrence.Period.StartTime!)));
                            else if (t.evt is { Start: {} eventStart, End: {} eventEnd })
                                endTime = startTime.Plus(Duration.FromTimeSpan(eventEnd.Value - eventStart.Value));
                            else
                                endTime = startTime;

                            return (t.Reference, t.evt, startTime, endTime, timeZone);
                        });
                }

                if (t.evt.Start != null && t.evt.End != null)
                {
                    var startTime = Instant.FromDateTimeOffset(t.evt.Start.AsUtc);
                    var endTime = Instant.FromDateTimeOffset(t.evt.End.AsUtc);
                    return [(t.Reference, t.evt, startTime, endTime, t.evt.Start.TzId)];
                }

                return [];
            })
            .Where(t => t.startTime <= timeRange.End && t.endTime >= timeRange.Start)
            .Select(t => MapToCalendarEvent(t.Reference, t.evt, t.startTime, t.endTime, t.timeZone))
            .ToList();

    private static CalendarEvent MapToCalendarEvent(EventReference reference, ICalEvent evt,
        Instant startTime, Instant endTime, string? timeZone)
    {
        var localStartTime = startTime.ToLocalDateTime();
        var localEndTime = endTime.ToLocalDateTime();
        
        var extensions = new ModelExtensions();
        if (evt.Start?.HasTime == false)
        {
            extensions.Set(CalendarEventExtensions.FullDay, true);
            localStartTime = localStartTime.Date.AtMidnight();
            localEndTime = localEndTime.Date.AtMidnight();
        }

        if (timeZone != null)
            extensions.Set(CalendarEventExtensions.TimeZone, timeZone);
        
        if (evt.Location != null)
            extensions.Set(CalendarEventExtensions.Location, evt.Location);
        
        if (evt.Description != null)
            extensions.Set(CalendarEventExtensions.Description, new RichText.SimpleText(evt.Description));
        
        if (evt.Url != null)
            extensions.Set(CalendarEventExtensions.Attachments, [
                new CalendarEventAttachment
                {
                    Title = "URL",
                    Url = evt.Url.ToString(),
                }
            ]);
        
        return new CalendarEvent
        {
            Reference = reference,
            Title = evt.Summary,
            StartTime = localStartTime,
            EndTime = localEndTime,
            ChangedAt = evt.DtStamp?.AsUtc,
            ResponseStatus = MapResponseStatus(evt.Status),
            Extensions = extensions,
        };
    }

    private static EventResponseStatus MapResponseStatus(string? status) => status switch
    {
        "CONFIRMED" => EventResponseStatus.Accepted,
        "TENTATIVE" => EventResponseStatus.Tentative,
        "CANCELLED" => EventResponseStatus.Declined,
        "NEEDS-ACTION" => EventResponseStatus.NeedsAction,
        _ => EventResponseStatus.None
    };

    /// <inheritdoc/>
    public async Task<CalendarSyncResult> GetCalendarsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
        {
            throw new InvalidOperationException($"No CalDAV credentials found for account {accountId}");
        }

        // Fetch calendars from CalDAV server
        var result = await calDavService.GetCalendarsAsync(calDavCredentials, syncToken, cancellationToken);

        // Convert to provider-agnostic format
        var calendars = result.Calendars.Select(c =>
        {
            var data = new Dictionary<string, DataAttribute>
            {
                ["rawData"] = new DataAttribute.Text(c.PropfindXml)
            };

            if (c.Owner != null)
                data["owner"] = new DataAttribute.Text(c.Owner);

            if (c.AclXml != null)
                data["rawACL"] = new DataAttribute.Text(c.AclXml);

            if (c.CurrentUserPrivilegeSetXml != null)
                data["currentUserPrivilegeSet"] = new DataAttribute.Text(c.CurrentUserPrivilegeSetXml);

            return new ProviderCalendar
            {
                ExternalId = c.Url,
                Name = c.DisplayName,
                Color = c.Color,
                Selected = true, // CalDAV doesn't have a "selected" concept, default to enabled
                Deleted = c.Deleted,
                Data = data,
            };
        }).ToList();

        return new CalendarSyncResult
        {
            Calendars = calendars,
            SyncToken = result.SyncToken
        };
    }

    /// <inheritdoc/>
    public async Task<EventSyncResult> GetEventsAsync(
        string accountId,
        string calendarExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
            throw new InvalidOperationException($"No CalDAV credentials found for account {accountId}");

        // Fetch events from CalDAV server
        var result =
            await calDavService.GetEventsAsync(calDavCredentials, calendarExternalId, syncToken, cancellationToken);

        // Convert to provider-agnostic format
        var events = result.Events.Select(ConvertCalDavEvent).OfType<ProviderEvent>().ToList();

        return new EventSyncResult
        {
            Events = events,
            SyncToken = result.SyncToken
        };
    }

    /// <inheritdoc/>
    public Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
        {
            return Task.FromResult(false);
        }

        return calDavService.TestConnectionAsync(calDavCredentials, cancellationToken);
    }

    private static ProviderEvent? ConvertCalDavEvent(CalDavEvent evt)
    {
        // Check if event was deleted or cancelled
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
        
        var iCalendar = evt.ICalendar ?? evt.RawICalendar?.Let(Calendar.Load);
        if (iCalendar == null)
            return null;

        Instant? startTime = null;
        Instant? endTime = null;

        foreach (var iCalEvent in iCalendar.Events)
        {
            if (iCalEvent.Uid != evt.Uid)
                continue;

            var eventStart = iCalEvent.Start?.AsUtc.ToInstant();
            var eventEnd  = iCalEvent.End?.AsUtc.ToInstant();
            
            if (eventStart != null && (startTime == null || eventStart < startTime))
                startTime = eventStart;
            
            var recurrenceEndTime = RecurrenceParser.CalculateRecurrenceEndTime(iCalEvent)?.ToUniversalTime().ToInstant();
            if (recurrenceEndTime != null && (endTime == null || recurrenceEndTime > endTime))
                endTime = recurrenceEndTime;
            if (recurrenceEndTime == null && eventEnd != null && (endTime == null || eventEnd > endTime))
                endTime = eventEnd;
        }

        return new ProviderEvent
        {
            ExternalId = evt.Uid,
            Title = evt.Summary ?? "Untitled Event",
            StartTime = startTime,
            EndTime = endTime,
            Status = evt.Status,
            Deleted = false,
            RecurringEventId = null, // CalDAV handles recurrence differently
            OriginalStartTime = null,
            RawData = evt.RawICalendar
        };
    }

    /// <inheritdoc/>
    public IList<int> GetReminderMinutes(
        string rawEventData,
        string? rawCalendarData = null)
    {
        Calendar? calendar;
        try
        {
            calendar = Calendar.Load(rawEventData);
        }
        catch
        {
            return [];
        }

        var evt = calendar?.Events.FirstOrDefault();
        if (evt == null)
            return [];

        var alarms = evt.Alarms;
        if (alarms.Count == 0)
            return [];

        List<int> reminderMinutes = [];

        foreach (var alarm in alarms)
        {
            if (alarm.Trigger?.IsRelative != true || !alarm.Trigger.Duration.HasValue)
                continue;

            var duration = alarm.Trigger.Duration.Value;
            // Use ToTimeSpanUnspecified() to convert Duration to TimeSpan
            // Negative values mean "before the event"
            var totalMinutes = (int)duration.ToTimeSpanUnspecified().TotalMinutes;

            // For reminders, we want positive "minutes before" values
            if (totalMinutes < 0)
            {
                reminderMinutes.Add(-totalMinutes);
            }
        }

        return reminderMinutes;
    }

    /// <inheritdoc/>
    public Instant? GetEventStartTime(
        string rawEventData,
        Instant? occurrenceTime = null)
    {
        var calendar = Calendar.Load(rawEventData);
        var evt = calendar?.Events.FirstOrDefault();
        if (evt == null)
            return null;

        var isRecurring = evt.RecurrenceRules.Count > 0;

        // For non-recurring events or when no occurrence time is specified, return base event start time
        if (!isRecurring || !occurrenceTime.HasValue)
        {
            var baseEventStartTime = evt.Start?.AsUtc;
            if (!baseEventStartTime.HasValue)
                return null;

            return Instant.FromDateTimeUtc(baseEventStartTime.Value);
        }

        var occurrences = evt.GetOccurrences(startTime: new CalDateTime(occurrenceTime.Value.ToDateTimeUtc()));

        var firstOccurrence = occurrences.FirstOrDefault();
        if (firstOccurrence != null)
        {
            var firstOccurrenceTime = firstOccurrence.Period.StartTime.AsUtc;
            return Instant.FromDateTimeUtc(firstOccurrenceTime);
        }

        // Fallback to base event start time
        var fallbackStartTime = evt.Start?.AsUtc;
        if (!fallbackStartTime.HasValue)
        {
            return null;
        }

        return Instant.FromDateTimeUtc(fallbackStartTime.Value);
    }

    /// <inheritdoc/>
    public IList<(Instant Occurrence, Instant TriggerTime)> GetNextReminderOccurrences(
        string rawEventData,
        string? rawCalendarData = null,
        Instant referenceTime = default)
    {
        var calendar = Calendar.Load(rawEventData);
        var evt = calendar?.Events.FirstOrDefault();
        if (evt == null)
            return [];

        var reminderMinutes = GetReminderMinutes(rawEventData, rawCalendarData);
        if (reminderMinutes.Count == 0)
            return [];

        var eventStartTime = evt.Start?.AsUtc.Let(Instant.FromDateTimeUtc);
        if (!eventStartTime.HasValue)
            return [];

        var isRecurring = evt.RecurrenceRules.Count > 0;
        var refTime = referenceTime == default
            ? SystemClock.Instance.GetCurrentInstant()
            : referenceTime;
        var startTime = refTime;
        var result = new List<(Instant Occurrence, Instant TriggerTime)>();

        if (isRecurring)
        {
            // Get all occurrences
            var occurrences = evt.GetOccurrences(startTime: new CalDateTime(startTime.ToDateTimeUtc()));
            var nextOccurrence = occurrences.FirstOrDefault();
            if (nextOccurrence == null)
            {
                return [];
            }

            var occurrenceTime = Instant.FromDateTimeUtc(nextOccurrence.Period.StartTime.AsUtc);
            foreach (var minutes in reminderMinutes)
            {
                var triggerTime = occurrenceTime.Plus(Duration.FromMinutes(-minutes));
                if (triggerTime > startTime)
                {
                    result.Add((occurrenceTime, triggerTime));
                    break;
                }
            }
        }
        else
        {
            foreach (var minutes in reminderMinutes)
            {
                var triggerTime = eventStartTime.Value.Plus(Duration.FromMinutes(-minutes));
                if (triggerTime > startTime)
                {
                    result.Add((eventStartTime.Value, triggerTime));
                }
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task RespondToEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string rawEventData,
        string responseStatus,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
        {
            throw new InvalidOperationException($"No CalDAV credentials found for account {accountId}");
        }

        // For CalDAV, we need user's email (stored in Username)
        var userEmail = calDavCredentials.Username;

        // Respond to event using the service
        await calDavService.RespondToEventAsync(
            calDavCredentials,
            eventId, // eventId is the event URL for CalDAV
            rawEventData,
            responseStatus,
            userEmail,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> CreateEventAsync(
        string accountId,
        string calendarId,
        string title,
        string? description,
        string? location,
        Instant startTime,
        Instant endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
        {
            throw new InvalidOperationException($"No CalDAV credentials found for account {accountId}");
        }

        return await calDavService.CreateEventAsync(
            calDavCredentials,
            calendarId,
            title,
            description,
            location,
            startTime.ToDateTimeUtc(),
            endTime.ToDateTimeUtc(),
            rawEventData,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string title,
        string? description,
        string? location,
        Instant startTime,
        Instant endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
        {
            throw new InvalidOperationException($"No CalDAV credentials found for account {accountId}");
        }

        // For CalDAV, eventId is event URL and rawEventData contains current iCalendar data
        await calDavService.UpdateEventAsync(
            calDavCredentials,
            eventId,
            rawEventData ?? string.Empty,
            title,
            description,
            location,
            startTime.ToDateTimeUtc(),
            endTime.ToDateTimeUtc(),
            rawEventData,
            cancellationToken);
    }
}