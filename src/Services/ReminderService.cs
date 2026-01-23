using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Json;
using Google.Apis.Calendar.v3.Data;
using ICalCalendarEvent = Ical.Net.CalendarComponents.CalendarEvent;
using perinma.Storage.Models;
using perinma.Utils;
using perinma.Storage;
using perinma.Models;

namespace perinma.Services;

public class ReminderService(SqliteStorage storage, IReadOnlyDictionary<string, ICalendarProvider> providers)
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
        var providerKey = accountType == AccountType.Google ? "Google" : "CalDAV";
        if (!providers.TryGetValue(providerKey, out var provider))
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

        // Get reminder minutes from the provider
        var reminderMinutes = await provider.GetReminderMinutesAsync(rawData, rawCalendarData, cancellationToken);

        if (reminderMinutes.Count == 0)
        {
            return;
        }

        // Parse event start time and recurrence info from raw data
        var (eventStartTime, isRecurring, recurrenceOccurrences) = ParseEventInfo(rawData, accountType, reminderMinutes.ToList());

        if (!eventStartTime.HasValue)
        {
            return;
        }

        List<(DateTime Occurrence, DateTime TriggerTime)> reminderOccurrences = [];

        if (isRecurring && recurrenceOccurrences.Count > 0)
        {
            reminderOccurrences.AddRange(recurrenceOccurrences);
        }
        else
        {
            foreach (var minutes in reminderMinutes)
            {
                var triggerTime = eventStartTime.Value.AddMinutes(-minutes);
                if (triggerTime > DateTime.UtcNow)
                {
                    reminderOccurrences.Add((eventStartTime.Value, triggerTime));
                }
            }
        }

        var existingReminders = await storage.GetRemindersByEventAsync(eventId);

        var remindersToDelete = existingReminders
            .Where(r => !reminderOccurrences.Any(o => o.TriggerTime == DateTimeOffset.FromUnixTimeSeconds(r.TriggerTime).DateTime))
            .Select(r => r.ReminderId)
            .ToList();

        await storage.DeleteRemindersAsync(remindersToDelete);

        foreach (var (occurrence, triggerTime) in reminderOccurrences)
        {
            if (!existingReminders.Any(r => r.TriggerTime == new DateTimeOffset(triggerTime).ToUnixTimeSeconds()))
            {
                await storage.CreateReminderAsync(eventId, occurrence, triggerTime);
            }
        }
    }

    private (DateTime? StartTime, bool IsRecurring, List<(DateTime Occurrence, DateTime TriggerTime)> Occurrences) ParseEventInfo(
        string rawData,
        AccountType accountType,
        List<int> reminderMinutes)
    {
        if (accountType == AccountType.Google)
        {
            return ParseGoogleEventInfo(rawData, reminderMinutes);
        }
        else
        {
            return ParseCalDavEventInfo(rawData, reminderMinutes);
        }
    }

    private (DateTime? StartTime, bool IsRecurring, List<(DateTime Occurrence, DateTime TriggerTime)> Occurrences) ParseGoogleEventInfo(
        string rawData,
        List<int> reminderMinutes)
    {
        var googleEvent = NewtonsoftJsonSerializer.Instance.Deserialize<Event>(rawData);
        if (googleEvent == null)
        {
            return (null, false, []);
        }

        var eventStartTime = ParseEventStartTime(googleEvent);
        if (!eventStartTime.HasValue)
        {
            return (null, false, []);
        }

        var isRecurring = googleEvent.Recurrence is { Count: > 0 };
        List<(DateTime Occurrence, DateTime TriggerTime)> occurrences = [];

        if (isRecurring)
        {
            occurrences = GetRecurringOccurrences(googleEvent, reminderMinutes);
        }

        return (eventStartTime, isRecurring, occurrences);
    }

    private (DateTime? StartTime, bool IsRecurring, List<(DateTime Occurrence, DateTime TriggerTime)> Occurrences) ParseCalDavEventInfo(
        string rawData,
        List<int> reminderMinutes)
    {
        try
        {
            var calendar = Ical.Net.Calendar.Load(rawData);
            var evt = calendar?.Events.FirstOrDefault();
            if (evt == null)
            {
                return (null, false, []);
            }

            var eventStartTime = evt.Start?.AsUtc;
            if (!eventStartTime.HasValue)
            {
                return (null, false, []);
            }

            var isRecurring = evt.RecurrenceRules.Count > 0;
            List<(DateTime Occurrence, DateTime TriggerTime)> occurrences = [];

            if (isRecurring)
            {
                occurrences = GetCalDavRecurringOccurrences(evt, reminderMinutes);
            }

            return (eventStartTime, isRecurring, occurrences);
        }
        catch (Exception)
        {
            return (null, false, []);
        }
    }

    private DateTime? ParseEventStartTime(Event googleEvent)
    {
        if (googleEvent.Start?.DateTimeRaw != null && DateTime.TryParse(googleEvent.Start.DateTimeRaw, out var dateTime))
        {
            return dateTime;
        }

        if (googleEvent.Start?.Date != null && DateTime.TryParse(googleEvent.Start.Date, out var date))
        {
            return date;
        }

        return null;
    }

    private List<(DateTime Occurrence, DateTime TriggerTime)> GetRecurringOccurrences(Event googleEvent, List<int> reminderMinutes)
    {
        var eventStartTime = ParseEventStartTime(googleEvent);
        if (!eventStartTime.HasValue)
        {
            return [];
        }

        var recurrenceEndTime = RecurrenceParser.GetRecurrenceEndTime(
            googleEvent.Recurrence,
            eventStartTime.Value,
            eventStartTime.Value.AddHours(1)
        );

        var now = DateTime.UtcNow;

        var icalString = BuildIcalString(googleEvent.Recurrence);
        if (string.IsNullOrEmpty(icalString))
        {
            return [];
        }

        try
        {
            var calendar = Ical.Net.Calendar.Load(icalString);
            var icalEvent = calendar?.Events.FirstOrDefault();

            if (icalEvent == null)
            {
                return [];
            }

            var occurrences = icalEvent.GetOccurrences();

            var result = new List<(DateTime Occurrence, DateTime TriggerTime)>();
            var searchEnd = recurrenceEndTime ?? now.AddYears(10);

            foreach (var occurrence in occurrences)
            {
                var occurrenceTime = occurrence.Period.StartTime.AsUtc;
                if (occurrenceTime < now)
                {
                    continue;
                }

                if (occurrenceTime > searchEnd)
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

            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private List<(DateTime Occurrence, DateTime TriggerTime)> GetCalDavRecurringOccurrences(ICalCalendarEvent evt, List<int> reminderMinutes)
    {
        var eventStartTime = evt.Start?.AsUtc;
        if (!eventStartTime.HasValue)
        {
            return [];
        }

        var recurrenceEndTime = RecurrenceParser.CalculateRecurrenceEndTime(evt);

        var now = DateTime.UtcNow;

        try
        {
            var occurrences = evt.GetOccurrences();

            var result = new List<(DateTime Occurrence, DateTime TriggerTime)>();
            var searchEnd = recurrenceEndTime ?? now.AddYears(10);

            foreach (var occurrence in occurrences)
            {
                var occurrenceTime = occurrence.Period.StartTime.AsUtc;
                if (occurrenceTime < now)
                {
                    continue;
                }

                if (occurrenceTime > searchEnd)
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

            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private string BuildIcalString(IList<string> recurrence)
    {
        if (recurrence == null || recurrence.Count == 0)
        {
            return string.Empty;
        }

        var ical = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n";
        foreach (var r in recurrence)
        {
            ical += r + "\r\n";
        }
        ical += "END:VEVENT\r\nEND:VCALENDAR";

        return ical;
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
        await HandleReminderFiredAsync(reminderId, cancellationToken);
    }

    public async Task SnoozeReminderAsync(string reminderId, SnoozeInterval interval, CancellationToken cancellationToken = default)
    {
        _firedReminders.Remove(reminderId);
        await HandleReminderFiredAsync(reminderId, cancellationToken, interval, cancellationToken);
    }

    private async Task HandleReminderFiredAsync(string reminderId, CancellationToken cancellationToken, SnoozeInterval? snoozeInterval = null, CancellationToken? originalCancellationToken = null)
    {
        var reminder = await storage.GetReminderAsync(reminderId);

        if (reminder == null)
        {
            return;
        }

        if (snoozeInterval.HasValue)
        {
            var newTriggerTime = CalculateSnoozeTime(reminder.TargetTime, snoozeInterval.Value);
            await storage.CreateReminderAsync(reminder.TargetId, DateTimeOffset.FromUnixTimeSeconds(reminder.TargetTime).DateTime, newTriggerTime);
        }
        else
        {
            var rawDataJson = await storage.GetEventData(reminder.TargetId, "rawData");

            if (!string.IsNullOrEmpty(rawDataJson))
            {
                var calendarId = await storage.GetEventCalendarIdAsync(reminder.TargetId);
                if (!string.IsNullOrEmpty(calendarId))
                {
                    var accountType = await storage.GetAccountTypeForCalendarAsync(calendarId);
                    if (accountType.HasValue)
                    {
                        await PopulateRemindersForEventAsync(reminder.TargetId, calendarId, accountType.Value, originalCancellationToken ?? cancellationToken);
                    }
                }
            }
        }

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
