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

    public async Task PopulateRemindersForEventAsync(string eventId, string calendarId, AccountType accountType, CancellationToken cancellationToken = default)
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
            rawCalendarData = await storage.GetCalendarData(calendar, "rawData");
        }

        // Get reminder occurrences from the provider
        var reminderOccurrences = await provider.GetReminderOccurrencesAsync(rawData, rawCalendarData, cancellationToken);

        if (reminderOccurrences.Count == 0)
        {
            return;
        }

        var existingReminders = await storage.GetRemindersByEventAsync(eventId);

        var remindersToDelete = existingReminders
            .Where(r => reminderOccurrences.All(o => o.TriggerTime != DateTimeOffset.FromUnixTimeSeconds(r.TriggerTime).DateTime))
            .Select(r => r.ReminderId)
            .ToList();

        await storage.DeleteRemindersAsync(remindersToDelete);

        foreach (var (occurrence, triggerTime) in reminderOccurrences)
        {
            if (existingReminders.All(r => r.TriggerTime != new DateTimeOffset(triggerTime).ToUnixTimeSeconds()))
            {
                await storage.CreateReminderAsync(eventId, occurrence, triggerTime);
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

    public async Task SnoozeReminderAsync(string reminderId, SnoozeInterval interval, CancellationToken cancellationToken = default)
    {
        _firedReminders.Remove(reminderId);
        await HandleSnoozeAsync(reminderId, interval, cancellationToken);
    }

    private async Task HandleDismissAsync(string reminderId, CancellationToken cancellationToken)
    {
        var reminder = await storage.GetReminderAsync(reminderId);

        if (reminder == null)
        {
            return;
        }

        // Prepare follow-up reminder.
        var calendarId = await storage.GetEventCalendarIdAsync(reminder.TargetId);
        if (!string.IsNullOrEmpty(calendarId))
        {
            var accountType = await storage.GetAccountTypeForCalendarAsync(calendarId);
            if (accountType.HasValue)
            {
                await PopulateRemindersForEventAsync(reminder.TargetId, calendarId, accountType.Value, cancellationToken);
            }
        }

        await storage.DeleteReminderAsync(reminderId);
    }

    private async Task HandleSnoozeAsync(string reminderId, SnoozeInterval interval, CancellationToken cancellationToken)
    {
        var reminder = await storage.GetReminderAsync(reminderId);

        if (reminder == null)
        {
            return;
        }

        var newTriggerTime = CalculateSnoozeTime(reminder.TargetTime, interval);
        await storage.CreateReminderAsync(reminder.TargetId, DateTimeOffset.FromUnixTimeSeconds(reminder.TargetTime).DateTime, newTriggerTime);
        await storage.DeleteReminderAsync(reminderId);
    }

    private DateTime CalculateSnoozeTime(long targetTimeUnix, SnoozeInterval interval)
    {
        var targetTime = DateTimeOffset.FromUnixTimeSeconds(targetTimeUnix).DateTime;

        return interval switch
        {
            SnoozeInterval.OneMinute => DateTime.UtcNow.AddMinutes(1),
            SnoozeInterval.FiveMinutes => DateTime.UtcNow.AddMinutes(5),
            SnoozeInterval.TenMinutes => DateTime.UtcNow.AddMinutes(10),
            SnoozeInterval.FifteenMinutes => DateTime.UtcNow.AddMinutes(15),
            SnoozeInterval.ThirtyMinutes => DateTime.UtcNow.AddMinutes(30),
            SnoozeInterval.OneHour => DateTime.UtcNow.AddHours(1),
            SnoozeInterval.TwoHours => DateTime.UtcNow.AddHours(2),
            SnoozeInterval.Tomorrow => DateTime.UtcNow.AddDays(1).Date,
            SnoozeInterval.OneMinuteBeforeStart => targetTime.AddMinutes(-1),
            SnoozeInterval.WhenItStarts => targetTime,
            _ => DateTime.UtcNow.AddMinutes(5)
        };
    }

    public void ClearFiredReminders()
    {
        _firedReminders.Clear();
    }
}
