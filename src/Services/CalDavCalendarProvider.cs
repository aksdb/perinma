using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using perinma.Storage.Models;
using perinma.Utils;

namespace perinma.Services;

/// <summary>
/// CalDAV implementation of ICalendarProvider.
/// </summary>
public class CalDavCalendarProvider : ICalendarProvider
{
    private readonly ICalDavService _calDavService;

    public CalDavCalendarProvider(ICalDavService calDavService)
    {
        _calDavService = calDavService;
    }

    /// <inheritdoc/>
    public async Task<CalendarSyncResult> GetCalendarsAsync(
        AccountCredentials credentials,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = ValidateCredentials(credentials);

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
        AccountCredentials credentials,
        string calendarExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = ValidateCredentials(credentials);

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
        AccountCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var calDavCredentials = ValidateCredentials(credentials);
        return _calDavService.TestConnectionAsync(calDavCredentials, cancellationToken);
    }

    private static CalDavCredentials ValidateCredentials(AccountCredentials credentials)
    {
        if (credentials is not CalDavCredentials calDavCredentials)
        {
            throw new InvalidOperationException(
                $"CalDavCalendarProvider requires CalDavCredentials, but received {credentials.GetType().Name}");
        }
        return calDavCredentials;
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
                if (alarm.Trigger == null)
                {
                    continue;
                }

                if (alarm.Trigger.IsRelative)
                {
                    var duration = alarm.Trigger.Duration;
                    if (duration.HasValue)
                    {
                        var weeks = duration.Value.Weeks ?? 0;
                        var days = duration.Value.Days ?? 0;
                        var hours = duration.Value.Hours ?? 0;
                        var minutes = duration.Value.Minutes ?? 0;
                        var totalMinutes = (int)(-(weeks * 7 * 24 * 60 + days * 24 * 60 + hours * 60 + minutes) * duration.Value.Sign);
                        if (totalMinutes > 0)
                        {
                            reminderMinutes.Add(totalMinutes);
                        }
                    }
                }
            }

            return Task.FromResult<IList<int>>(reminderMinutes);
        }
        catch (Exception)
        {
            return Task.FromResult<IList<int>>([]);
        }
    }
}
