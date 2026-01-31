using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using Ical.Net.DataTypes;
using perinma.Utils;
using Calendar = Ical.Net.Calendar;

namespace perinma.Services.Google;

/// <summary>
/// Google Calendar implementation of ICalendarProvider.
/// </summary>
public class GoogleCalendarProvider : ICalendarProvider
{
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly CredentialManagerService _credentialManager;

    public GoogleCalendarProvider(
        IGoogleCalendarService googleCalendarService,
        CredentialManagerService credentialManager)
    {
        _googleCalendarService = googleCalendarService;
        _credentialManager = credentialManager;
    }

    /// <inheritdoc/>
    public CredentialManagerService CredentialManager => _credentialManager;

    /// <inheritdoc/>
    public async Task<CalendarSyncResult> GetCalendarsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = _credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        // Create Google Calendar service (handles token refresh)
        var service = await _googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        // Fetch calendars from Google
        var result = await _googleCalendarService.GetCalendarsAsync(service, syncToken, cancellationToken);

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
                {"rawData", new DataAttribute.JsonText(NewtonsoftJsonSerializer.Instance.Serialize(c))}
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
        var googleCredentials = _credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        // Create Google Calendar service (handles token refresh)
        var service = await _googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        // Fetch events from Google
        var result = await _googleCalendarService.GetEventsAsync(service, calendarExternalId, syncToken, cancellationToken);

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
            var googleCredentials = _credentialManager.GetGoogleCredentials(accountId);
            if (googleCredentials == null)
            {
                return false;
            }

            var service = await _googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken);

            // Try to fetch calendar list as a connection test
            await _googleCalendarService.GetCalendarsAsync(service, null, cancellationToken);
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

        DateTime? startTime = null;
        DateTime? endTime = null;
        DateTime? originalStartTime = null;

        // Handle override events
        if (isOverride)
        {
            // Parse OriginalStartTime (when the override replaces)
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
                    startTime.Value,
                    endTime.Value);

                if (recurrenceEndTime.HasValue)
                {
                    endTime = recurrenceEndTime.Value;
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

    private static DateTime? ParseGoogleDateTime(EventDateTime? eventDateTime)
    {
        if (eventDateTime == null)
            return null;

        if (eventDateTime.DateTimeRaw != null && DateTime.TryParse(eventDateTime.DateTimeRaw, out var dateTime))
        {
            return dateTime;
        }

        if (eventDateTime.Date != null && DateTime.TryParse(eventDateTime.Date, out var date))
        {
            return date;
        }

        return null;
    }

    /// <inheritdoc/>
     public Task<DateTimeOffset?> GetEventStartTimeAsync(
         string rawEventData,
         DateTime? occurrenceTime = null,
         CancellationToken cancellationToken = default)
     {
         try
         {
             var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<Event>(rawEventData);
             if (googleEvent == null)
             {
                 return Task.FromResult<DateTimeOffset?>(null);
             }

             var isRecurring = googleEvent.Recurrence is { Count: > 0 };

             // For non-recurring events or when no occurrence time is specified, return base event start time
             if (!isRecurring || !occurrenceTime.HasValue)
             {
                 return Task.FromResult<DateTimeOffset?>(ParseGoogleDateTimeWithTimezone(googleEvent.Start));
             }

             // For recurring events with a specific occurrence time, find matching occurrence
             var icalString = BuildIcalString(googleEvent.Recurrence);
             if (string.IsNullOrEmpty(icalString))
             {
                 return Task.FromResult<DateTimeOffset?>(ParseGoogleDateTimeWithTimezone(googleEvent.Start));
             }

             var calendar = Calendar.Load(icalString);
             var icalEvent = calendar?.Events.FirstOrDefault();

             if (icalEvent == null)
             {
                 return Task.FromResult<DateTimeOffset?>(ParseGoogleDateTimeWithTimezone(googleEvent.Start));
             }

             var occurrences = icalEvent.GetOccurrences(startTime: new CalDateTime(occurrenceTime.Value.ToUniversalTime()));

             var firstOccurrence = occurrences.FirstOrDefault();
             if (firstOccurrence != null)
             {
                 var firstOccurrenceTime = firstOccurrence.Period.StartTime.AsUtc;
                 return Task.FromResult<DateTimeOffset?>(new DateTimeOffset(firstOccurrenceTime));
             }

             // Fallback to base event start time
             return Task.FromResult<DateTimeOffset?>(ParseGoogleDateTimeWithTimezone(googleEvent.Start));
         }
         catch (Exception)
         {
             return Task.FromResult<DateTimeOffset?>(null);
         }
     }

    private static DateTimeOffset? ParseGoogleDateTimeWithTimezone(EventDateTime? eventDateTime)
    {
        if (eventDateTime == null)
            return null;

        if (eventDateTime.DateTimeRaw != null && DateTimeOffset.TryParse(eventDateTime.DateTimeRaw, out var dateTimeOffset))
        {
            return dateTimeOffset;
        }

        if (eventDateTime.Date != null && DateTimeOffset.TryParse(eventDateTime.Date, out var dateOffset))
        {
            return dateOffset;
        }

        return null;
    }

    /// <inheritdoc/>
    public Task<IList<int>> GetReminderMinutesAsync(
        string rawEventData,
        string? rawCalendarData = null,
        CancellationToken cancellationToken = default)
    {
        var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<Event>(rawEventData);
        if (googleEvent?.Reminders == null)
        {
            return Task.FromResult<IList<int>>([]);
        }

        List<int> reminderMinutes = [];

        if (googleEvent.Reminders.UseDefault == true)
        {
            // Use default reminders from calendar
            if (!string.IsNullOrEmpty(rawCalendarData))
            {
                var calendarListEntry = NewtonsoftJsonSerializer.Instance.Deserialize<CalendarListEntry>(rawCalendarData);
                if (calendarListEntry?.DefaultReminders != null)
                {
                    foreach (var reminder in calendarListEntry.DefaultReminders.Where(r => r.Method == "popup" && r.Minutes.HasValue))
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
                foreach (var reminder in googleEvent.Reminders.Overrides.Where(r => r.Method == "popup" && r.Minutes.HasValue))
                {
                    reminderMinutes.Add(reminder.Minutes!.Value);
                }
            }
        }

        return Task.FromResult<IList<int>>(reminderMinutes);
    }

    /// <inheritdoc/>
    public async Task<IList<(DateTime Occurrence, DateTime TriggerTime)>> GetNextReminderOccurrencesAsync(
        string rawEventData,
        string? rawCalendarData = null,
        DateTime referenceTime = default,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<Event>(rawEventData);
            if (googleEvent == null)
            {
                return [];
            }

            var reminderMinutes = await GetReminderMinutesAsync(rawEventData, rawCalendarData, cancellationToken);
            if (reminderMinutes.Count == 0)
            {
                return [];
            }

            var eventStartTime = ParseGoogleDateTime(googleEvent.Start);
            if (!eventStartTime.HasValue)
            {
                return [];
            }

            var isRecurring = googleEvent.Recurrence is { Count: > 0 };
            var startTime = referenceTime == default ? DateTime.UtcNow : referenceTime;
            var result = new List<(DateTime Occurrence, DateTime TriggerTime)>();

            if (isRecurring)
            {
                var icalString = BuildIcalString(googleEvent.Recurrence);
                if (string.IsNullOrEmpty(icalString))
                {
                    return [];
                }

                var calendar = Calendar.Load(icalString);
                var icalEvent = calendar?.Events.FirstOrDefault();

                if (icalEvent != null)
                {
                    var occurrences = icalEvent.GetOccurrences(startTime: new CalDateTime(startTime));
                    var nextOccurrence = occurrences.FirstOrDefault();
                    if (nextOccurrence == null)
                    {
                        return [];
                    }

                    var occurrenceTime = nextOccurrence.Period.StartTime.AsUtc;

                    foreach (var minutes in reminderMinutes)
                    {
                        var triggerTime = occurrenceTime.AddMinutes(-minutes);
                        if (triggerTime > startTime)
                        {
                            result.Add((occurrenceTime, triggerTime));
                            break;
                        }
                    }
                }
            }
            else
            {
                foreach (var minutes in reminderMinutes)
                {
                    var triggerTime = eventStartTime.Value.AddMinutes(-minutes);
                    if (triggerTime > startTime)
                    {
                        result.Add((eventStartTime.Value, triggerTime));
                    }
                }
            }

            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string BuildIcalString(IList<string>? recurrence)
    {
        if (recurrence == null || recurrence.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("BEGIN:VEVENT");

        foreach (var r in recurrence)
        {
            sb.AppendLine(r);
        }

        sb.AppendLine("END:VEVENT");
        sb.Append("END:VCALENDAR");

        return sb.ToString();
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
        var googleCredentials = _credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        // Create Google Calendar service (handles token refresh)
        var service = await _googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        // Respond to the event using the service
        await _googleCalendarService.RespondToEventAsync(service, calendarId, eventId, responseStatus, cancellationToken);
    }
}
