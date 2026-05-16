using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using NodaTime;
using NodaTime.Extensions;
using perinma.Models;
using perinma.Utils;
using perinma.Storage;
using perinma.Services;
using Calendar = Ical.Net.Calendar;
using Duration = NodaTime.Duration;
using ICalEvent = Ical.Net.CalendarComponents.CalendarEvent;
using CalDateTime = Ical.Net.DataTypes.CalDateTime;
using CalendarEvent = perinma.Models.CalendarEvent;

namespace perinma.Services.CalDAV;

/// <summary>
/// CalDAV implementation of ICalendarProvider.
/// </summary>
public class CalDavCalendarProvider(
    ICalDavService calDavService,
    CredentialManagerService credentialManager,
    SqliteStorage? storage = null)
    : ICalendarProvider
{
    private static ModelExtension<ICalEvent> ICalEventExtension = new();
    private static ModelExtension<Calendar> ICalCalendarExtension = new();

    /// <inheritdoc/>
    public void EnrichCalendar(perinma.Models.Calendar calendar, Func<string, string?> getData)
    {
        try
        {
            var privilegeXml = getData("currentUserPrivilegeSet");
            if (string.IsNullOrEmpty(privilegeXml)) return;
            // server didn't return privilege info — safe default (not read-only)

            var xml = XDocument.Parse(privilegeXml);
            XNamespace dav = "DAV:";
            var privileges = xml.Descendants(dav + "privilege")
                .SelectMany(p => p.Elements())
                .Select(e => e.Name)
                .ToHashSet();

            // {DAV:}write subsumes write-content; either means the calendar is writable.
            // Absent write privilege → read-only subscription.
            if (!privileges.Contains(dav + "write") && !privileges.Contains(dav + "write-content"))
                calendar.Extensions.Set(perinma.Models.CalendarExtensions.IsReadOnly, true);
        }
        catch
        {
            // Malformed data — leave extension unset (safe default: not read-only).
        }
    }


    /// <inheritdoc/>
    public List<CalendarEvent> ParseCalendarEvents(List<RawEvent> rawEvents, Interval timeRange) =>
        rawEvents
            .Select(t => (t.Reference, Calendar: Calendar.Load(t.RawData)))
            .Where(t => t.Calendar is { Events.Count: > 0 })
            .SelectMany(t => ParseCalendarEvents(t.Reference, t.Calendar!, timeRange))
            .ToList();

    /// <inheritdoc/>
    public CalendarEvent ParseEventForEdit(RawEvent rawEvent)
    {
        var calendar = Calendar.Load(rawEvent.RawData);
        if (calendar is not { Events.Count: > 0 })
            throw new InvalidOperationException("Failed to parse CalDAV event");

        var baseEvent = calendar.Events
            .FirstOrDefault(evt => evt.RecurrenceIdentifier == null)
            ?? calendar.Events.First();

        var startTime = baseEvent.Start?.AsUtc.ToInstant() ?? Instant.MinValue;
        var endTime = baseEvent.End?.AsUtc.ToInstant() ?? startTime;
        return MapToCalendarEvent(rawEvent.Reference, calendar, baseEvent, startTime, endTime, baseEvent.Start?.TzId,
            BuildRecurrenceEditInfo(baseEvent, null, rawEvent.Reference.ExternalId));
    }

    private static IEnumerable<CalendarEvent> ParseCalendarEvents(EventReference reference, Calendar calendar,
        Interval timeRange)
    {
        var eventsByUid = calendar.Events
            .Where(evt => evt.Uid != null)
            .GroupBy(evt => evt.Uid!)
            .ToList();

        foreach (var group in eventsByUid)
        {
            var baseEvent = group.FirstOrDefault(evt => evt.RecurrenceIdentifier == null && evt.RecurrenceRules.Count > 0)
                ?? group.FirstOrDefault(evt => evt.RecurrenceIdentifier == null)
                ?? group.First();

            var overridesByStart = group
                .Where(evt => evt.RecurrenceIdentifier != null)
                .Select(evt => (Event: evt, OriginalStart: GetInstant(evt.RecurrenceIdentifier)))
                .Where(t => t.OriginalStart != null)
                .ToDictionary(t => t.OriginalStart!.Value, t => t.Event);

            var isRecurring = baseEvent.RecurrenceRules.Count > 0 || overridesByStart.Count > 0;
            if (!isRecurring)
            {
                if (baseEvent.Start == null || baseEvent.End == null)
                    continue;

                var startTime = Instant.FromDateTimeOffset(baseEvent.Start.AsUtc);
                var endTime = Instant.FromDateTimeOffset(baseEvent.End.AsUtc);
                if (startTime > timeRange.End || endTime < timeRange.Start)
                    continue;

                yield return MapToCalendarEvent(reference, calendar, baseEvent, startTime, endTime, baseEvent.Start.TzId,
                    BuildRecurrenceEditInfo(baseEvent, null, reference.ExternalId));
                continue;
            }

            foreach (var occurrence in calendar.GetOccurrences(new CalDateTime(timeRange.Start.ToDateTimeUtc()))
                         .TakeWhile(o => o.Period.StartTime.Value <= timeRange.End.ToDateTimeUtc()))
            {
                if (occurrence.Source is not ICalEvent occurrenceSource || occurrenceSource.Uid != group.Key)
                    continue;

                var originalStart = GetInstant(occurrenceSource.RecurrenceIdentifier)
                    ?? Instant.FromDateTimeOffset(occurrence.Period.StartTime.AsUtc);
                var mappedEvent = occurrenceSource.RecurrenceIdentifier != null
                    ? occurrenceSource
                    : overridesByStart.GetValueOrDefault(originalStart) ?? baseEvent;
                var startTime = mappedEvent == baseEvent
                    ? Instant.FromDateTimeOffset(occurrence.Period.StartTime.AsUtc)
                    : mappedEvent.Start?.AsUtc.ToInstant() ?? Instant.FromDateTimeOffset(occurrence.Period.StartTime.AsUtc);
                var tzId = mappedEvent.Start?.TzId ?? occurrence.Period.StartTime.TzId ?? baseEvent.Start?.TzId;

                Instant endTime;
                if (mappedEvent != baseEvent && mappedEvent.End != null)
                {
                    endTime = Instant.FromDateTimeOffset(mappedEvent.End.AsUtc);
                }
                else if (occurrence.Period.EndTime is { } occurrenceEndTime)
                {
                    endTime = Instant.FromDateTimeOffset(occurrenceEndTime.AsUtc);
                }
                else if (baseEvent.Duration is { } eventDuration)
                {
                    endTime = startTime.Plus(Duration.FromTimeSpan(eventDuration.ToTimeSpan(occurrence.Period.StartTime!)));
                }
                else if (baseEvent is { Start: { } eventStart, End: { } eventEnd })
                {
                    endTime = startTime.Plus(Duration.FromTimeSpan(eventEnd.Value - eventStart.Value));
                }
                else
                {
                    endTime = startTime;
                }

                if (startTime > timeRange.End || endTime < timeRange.Start)
                    continue;

                var recurrenceInfo = BuildRecurrenceEditInfo(mappedEvent, originalStart, reference.ExternalId);
                yield return MapToCalendarEvent(reference, calendar, mappedEvent, startTime, endTime, tzId, recurrenceInfo);
            }
        }
    }

    private static CalendarEvent MapToCalendarEvent(EventReference reference, Calendar calendar, ICalEvent evt,
        Instant startTime, Instant endTime, string? timeZone, RecurrenceEditInfo recurrenceEditInfo)
    {
        var localStartTime = startTime.ToLocalDateTime();
        var localEndTime = endTime.ToLocalDateTime();

        var extensions = new ModelExtensions();
        extensions.Set(ICalCalendarExtension, calendar);
        extensions.Set(ICalEventExtension, evt);
        extensions.Set(CalendarEventExtensions.RecurrenceEdit, recurrenceEditInfo);
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

        if (evt.Transparency == TransparencyType.Transparent)
            extensions.Set(CalendarEventExtensions.NonBlocking, true);
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

    private static RecurrenceEditInfo BuildRecurrenceEditInfo(ICalEvent evt, Instant? originalStartTime,
        string? eventExternalId)
    {
        var actions = new HashSet<RecurringEventAction>();

        if (evt.RecurrenceIdentifier != null)
        {
            actions.Add(RecurringEventAction.EditOccurrence);
            actions.Add(RecurringEventAction.EditSeries);
            actions.Add(RecurringEventAction.DeleteOccurrence);
            actions.Add(RecurringEventAction.DeleteSeries);
            actions.Add(RecurringEventAction.RevertOverride);
            return new RecurrenceEditInfo
            {
                Kind = RecurrenceEditKind.OverrideOccurrence,
                SeriesExternalId = eventExternalId,
                OriginalStartTime = originalStartTime ?? GetInstant(evt.RecurrenceIdentifier),
                BackingExternalId = eventExternalId,
                AllowedActions = actions,
            };
        }

        if (evt.RecurrenceRules.Count > 0)
        {
            actions.Add(RecurringEventAction.EditSeries);
            actions.Add(RecurringEventAction.DeleteSeries);
            if (originalStartTime.HasValue)
            {
                actions.Add(RecurringEventAction.EditOccurrence);
                actions.Add(RecurringEventAction.DeleteOccurrence);
                return new RecurrenceEditInfo
                {
                    Kind = RecurrenceEditKind.GeneratedOccurrence,
                    SeriesExternalId = eventExternalId,
                    OriginalStartTime = originalStartTime,
                    BackingExternalId = eventExternalId,
                    AllowedActions = actions,
                };
            }

            return new RecurrenceEditInfo
            {
                Kind = RecurrenceEditKind.SeriesMaster,
                SeriesExternalId = eventExternalId,
                BackingExternalId = eventExternalId,
                AllowedActions = actions,
            };
        }

        return new RecurrenceEditInfo
        {
            Kind = RecurrenceEditKind.None,
            BackingExternalId = eventExternalId,
        };
    }

    private static Instant? GetInstant(CalDateTime? dateTime) => dateTime == null
        ? null
        : Instant.FromDateTimeOffset(dateTime.AsUtc);

    private static Instant? GetInstant(RecurrenceIdentifier? dateTime)
    {
        if (dateTime == null)
            return null;

        foreach (var propertyName in new[] { "DateTime", "RecurrenceId", "CalDateTime", "Value", "StartTime" })
        {
            var value = dateTime.GetType().GetProperty(propertyName)?.GetValue(dateTime);
            switch (value)
            {
                case CalDateTime calDateTime:
                    return GetInstant(calDateTime);
                case DateTime utcDateTime:
                    return Instant.FromDateTimeUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc));
            }
        }

        throw new InvalidOperationException("Unsupported recurrence identifier representation");
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
                ExternalId = evt.Url,
                Title = evt.Summary,
                Status = evt.Status,
                Deleted = true,
                Data = new Dictionary<string, DataAttribute>
                {
                    { "rawData", new DataAttribute.Text(evt.RawICalendar!) },
                }
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
            var eventEnd = iCalEvent.End?.AsUtc.ToInstant();

            if (eventStart != null && (startTime == null || eventStart < startTime))
                startTime = eventStart;

            var recurrenceEndTime =
                RecurrenceParser.CalculateRecurrenceEndTime(iCalEvent)?.ToUniversalTime().ToInstant();
            if (recurrenceEndTime != null && (endTime == null || recurrenceEndTime > endTime))
                endTime = recurrenceEndTime;
            if (recurrenceEndTime == null && eventEnd != null && (endTime == null || eventEnd > endTime))
                endTime = eventEnd;
        }

        return new ProviderEvent
        {
            ExternalId = evt.Url,
            Title = evt.Summary ?? "Untitled Event",
            StartTime = startTime,
            EndTime = endTime,
            Status = evt.Status,
            Deleted = false,
            RecurringEventId = null, // CalDAV handles recurrence differently
            OriginalStartTime = null,
            Data = new Dictionary<string, DataAttribute>
            {
                { "rawData", new DataAttribute.Text(evt.RawICalendar!) },
            }
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
    public IList<(Instant Occurrence, Instant TriggerTime, string? TargetEventId)> GetNextReminderOccurrences(
        string rawEventData,
        string? rawCalendarData = null,
        Instant referenceTime = default,
        IList<string>? overrides = null)
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
        var result = new List<(Instant Occurrence, Instant TriggerTime, string? TargetEventId)>();

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
                    result.Add((occurrenceTime, triggerTime, null));
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
                    result.Add((eventStartTime.Value, triggerTime, null));
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
    public async Task<(string externalId, string rawData)> CreateEventAsync(
        string accountId,
        string calendarId,
        string title,
        ModelExtensions extensions,
        LocalDateTime startTime,
        LocalDateTime endTime,
        SendInvitesResult sendUpdates = SendInvitesResult.SendToAll,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
        {
            throw new InvalidOperationException($"No CalDAV credentials found for account {accountId}");
        }

        var description = extensions.Get(CalendarEventExtensions.Description) switch
        {
            RichText.HTML html => html.value,
            RichText.SimpleText st => st.value,
            _ => null
        };

        var location = extensions.Get(CalendarEventExtensions.Location);

        var calendar = new Calendar();
        var isFullDay = extensions.Get(CalendarEventExtensions.FullDay);

        var calendarEvent = new ICalEvent
        {
            Summary = title,
            Description = description,
            Location = location,
            Start = isFullDay
                ? new CalDateTime(startTime.Year, startTime.Month, startTime.Day)
                : new CalDateTime(startTime.ToZonedDateTime().ToDateTimeUtc(), true),
            End = isFullDay
                ? new CalDateTime(endTime.Year, endTime.Month, endTime.Day)
                : new CalDateTime(endTime.ToZonedDateTime().ToDateTimeUtc(), true),
            Uid = Guid.NewGuid().ToString()
        };

        // Handle reminder
        var reminderMinutes = extensions.Get(CalendarEventExtensions.ReminderMinutesBefore);
        if (reminderMinutes >= 0)
        {
            calendarEvent.Alarms.Add(new Alarm
            {
                Action = AlarmAction.Display,
                Trigger = new Trigger
                {
                    Duration = new Ical.Net.DataTypes.Duration(minutes: -reminderMinutes)
                }
            });
        }

        // TODO honor timezone extension when available? Might have to convert to localtime then first.

        calendar.Events.Add(calendarEvent);

        var serializer = new CalendarSerializer();
        var rawData = serializer.SerializeToString(calendar)
                      ?? throw new InvalidOperationException("Failed to serialize calendar");

        var externalId = await calDavService.CreateEventAsync(
            calDavCredentials,
            calendarId,
            calendar,
            cancellationToken);

        return (externalId, rawData);
    }

    /// <inheritdoc/>
    public async Task UpdateEventAsync(
        CalendarEvent calendarEvent,
        EventEditScope scope,
        SendInvitesResult sendUpdates = SendInvitesResult.SendToAll,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials =
            credentialManager.GetCalDavCredentials(calendarEvent.Reference.Calendar.Account.Id.ToString());
        if (calDavCredentials == null)
        {
            throw new InvalidOperationException(
                $"No CalDAV credentials found for account {calendarEvent.Reference.Calendar.Account.Name}");
        }

        var recurrenceEdit = calendarEvent.Extensions.Get(CalendarEventExtensions.RecurrenceEdit)
            ?? new RecurrenceEditInfo { Kind = RecurrenceEditKind.None, AllowedActions = [] };
        var originalEvent = calendarEvent.Extensions.Get(ICalEventExtension)
                            ?? throw new InvalidOperationException("Not a CalDAV calendar event");
        var workingCalendar = CloneCalendar(calendarEvent.Extensions.Get(ICalCalendarExtension)
                                            ?? throw new InvalidOperationException("Missing CalDAV calendar data"));

        var seriesMaster = FindSeriesMasterEvent(workingCalendar, originalEvent.Uid);
        var targetEvent = scope switch
        {
            EventEditScope.Event => FindMatchingEvent(workingCalendar, originalEvent),
            EventEditScope.Series => seriesMaster,
            EventEditScope.Occurrence => GetOrCreateOccurrenceOverride(workingCalendar, seriesMaster, recurrenceEdit),
            _ => throw new InvalidOperationException($"Unsupported edit scope {scope}")
        };

        ApplyEditableValues(calendarEvent, targetEvent);

        await calDavService.UpdateEventAsync(
            calDavCredentials,
            ResolveEventUrl(calendarEvent.Reference.Calendar.ExternalId, calendarEvent.Reference.ExternalId),
            workingCalendar,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteEventAsync(
        CalendarEvent calendarEvent,
        EventDeleteAction action,
        CancellationToken cancellationToken = default)
    {
        var accountId = calendarEvent.Reference.Calendar.Account.Id.ToString();
        var calDavCredentials = credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
        {
            throw new InvalidOperationException($"No CalDAV credentials found for account {accountId}");
        }

        if (action is EventDeleteAction.Event or EventDeleteAction.Series)
        {
            await calDavService.DeleteEventAsync(
                calDavCredentials,
                ResolveEventUrl(calendarEvent.Reference.Calendar.ExternalId, calendarEvent.Reference.ExternalId),
                cancellationToken);
            return;
        }

        var recurrenceEdit = calendarEvent.Extensions.Get(CalendarEventExtensions.RecurrenceEdit)
            ?? new RecurrenceEditInfo { Kind = RecurrenceEditKind.None, AllowedActions = [] };
        var originalEvent = calendarEvent.Extensions.Get(ICalEventExtension)
                            ?? throw new InvalidOperationException("Not a CalDAV calendar event");
        var workingCalendar = CloneCalendar(calendarEvent.Extensions.Get(ICalCalendarExtension)
                                            ?? throw new InvalidOperationException("Missing CalDAV calendar data"));
        var seriesMaster = FindSeriesMasterEvent(workingCalendar, originalEvent.Uid);

        if (!recurrenceEdit.OriginalStartTime.HasValue)
            throw new InvalidOperationException("This event does not identify a recurring occurrence");

        var overrideEvent = FindOverrideEvent(workingCalendar, originalEvent.Uid, recurrenceEdit.OriginalStartTime.Value);

        switch (action)
        {
            case EventDeleteAction.Occurrence:
                if (overrideEvent != null)
                    workingCalendar.Events.Remove(overrideEvent);
                AddExceptionDate(seriesMaster, recurrenceEdit.OriginalStartTime.Value);
                break;
            case EventDeleteAction.RevertOverride:
                if (overrideEvent == null)
                    throw new InvalidOperationException("No override exists for this occurrence");
                workingCalendar.Events.Remove(overrideEvent);
                RemoveExceptionDate(seriesMaster, recurrenceEdit.OriginalStartTime.Value);
                break;
            default:
                throw new InvalidOperationException($"Unsupported delete action {action}");
        }

        await calDavService.UpdateEventAsync(
            calDavCredentials,
            ResolveEventUrl(calendarEvent.Reference.Calendar.ExternalId, calendarEvent.Reference.ExternalId),
            workingCalendar,
            cancellationToken);
    }

    private static void ApplyEditableValues(CalendarEvent calendarEvent, ICalEvent iCalEvent)
    {
        var description = calendarEvent.Extensions.Get(CalendarEventExtensions.Description) switch
        {
            RichText.HTML html => html.value,
            RichText.SimpleText st => st.value,
            _ => null
        };

        var location = calendarEvent.Extensions.Get(CalendarEventExtensions.Location);
        var isFullDay = calendarEvent.Extensions.Get(CalendarEventExtensions.FullDay);
        var startTime = calendarEvent.StartTime;
        var endTime = calendarEvent.EndTime;

        iCalEvent.Summary = calendarEvent.Title;
        iCalEvent.Description = description;
        iCalEvent.Location = location;
        iCalEvent.Start = isFullDay
            ? new CalDateTime(startTime.Year, startTime.Month, startTime.Day)
            : new CalDateTime(startTime.ToZonedDateTime().ToDateTimeUtc(), true);
        iCalEvent.End = isFullDay
            ? new CalDateTime(endTime.Year, endTime.Month, endTime.Day)
            : new CalDateTime(endTime.ToZonedDateTime().ToDateTimeUtc(), true);

        iCalEvent.Alarms.Clear();
        var reminderMinutes = calendarEvent.Extensions.Get<int>(CalendarEventExtensions.ReminderMinutesBefore);
        if (reminderMinutes >= 0)
        {
            iCalEvent.Alarms.Add(new Alarm
            {
                Action = AlarmAction.Display,
                Trigger = new Trigger
                {
                    Duration = new Ical.Net.DataTypes.Duration(minutes: -reminderMinutes)
                }
            });
        }
    }

    private static Calendar CloneCalendar(Calendar calendar)
    {
        var serializer = new CalendarSerializer();
        var rawData = serializer.SerializeToString(calendar)
                      ?? throw new InvalidOperationException("Failed to serialize calendar");
        return Calendar.Load(rawData) ?? throw new InvalidOperationException("Failed to clone calendar");
    }

    private static ICalEvent FindSeriesMasterEvent(Calendar calendar, string? uid)
    {
        return calendar.Events
            .Where(evt => evt.Uid == uid)
            .OrderByDescending(evt => evt.RecurrenceRules.Count > 0)
            .ThenBy(evt => evt.RecurrenceIdentifier != null)
            .First();
    }

    private static ICalEvent FindMatchingEvent(Calendar calendar, ICalEvent originalEvent)
    {
        return calendar.Events.First(evt => evt.Uid == originalEvent.Uid &&
                                            Nullable.Equals(GetInstant(evt.RecurrenceIdentifier),
                                                GetInstant(originalEvent.RecurrenceIdentifier)));
    }

    private static ICalEvent? FindOverrideEvent(Calendar calendar, string? uid, Instant originalStartTime)
    {
        return calendar.Events.FirstOrDefault(evt => evt.Uid == uid &&
                                                   GetInstant(evt.RecurrenceIdentifier) == originalStartTime);
    }

    private static ICalEvent GetOrCreateOccurrenceOverride(Calendar calendar, ICalEvent seriesMaster,
        RecurrenceEditInfo recurrenceEdit)
    {
        if (!recurrenceEdit.OriginalStartTime.HasValue)
            throw new InvalidOperationException("Occurrence metadata is missing original start time");

        var existingOverride = FindOverrideEvent(calendar, seriesMaster.Uid, recurrenceEdit.OriginalStartTime.Value);
        if (existingOverride != null)
            return existingOverride;

        var overrideEvent = CloneEvent(seriesMaster);
        overrideEvent.RecurrenceRules.Clear();
        overrideEvent.RecurrenceDates.Clear();
        overrideEvent.ExceptionDates.Clear();
        overrideEvent.RecurrenceIdentifier = new RecurrenceIdentifier(
            ToCalDateTime(recurrenceEdit.OriginalStartTime.Value, seriesMaster.Start));

        var duration = GetEventDuration(seriesMaster);
        overrideEvent.Start = ToCalDateTime(recurrenceEdit.OriginalStartTime.Value, seriesMaster.Start);
        overrideEvent.End = ToCalDateTime(recurrenceEdit.OriginalStartTime.Value.Plus(duration), seriesMaster.End ?? seriesMaster.Start);
        calendar.Events.Add(overrideEvent);
        return overrideEvent;
    }

    private static ICalEvent CloneEvent(ICalEvent source)
    {
        var calendar = new Calendar();
        calendar.Events.Add(source);
        return CloneCalendar(calendar).Events.First();
    }

    private static Duration GetEventDuration(ICalEvent calendarEvent)
    {
        if (calendarEvent.Start != null && calendarEvent.End != null)
            return Duration.FromTimeSpan(calendarEvent.End.Value - calendarEvent.Start.Value);
        return Duration.Zero;
    }

    private static CalDateTime ToCalDateTime(Instant instant, CalDateTime? template)
    {
        if (template?.HasTime == false)
        {
            var date = instant.ToDateTimeUtc().Date;
            return new CalDateTime(date.Year, date.Month, date.Day);
        }

        return template?.TzId != null
            ? new CalDateTime(instant.ToDateTimeUtc(), template.TzId)
            : new CalDateTime(instant.ToDateTimeUtc(), true);
    }

    private static void AddExceptionDate(ICalEvent seriesMaster, Instant originalStartTime)
    {
        var exceptionDate = ToCalDateTime(originalStartTime, seriesMaster.Start);
        seriesMaster.ExceptionDates.Add(exceptionDate);
    }

    private static void RemoveExceptionDate(ICalEvent seriesMaster, Instant originalStartTime)
    {
        var exceptionDate = ToCalDateTime(originalStartTime, seriesMaster.Start);
        seriesMaster.ExceptionDates.Remove(exceptionDate);
    }

    private static string ResolveEventUrl(string? calendarId, string? eventId)
    {
        if (string.IsNullOrEmpty(eventId))
            throw new InvalidOperationException("Event ExternalId is null");

        if (eventId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            eventId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return eventId;
        }

        if (string.IsNullOrEmpty(calendarId))
            throw new InvalidOperationException("Calendar ExternalId is null");

        var uid = eventId.EndsWith(".ics") ? eventId[..^4] : eventId;
        return $"{TrimTrailingSlash(calendarId)}/{uid}.ics";
    }

    private static string TrimTrailingSlash(string url)
    {
        return url.EndsWith('/') ? url.TrimEnd('/') : url;
    }

    /// <inheritdoc/>
    public IList<object> GetSupportedExtensions() =>
    [
        CalendarEventExtensions.FullDay,
        CalendarEventExtensions.TimeZone,
        CalendarEventExtensions.Location,
        CalendarEventExtensions.Description,
        CalendarEventExtensions.Attachments
    ];

    /// <inheritdoc/>
    public async Task<IList<AttendeeFreeBusy>> GetFreeBusyAsync(
        string accountId,
        IList<string> attendeeEmails,
        Interval timeRange,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
            return attendeeEmails
                .Select(e => new AttendeeFreeBusy { Email = e, Status = FreeBusyStatus.Unknown })
                .ToList<AttendeeFreeBusy>();

        var organizerEmail = calDavCredentials.Username;

        // Build organizer's own busy slots from the local SQLite cache
        List<TimeSlot> organizerBusySlots;
        if (storage != null)
        {
            var dbEvents = await storage.GetEventsByTimeRangeAsync(timeRange);
            organizerBusySlots = dbEvents
                .Where(e => e.AccountId == accountId &&
                            e.StartTime.HasValue && e.EndTime.HasValue)
                .Select(e => new TimeSlot(
                    Instant.FromUnixTimeSeconds(e.StartTime!.Value),
                    Instant.FromUnixTimeSeconds(e.EndTime!.Value)))
                .OrderBy(s => s.Start)
                .ToList();
        }
        else
        {
            organizerBusySlots = [];
        }

        return await calDavService.GetFreeBusyAsync(
            calDavCredentials,
            accountId,
            organizerEmail,
            attendeeEmails,
            timeRange,
            organizerBusySlots,
            cancellationToken);
    }
}