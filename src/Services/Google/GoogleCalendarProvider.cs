using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using Ical.Net.DataTypes;
using perinma.Models;
using perinma.Utils;
using Calendar = Ical.Net.Calendar;

namespace perinma.Services.Google;

/// <summary>
/// Google Calendar implementation of ICalendarProvider.
/// </summary>
public class GoogleCalendarProvider(
    IGoogleCalendarService googleCalendarService,
    CredentialManagerService credentialManager)
    : ICalendarProvider
{
    public List<CalendarEvent> ParseCalendarEvents(List<RawEvent> rawEvents, TimeRange timeRange)
    {
        var googleEvents = rawEvents
            .Select(e => (e.Reference, Event: NewtonsoftJsonSerializer.Instance.Deserialize<Event>(e.RawData)))
            .Where(t => t.Event != null)
            .ToList();

        var overrides = googleEvents
            .Where(t => !string.IsNullOrEmpty(t.Event.RecurringEventId))
            .ToList();

        return googleEvents
            .Where(t => string.IsNullOrEmpty(t.Event.RecurringEventId)) // Main events (regular or master recurring)
            .SelectMany(t =>
            {
                if (t.Event.Recurrence is { Count: > 0 })
                {
                    // Generate occurrences for recurring events
                    return DetermineOccurrences(t.Event.Recurrence, timeRange)
                        .Where(occurrenceStart => !overrides.Any(ov =>
                            ov.Event.RecurringEventId == t.Event.Id &&
                            ParseGoogleDateTime(ov.Event.OriginalStartTime) == occurrenceStart))
                        .Select(occurrenceStart => MapToCalendarEvent(t.Reference, t.Event, occurrenceStart));
                }

                // Regular non-recurring event
                return [MapToCalendarEvent(t.Reference, t.Event, null)];
            })
            .Concat(overrides.Select(ov => MapToCalendarEvent(ov.Reference, ov.Event, null))) // Include the overrides themselves
            .Where(ce => ce.StartTime <= timeRange.End && ce.EndTime >= timeRange.Start)
            .ToList();
    }

    private CalendarEvent MapToCalendarEvent(EventReference reference, Event googleEvent,
        ZonedDateTime? occurrenceStart)
    {
        var start = occurrenceStart ?? ParseGoogleDateTime(googleEvent.Start) ?? default;

        // Calculate duration if it's an occurrence
        var duration = TimeSpan.Zero;
        if (googleEvent.Start != null && googleEvent.End != null)
        {
            var s = ParseGoogleDateTime(googleEvent.Start);
            var e = ParseGoogleDateTime(googleEvent.End);
            if (s.HasValue && e.HasValue) duration = e.Value - s.Value;
        }

        var end = ParseGoogleDateTime(googleEvent.End) ?? occurrenceStart?.Add(duration) ?? default;

        return new CalendarEvent
        {
            Reference = reference,
            Title = googleEvent.Summary,
            StartTime = start,
            EndTime = end,
            ChangedAt = googleEvent.UpdatedDateTimeOffset?.DateTime,
            ResponseStatus = MapResponseStatus(googleEvent.Status),
            Extensions = new ExtensionValues()
        };
    }

    private static EventResponseStatus MapResponseStatus(string? status) => status switch
    {
        "confirmed" => EventResponseStatus.Accepted,
        "tentative" => EventResponseStatus.Tentative,
        "cancelled" => EventResponseStatus.Declined,
        _ => EventResponseStatus.None
    };

    /// <inheritdoc/>
    public async Task<CalendarSyncResult> GetCalendarsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        // Create Google Calendar service (handles token refresh)
        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        // Fetch calendars from Google
        var result = await googleCalendarService.GetCalendarsAsync(service, syncToken, cancellationToken);

        // Convert to provider-agnostic format
        var calendars = result.Calendars.Select<CalendarListEntry, ProviderCalendar>(c => new ProviderCalendar
        {
            ExternalId = c.Id,
            Name = c.Summary ?? "Unnamed Calendar",
            Color = c.BackgroundColor,
            Selected = c.Selected == true,
            Deleted = c.Deleted == true,
            Data = new()
            {
                { "rawData", new DataAttribute.JsonText(NewtonsoftJsonSerializer.Instance.Serialize(c)) }
            }
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
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        // Create Google Calendar service (handles token refresh)
        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        // Fetch events from Google
        var result =
            await googleCalendarService.GetEventsAsync(service, calendarExternalId, syncToken, cancellationToken);

        // Convert to provider-agnostic format
        var events = new List<ProviderEvent>();

        foreach (var evt in result.Events)
        {
            var providerEvent = ConvertGoogleEvent(evt);
            if (providerEvent != null)
            {
                events.Add(providerEvent);
            }
        }

        return new EventSyncResult
        {
            Events = events,
            SyncToken = result.SyncToken
        };
    }

    /// <inheritdoc/>
    public async Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
            if (googleCredentials == null)
            {
                return false;
            }

            var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken);

            // Try to fetch calendar list as a connection test
            await googleCalendarService.GetCalendarsAsync(service, null, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Google Calendar connection test failed: {ex.Message}");
            return false;
        }
    }

    private static ProviderEvent? ConvertGoogleEvent(Event evt)
    {
        var isOverride = !string.IsNullOrEmpty(evt.RecurringEventId);

        // For non-override cancelled events, mark as deleted
        if (!isOverride && evt.Status == "cancelled")
        {
            return new ProviderEvent
            {
                ExternalId = evt.Id,
                Title = evt.Summary,
                Status = evt.Status,
                Deleted = true,
                RawData = NewtonsoftJsonSerializer.Instance.Serialize(evt)
            };
        }

        ZonedDateTime? startTime = null;
        ZonedDateTime? endTime = null;
        ZonedDateTime? originalStartTime = null;

        // Handle override events
        if (isOverride)
        {
            // Parse OriginalStartTime (when override replaces)
            originalStartTime = ParseGoogleDateTime(evt.OriginalStartTime);

            if (evt.Status == "cancelled")
            {
                // Cancelled override - use OriginalStartTime
                startTime = originalStartTime;
                endTime = originalStartTime;
            }
            else
            {
                // Modified override - parse actual start/end
                startTime = ParseGoogleDateTime(evt.Start);
                endTime = ParseGoogleDateTime(evt.End);

                // Ensure OriginalStartTime is within bounds
                if (originalStartTime.HasValue && startTime.HasValue && endTime.HasValue)
                {
                    if (originalStartTime.Value < startTime.Value)
                    {
                        startTime = originalStartTime;
                    }
                    else if (originalStartTime.Value > endTime.Value)
                    {
                        endTime = originalStartTime;
                    }
                }
            }
        }
        else
        {
            // Regular events
            if (evt.Start == null || evt.End == null)
            {
                return null;
            }

            startTime = ParseGoogleDateTime(evt.Start);
            endTime = ParseGoogleDateTime(evt.End);

            // For recurring events, calculate recurrence end time
            if (evt.Recurrence is { Count: > 0 } && startTime.HasValue && endTime.HasValue)
            {
                var recurrenceEndTime = RecurrenceParser.GetRecurrenceEndTime(
                    evt.Recurrence,
                    startTime.Value.DateTime,
                    endTime.Value.DateTime);

                if (recurrenceEndTime.HasValue)
                {
                    // TODO merge local recurrence calculations into the RecurrenceParser and
                    //   make it ZonedDateTime aware
                    endTime = new ZonedDateTime(recurrenceEndTime.Value, endTime.Value.TimeZone);
                }
            }
        }

        return new ProviderEvent
        {
            ExternalId = evt.Id,
            Title = evt.Summary ?? "Untitled Event",
            StartTime = startTime,
            EndTime = endTime,
            Status = evt.Status,
            Deleted = false,
            RecurringEventId = evt.RecurringEventId,
            OriginalStartTime = originalStartTime,
            RawData = NewtonsoftJsonSerializer.Instance.Serialize(evt)
        };
    }

    private static ZonedDateTime? ParseGoogleDateTime(EventDateTime? eventDateTime)
    {
        if (eventDateTime == null)
            return null;

        if (eventDateTime.DateTimeRaw != null && DateTime.TryParse(eventDateTime.DateTimeRaw, out var dateTime))
        {
            return new ZonedDateTime(dateTime, TimeZoneInfo.FindSystemTimeZoneById(eventDateTime.TimeZone));
        }

        if (eventDateTime.Date != null && DateTime.TryParse(eventDateTime.Date, out var date))
        {
            // Since it should still be the full day in our display, we have to consider it to be in our timezone.
            return new ZonedDateTime(date, TimeZoneInfo.Local);
        }

        return null;
    }

    /// <inheritdoc/>
    public ZonedDateTime? GetEventStartTime(
        string rawEventData,
        DateTime? occurrenceTime = null)
    {
        var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<Event>(rawEventData);
        if (googleEvent == null)
            return null;

        var isRecurring = googleEvent.Recurrence is { Count: > 0 };

        // For non-recurring events or when no occurrence time is specified, return base event start time
        if (!isRecurring || !occurrenceTime.HasValue)
            return ParseGoogleDateTime(googleEvent.Start);

        var occurrence = DetermineOccurrences(
                googleEvent.Recurrence,
                TimeRange.From(new ZonedDateTime(occurrenceTime.Value.ToUniversalTime(), TimeZoneInfo.Utc)),
                max: 1)
            .FirstOrDefault();

        if (occurrence == default)
            // Nothing found?! Well ...
            return ParseGoogleDateTime(googleEvent.Start);

        return occurrence;
    }

    /// <inheritdoc/>
    public IList<int> GetReminderMinutes(
        string rawEventData,
        string? rawCalendarData = null)
    {
        var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<Event>(rawEventData);
        if (googleEvent?.Reminders == null)
            return [];

        List<int> reminderMinutes = [];

        if (googleEvent.Reminders.UseDefault == true)
        {
            // Use default reminders from calendar
            if (!string.IsNullOrEmpty(rawCalendarData))
            {
                var calendarListEntry =
                    NewtonsoftJsonSerializer.Instance.Deserialize<CalendarListEntry>(rawCalendarData);
                if (calendarListEntry?.DefaultReminders != null)
                {
                    foreach (var reminder in calendarListEntry.DefaultReminders.Where(r =>
                                 r.Method == "popup" && r.Minutes.HasValue))
                    {
                        reminderMinutes.Add(reminder.Minutes!.Value);
                    }
                }
            }
        }
        else
        {
            // Use event-specific reminders
            if (googleEvent.Reminders.Overrides != null)
            {
                foreach (var reminder in googleEvent.Reminders.Overrides.Where(r =>
                             r.Method == "popup" && r.Minutes.HasValue))
                {
                    reminderMinutes.Add(reminder.Minutes!.Value);
                }
            }
        }

        return reminderMinutes;
    }

    /// <inheritdoc/>
    public IList<(ZonedDateTime Occurrence, ZonedDateTime TriggerTime)> GetNextReminderOccurrences(
        string rawEventData,
        string? rawCalendarData = null,
        ZonedDateTime referenceTime = default)
    {
        try
        {
            var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<Event>(rawEventData);
            if (googleEvent == null)
                return [];

            var reminderMinutes = GetReminderMinutes(rawEventData, rawCalendarData);
            if (reminderMinutes.Count == 0)
                return [];

            var eventStartTime = ParseGoogleDateTime(googleEvent.Start);
            if (!eventStartTime.HasValue)
                return [];

            var isRecurring = googleEvent.Recurrence is { Count: > 0 };
            var refTime = referenceTime == default
                ? new ZonedDateTime(DateTime.Now, TimeZoneInfo.Local)
                : referenceTime;
            var result = new List<(ZonedDateTime Occurrence, ZonedDateTime TriggerTime)>();

            if (isRecurring)
            {
                var nextOccurrence = DetermineOccurrences(googleEvent.Recurrence, TimeRange.From(refTime), max: 1)
                    .FirstOrDefault();
                if (nextOccurrence == default)
                    return [];

                foreach (var minutes in reminderMinutes)
                {
                    var triggerTime = nextOccurrence.AddMinutes(-minutes);
                    if (triggerTime > refTime)
                    {
                        result.Add((nextOccurrence, triggerTime));
                        break;
                    }
                }
            }
            else
            {
                foreach (var minutes in reminderMinutes)
                {
                    var triggerTime = eventStartTime.Value.AddMinutes(-minutes);
                    if (triggerTime > refTime)
                        result.Add((eventStartTime.Value, triggerTime));
                }
            }

            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static List<ZonedDateTime> DetermineOccurrences(IList<string>? recurrence, TimeRange timeRange,
        int max = Int32.MaxValue)
    {
        if (recurrence == null || recurrence.Count == 0)
        {
            return [];
        }

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("BEGIN:VEVENT");

        // Add DTSTART with timezone information
        if (timeRange.Start.DateTime > DateTime.MinValue)
            sb.AppendLine($"DTSTART;TZID={timeRange.Start.TimeZone}:{timeRange.Start.DateTime:yyyyMMdd'T'HHmmss}");

        foreach (var r in recurrence)
            sb.AppendLine(r);

        sb.AppendLine("END:VEVENT");
        sb.Append("END:VCALENDAR");

        var calendar = Calendar.Load(sb.ToString());
        var icalEvent = calendar?.Events.FirstOrDefault();

        if (icalEvent == null)
            throw new InvalidOperationException("failed to parse recurrence");

        var occurrences = icalEvent.GetOccurrences(
            new CalDateTime(timeRange.Start.DateTime, timeRange.Start.TimeZone.Id));

        return occurrences
            .Select(o =>
                new ZonedDateTime(o.Period.StartTime.AsUtc, TimeZoneInfo.Utc).ConvertTo(timeRange.Start.TimeZone))
            .TakeWhile(z => z.DateTime <= timeRange.End.DateTime)
            .Take(max)
            .ToList();
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
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        // Create Google Calendar service (handles token refresh)
        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        // Respond to the event using the service
        await googleCalendarService.RespondToEventAsync(service, calendarId, eventId, responseStatus,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> CreateEventAsync(
        string accountId,
        string calendarId,
        string title,
        string? description,
        string? location,
        ZonedDateTime startTime,
        ZonedDateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);
        return await googleCalendarService.CreateEventAsync(service, calendarId, title, description, location,
            startTime.DateTime, endTime.DateTime, rawEventData, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string title,
        string? description,
        string? location,
        ZonedDateTime startTime,
        ZonedDateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        var service = await googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);
        await googleCalendarService.UpdateEventAsync(service, calendarId, eventId, title, description, location,
            startTime.DateTime, endTime.DateTime, rawEventData, cancellationToken);
    }
}