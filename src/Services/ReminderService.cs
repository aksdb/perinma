using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NodaTime;
using perinma.Models;
using perinma.Storage;
using perinma.Storage.Models;
using perinma.Utils;

namespace perinma.Services;

public class ReminderService(SqliteStorage storage, IReadOnlyDictionary<AccountType, ICalendarProvider> providers, IClock? clock = null)
{
    private readonly IClock _clock = clock ?? SystemClock.Instance;
    private readonly HashSet<string> _firedReminders = new();

    public async Task PopulateRemindersForEventAsync(string eventId, string calendarId, AccountType accountType,
        CancellationToken cancellationToken = default,
        Instant referenceTime = default)
    {
        var rawData = await storage.GetEventData(eventId, "rawData");

        if (string.IsNullOrEmpty(rawData))
        {
            return;
        }

        // Get appropriate provider
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
            ? _clock.GetCurrentInstant()
            : referenceTime;

        // Get reminder occurrences from provider
        var reminderOccurrences =
            provider.GetNextReminderOccurrences(rawData, rawCalendarData, refTime);

        if (reminderOccurrences.Count == 0)
        {
            return;
        }

        var existingReminders = await storage.GetRemindersByEventAsync(eventId);

        var remindersToDelete = existingReminders
            .Where(r => reminderOccurrences.All(o =>
                o.TriggerTime.ToUnixTimeSeconds() != r.TriggerTime))
            .Select(r => r.ReminderId)
            .ToList();

        await storage.DeleteRemindersAsync(remindersToDelete);

        foreach ((Instant occurrence, Instant triggerTime) in reminderOccurrences)
        {
            if (existingReminders.All(r => r.TriggerTime != triggerTime.ToUnixTimeSeconds()))
            {
                await storage.CreateReminderAsync(eventId, occurrence.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
            }
        }
    }

    public async Task<List<ReminderWithEvent>> GetDueRemindersAsync(CancellationToken cancellationToken = default)
    {
        var referenceTime = _clock.GetCurrentInstant().ToUnixTimeSeconds();
        var reminders = await storage.GetDueRemindersAsync(_firedReminders, referenceTime);

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
        var calendar = calendarId?.Let(id =>storage.GetCachedCalendar(new Guid(calendarId)));
        if (calendar != null)
        {
            var previousTargetTime = Instant.FromUnixTimeSeconds(reminder.TargetTime);
            await PopulateRemindersForEventAsync(reminder.TargetId, calendarId!, calendar.Account.Type,
                cancellationToken, previousTargetTime.Plus(Duration.FromSeconds(1)));
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

        var targetInstant = Instant.FromUnixTimeSeconds(reminder.TargetTime);

        Instant newTriggerInstant;
        if (interval is SnoozeInterval.WhenItStarts or SnoozeInterval.OneMinuteBeforeStart)
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

            var occurrenceStartTimeInstant = await GetEventStartTimeAsync(reminder.TargetId,
                targetInstant,
                calendar.Account.Type, cancellationToken);
            if (occurrenceStartTimeInstant == null) return;

            newTriggerInstant = CalculateSnoozeTime(occurrenceStartTimeInstant.Value, interval);
        }
        else
        {
            newTriggerInstant = CalculateSnoozeTime(targetInstant, interval);
        }

        await storage.CreateReminderAsync(reminder.TargetId, targetInstant.ToDateTimeUtc(), newTriggerInstant.ToDateTimeUtc());
    }

    private Instant CalculateSnoozeTime(Instant targetTime, SnoozeInterval interval)
    {
        return interval switch
        {
            SnoozeInterval.OneMinute => _clock.GetCurrentInstant() + Duration.FromMinutes(1),
            SnoozeInterval.FiveMinutes => _clock.GetCurrentInstant() + Duration.FromMinutes(5),
            SnoozeInterval.TenMinutes => _clock.GetCurrentInstant() + Duration.FromMinutes(10),
            SnoozeInterval.FifteenMinutes => _clock.GetCurrentInstant() + Duration.FromMinutes(15),
            SnoozeInterval.ThirtyMinutes => _clock.GetCurrentInstant() + Duration.FromMinutes(30),
            SnoozeInterval.OneHour => _clock.GetCurrentInstant() + Duration.FromHours(1),
            SnoozeInterval.TwoHours => _clock.GetCurrentInstant() + Duration.FromHours(2),
            SnoozeInterval.Tomorrow => _clock.GetCurrentInstant().InUtc().Date.PlusDays(1).AtStartOfDayInZone(DateTimeZone.Utc).ToInstant(),
            SnoozeInterval.OneMinuteBeforeStart => targetTime - Duration.FromMinutes(1),
            SnoozeInterval.WhenItStarts => targetTime,
            _ => _clock.GetCurrentInstant() + Duration.FromMinutes(5)
        };
    }

    public void ClearFiredReminders()
    {
        _firedReminders.Clear();
    }

    public async Task<Instant?> GetEventStartTimeAsync(string eventId, Instant occurrenceTime,
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

        return provider.GetEventStartTime(rawData, occurrenceTime);
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
