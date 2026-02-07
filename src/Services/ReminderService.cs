using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using perinma.Models;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Services;

public class ReminderService(SqliteStorage storage, IReadOnlyDictionary<AccountType, ICalendarProvider> providers)
{
    private readonly HashSet<string> _firedReminders = new();

    public async Task PopulateRemindersForEventAsync(string eventId, string calendarId, AccountType accountType,
        CancellationToken cancellationToken = default,
        ZonedDateTime referenceTime = default)
    {
        var rawData = await storage.GetEventData(eventId, "rawData");

        if (string.IsNullOrEmpty(rawData))
        {
            return;
        }

        // Get the appropriate provider
        if (!providers.TryGetValue(accountType, out var provider))
        {
            return;
        }

        // Get raw calendar data for default reminders (Google uses this)
        string? rawCalendarData = null;
        var calendar = await storage.GetCalendarByIdAsync(calendarId);
        if (calendar != null)
        {
            rawCalendarData = await storage.GetCalendarDataAsync(calendar.CalendarId, "rawData");
        }

        var refTime = referenceTime == default 
            ? new ZonedDateTime(DateTime.UtcNow, TimeZoneInfo.Utc) 
            : referenceTime;

        // Get reminder occurrences from the provider
        var reminderOccurrences =
            provider.GetNextReminderOccurrences(rawData, rawCalendarData, refTime);

        if (reminderOccurrences.Count == 0)
        {
            return;
        }

        var existingReminders = await storage.GetRemindersByEventAsync(eventId);

        var remindersToDelete = existingReminders
            .Where(r => reminderOccurrences.All(o =>
                o.TriggerTime.ToDateTimeOffset().ToUnixTimeSeconds() != r.TriggerTime))
            .Select(r => r.ReminderId)
            .ToList();

        await storage.DeleteRemindersAsync(remindersToDelete);

        foreach ((ZonedDateTime occurrence, ZonedDateTime triggerTime) in reminderOccurrences)
        {
            if (existingReminders.All(r => r.TriggerTime != triggerTime.ToDateTimeOffset().ToUnixTimeSeconds()))
            {
                await storage.CreateReminderAsync(eventId, occurrence.DateTime, triggerTime.DateTime);
            }
        }
    }

    public async Task<List<ReminderWithEvent>> GetDueRemindersAsync(CancellationToken cancellationToken = default)
    {
        var reminders = await storage.GetDueRemindersAsync(_firedReminders);

        foreach (var reminder in reminders)
        {
            _firedReminders.Add(reminder.ReminderId);
        }

        return reminders;
    }

    public async Task DismissReminderAsync(string reminderId, CancellationToken cancellationToken = default)
    {
        _firedReminders.Remove(reminderId);
        await HandleDismissAsync(reminderId, cancellationToken);
    }

    public async Task SnoozeReminderAsync(string reminderId, SnoozeInterval interval,
        CancellationToken cancellationToken = default)
    {
        _firedReminders.Remove(reminderId);
        await HandleSnoozeAsync(reminderId, interval, cancellationToken);
    }

    private async Task HandleDismissAsync(string reminderId, CancellationToken cancellationToken)
    {
        var reminder = await storage.GetReminderAsync(reminderId);
        if (reminder == null)
            return;

        // Prepare follow-up reminder using trigger time as reference to get next recurrence
        var calendarId = await storage.GetEventCalendarIdAsync(reminder.TargetId);
        if (!string.IsNullOrEmpty(calendarId))
        {
            var calendar = storage.GetCachedCalendar(new Guid(calendarId));
            if (calendar != null)
            {
                var previousTargetTime = DateTimeOffset.FromUnixTimeSeconds(reminder.TargetTime).DateTime;
                var previousTargetTimeZoned = new ZonedDateTime(previousTargetTime, TimeZoneInfo.Local);
                await PopulateRemindersForEventAsync(reminder.TargetId, calendarId, calendar.Account.Type,
                    cancellationToken, previousTargetTimeZoned);
            }
        }

        await storage.DeleteReminderAsync(reminderId);
    }

    private async Task HandleSnoozeAsync(string reminderId, SnoozeInterval interval,
        CancellationToken cancellationToken)
    {
        var reminder = await storage.GetReminderAsync(reminderId);
        if (reminder == null)
        {
            return;
        }

        await storage.DeleteReminderAsync(reminderId);

        var targetTime = DateTimeOffset.FromUnixTimeMilliseconds(reminder.TargetTime).DateTime;
        var targetTimeZoned = new ZonedDateTime(targetTime, TimeZoneInfo.Local);

        ZonedDateTime newTriggerTimeZoned;
        if (interval == SnoozeInterval.WhenItStarts || interval == SnoozeInterval.OneMinuteBeforeStart)
        {
            if (reminder.TargetType != 1)
            {
                // Not a calendar reminder; nothing to be done
                return;
            }

            var calendarId = await storage.GetEventCalendarIdAsync(reminder.TargetId);
            if (calendarId == null) return;

            var calendar = storage.GetCachedCalendar(new Guid(calendarId));
            if (calendar == null) return;

            var occurrenceStartTimeZoned = await GetEventStartTimeAsync(reminder.TargetId,
                targetTimeZoned,
                calendar.Account.Type, cancellationToken);
            if (occurrenceStartTimeZoned == null) return;

            newTriggerTimeZoned = CalculateSnoozeTime(occurrenceStartTimeZoned.Value, interval);
        }
        else
        {
            newTriggerTimeZoned = CalculateSnoozeTime(targetTimeZoned, interval);
        }

        await storage.CreateReminderAsync(reminder.TargetId, targetTimeZoned.DateTime, newTriggerTimeZoned.DateTime);
    }

    private ZonedDateTime CalculateSnoozeTime(ZonedDateTime targetTime, SnoozeInterval interval)
    {
        return interval switch
        {
            SnoozeInterval.OneMinute => new ZonedDateTime(DateTime.UtcNow.AddMinutes(1), TimeZoneInfo.Utc),
            SnoozeInterval.FiveMinutes => new ZonedDateTime(DateTime.UtcNow.AddMinutes(5), TimeZoneInfo.Utc),
            SnoozeInterval.TenMinutes => new ZonedDateTime(DateTime.UtcNow.AddMinutes(10), TimeZoneInfo.Utc),
            SnoozeInterval.FifteenMinutes => new ZonedDateTime(DateTime.UtcNow.AddMinutes(15), TimeZoneInfo.Utc),
            SnoozeInterval.ThirtyMinutes => new ZonedDateTime(DateTime.UtcNow.AddMinutes(30), TimeZoneInfo.Utc),
            SnoozeInterval.OneHour => new ZonedDateTime(DateTime.UtcNow.AddHours(1), TimeZoneInfo.Utc),
            SnoozeInterval.TwoHours => new ZonedDateTime(DateTime.UtcNow.AddHours(2), TimeZoneInfo.Utc),
            SnoozeInterval.Tomorrow => new ZonedDateTime(DateTime.UtcNow.AddDays(1).Date, TimeZoneInfo.Utc),
            SnoozeInterval.OneMinuteBeforeStart => new ZonedDateTime(targetTime.DateTime.AddMinutes(-1), targetTime.TimeZone),
            SnoozeInterval.WhenItStarts => targetTime,
            _ => new ZonedDateTime(DateTime.UtcNow.AddMinutes(5), TimeZoneInfo.Utc)
        };
    }

    public void ClearFiredReminders()
    {
        _firedReminders.Clear();
    }

    public async Task<ZonedDateTime?> GetEventStartTimeAsync(string eventId, ZonedDateTime occurrenceTime,
        AccountType accountType, CancellationToken cancellationToken = default)
    {
        var rawData = await storage.GetEventData(eventId, "rawData");

        if (string.IsNullOrEmpty(rawData))
        {
            return null;
        }

        if (!providers.TryGetValue(accountType, out var provider))
        {
            return null;
        }

        var startTimeOffset = provider.GetEventStartTime(rawData, occurrenceTime.DateTime);
        if (startTimeOffset.HasValue)
        {
            return new ZonedDateTime(startTimeOffset.Value.DateTime, occurrenceTime.TimeZone);
        }

        return null;
    }

    public async Task<RemindersRebuildResult> RebuildAllRemindersAsync(CancellationToken cancellationToken = default)
    {
        var result = new RemindersRebuildResult();

        await storage.DeleteAllRemindersAsync();

        var accounts = (await storage.GetAllAccountsAsync()).ToList();

        foreach (var account in accounts)
        {
            var calendars = storage.GetCachedCalendars(new Models.Account
            {
                Id = Guid.Parse(account.AccountId),
                Name = account.Name,
                Type = account.AccountTypeEnum
            }).Where(c => c.Enabled).ToList();

            result.TotalCalendars = calendars.Count;

            foreach (var calendar in calendars)
            {
                var events = (await storage.GetEventsByCalendarAsync(calendar.Id.ToString())).ToList();
                result.TotalEvents += events.Count;

                foreach (var evt in events)
                {
                    try
                    {
                        await PopulateRemindersForEventAsync(evt.EventId, calendar.Id.ToString(), calendar.Account.Type,
                            cancellationToken);
                        result.EventsProcessed++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Event {evt.EventId} in calendar {calendar.Name}: {ex.Message}");
                    }
                }
            }
        }

        return result;
    }
}

public class RemindersRebuildResult
{
    public int TotalCalendars { get; set; }
    public int TotalEvents { get; set; }
    public int EventsProcessed { get; set; }
    public List<string> Errors { get; set; } = new();
}
