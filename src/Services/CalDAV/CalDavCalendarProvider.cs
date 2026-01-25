using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using perinma.Storage.Models;
using perinma.Utils;

namespace perinma.Services.CalDAV;

/// <summary>
/// CalDAV implementation of ICalendarProvider.
/// </summary>
public class CalDavCalendarProvider : ICalendarProvider
{
    private readonly ICalDavService _calDavService;
    private readonly CredentialManagerService _credentialManager;

    public CalDavCalendarProvider(
        ICalDavService calDavService,
        CredentialManagerService credentialManager)
    {
        _calDavService = calDavService;
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
        var calDavCredentials = _credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
        {
            throw new InvalidOperationException($"No CalDAV credentials found for account {accountId}");
        }

        // Fetch calendars from CalDAV server
        var result = await _calDavService.GetCalendarsAsync(calDavCredentials, syncToken, cancellationToken);

        // Convert to provider-agnostic format
        var calendars = result.Calendars.Select(c => new ProviderCalendar
        {
            ExternalId = c.Url,
            Name = c.DisplayName,
            Color = c.Color,
            Selected = true, // CalDAV doesn't have a "selected" concept, default to enabled
            Deleted = c.Deleted,
            RawData = null // CalDAV doesn't have additional raw data to store for calendars
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
        var calDavCredentials = _credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
        {
            throw new InvalidOperationException($"No CalDAV credentials found for account {accountId}");
        }

        // Fetch events from CalDAV server
        var result = await _calDavService.GetEventsAsync(calDavCredentials, calendarExternalId, syncToken, cancellationToken);

        // Convert to provider-agnostic format
        var events = new List<ProviderEvent>();

        foreach (var evt in result.Events)
        {
            var providerEvent = ConvertCalDavEvent(evt);
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
    public Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = _credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
        {
            return Task.FromResult(false);
        }
        return _calDavService.TestConnectionAsync(calDavCredentials, cancellationToken);
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

        DateTime? endTime = evt.EndTime;

        // Parse recurrence end time from raw iCalendar data if present
        if (!string.IsNullOrEmpty(evt.RawICalendar))
        {
            var recurrenceEndTime = ParseCalDavRecurrenceEndTime(evt.RawICalendar);
            if (recurrenceEndTime.HasValue)
            {
                endTime = recurrenceEndTime.Value;
            }
        }

        return new ProviderEvent
        {
            ExternalId = evt.Uid,
            Title = evt.Summary ?? "Untitled Event",
            StartTime = evt.StartTime,
            EndTime = endTime,
            Status = evt.Status,
            Deleted = false,
            RecurringEventId = null, // CalDAV handles recurrence differently
            OriginalStartTime = null,
            RawData = evt.RawICalendar
        };
    }

    /// <summary>
    /// Parses recurrence end time from raw iCalendar data.
    /// </summary>
    private static DateTime? ParseCalDavRecurrenceEndTime(string rawICalendar)
    {
        try
        {
            var calendar = Ical.Net.Calendar.Load(rawICalendar);
            var calendarEvent = calendar?.Events.FirstOrDefault();

            if (calendarEvent != null)
            {
                return RecurrenceParser.CalculateRecurrenceEndTime(calendarEvent);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing CalDAV recurrence: {ex.Message}");
        }

        return null;
    }

    /// <inheritdoc/>
    public Task<IList<int>> GetReminderMinutesAsync(
        string rawEventData,
        string? rawCalendarData = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var calendar = Ical.Net.Calendar.Load(rawEventData);
            var evt = calendar?.Events.FirstOrDefault();
            if (evt == null)
            {
                return Task.FromResult<IList<int>>([]);
            }

            var alarms = evt.Alarms;
            if (alarms == null || alarms.Count == 0)
            {
                return Task.FromResult<IList<int>>([]);
            }

            List<int> reminderMinutes = [];

            foreach (var alarm in alarms)
            {
                if (alarm.Trigger?.IsRelative != true || !alarm.Trigger.Duration.HasValue)
                {
                    continue;
                }

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

            return Task.FromResult<IList<int>>(reminderMinutes);
        }
        catch (Exception)
        {
            return Task.FromResult<IList<int>>([]);
        }
    }

    /// <inheritdoc/>
    public async Task<IList<(DateTime Occurrence, DateTime TriggerTime)>> GetReminderOccurrencesAsync(
        string rawEventData,
        string? rawCalendarData = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var calendar = Ical.Net.Calendar.Load(rawEventData);
            var evt = calendar?.Events.FirstOrDefault();
            if (evt == null)
            {
                return [];
            }

            var reminderMinutes = await GetReminderMinutesAsync(rawEventData, rawCalendarData, cancellationToken);
            if (reminderMinutes.Count == 0)
            {
                return [];
            }

            var eventStartTime = evt.Start?.AsUtc;
            if (!eventStartTime.HasValue)
            {
                return [];
            }

            var isRecurring = evt.RecurrenceRules.Count > 0;
            var now = DateTime.UtcNow;
            var result = new List<(DateTime Occurrence, DateTime TriggerTime)>();

            if (isRecurring)
            {
                var recurrenceEndTime = RecurrenceParser.CalculateRecurrenceEndTime(evt);
                var searchEnd = recurrenceEndTime ?? now.AddYears(10);

                foreach (var occurrence in evt.GetOccurrences())
                {
                    var occurrenceTime = occurrence.Period.StartTime.AsUtc;
                    if (occurrenceTime < now || occurrenceTime > searchEnd)
                    {
                        continue;
                    }

                    foreach (var minutes in reminderMinutes)
                    {
                        var triggerTime = occurrenceTime.AddMinutes(-minutes);
                        if (triggerTime > now)
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
                    if (triggerTime > now)
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

    /// <inheritdoc/>
    public async Task RespondToEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string rawEventData,
        string responseStatus,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = _credentialManager.GetCalDavCredentials(accountId);
        if (calDavCredentials == null)
        {
            throw new InvalidOperationException($"No CalDAV credentials found for account {accountId}");
        }

        // For CalDAV, we need the user's email (stored in Username)
        var userEmail = calDavCredentials.Username;

        // Respond to the event using the service
        await _calDavService.RespondToEventAsync(
            calDavCredentials,
            eventId, // eventId is the event URL for CalDAV
            rawEventData,
            responseStatus,
            userEmail,
            cancellationToken);
    }
}
