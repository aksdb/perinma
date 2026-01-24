using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using perinma.Storage.Models;
using perinma.Utils;

namespace perinma.Services.Google;

/// <summary>
/// Google Calendar implementation of ICalendarProvider.
/// </summary>
public class GoogleCalendarProvider : ICalendarProvider
{
    private readonly IGoogleCalendarService _googleCalendarService;

    public GoogleCalendarProvider(IGoogleCalendarService googleCalendarService)
    {
        _googleCalendarService = googleCalendarService;
    }

    /// <inheritdoc/>
    public async Task<CalendarSyncResult> GetCalendarsAsync(
        AccountCredentials credentials,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = ValidateCredentials(credentials);

        // Create Google Calendar service (handles token refresh)
        var service = await _googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken);

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
            RawData = NewtonsoftJsonSerializer.Instance.Serialize(c)
        }).ToList();

        return new CalendarSyncResult
        {
            Calendars = calendars,
            SyncToken = result.SyncToken
        };
    }

    /// <inheritdoc/>
    public async Task<EventSyncResult> GetEventsAsync(
        AccountCredentials credentials,
        string calendarExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = ValidateCredentials(credentials);

        // Create Google Calendar service (handles token refresh)
        var service = await _googleCalendarService.CreateServiceAsync(googleCredentials, cancellationToken);

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
        AccountCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var googleCredentials = ValidateCredentials(credentials);
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

    private static GoogleCredentials ValidateCredentials(AccountCredentials credentials)
    {
        if (credentials is not GoogleCredentials googleCredentials)
        {
            throw new InvalidOperationException(
                $"GoogleCalendarProvider requires GoogleCredentials, but received {credentials.GetType().Name}");
        }
        return googleCredentials;
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
}
