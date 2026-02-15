using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using NodaTime;
using perinma.Services.CalDAV;
using Duration = NodaTime.Duration;
using ICalCalendar = Ical.Net.Calendar;
using ICalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace tests.Helpers;

/// <summary>
/// Helper functions for creating test event data using real Google API and iCal models.
/// </summary>
public static class TestDataHelpers
{
    #region Google Calendar Helpers

    /// <summary>
    /// Creates a Google calendar with the specified properties.
    /// </summary>
    public static CalendarListEntry CreateGoogleCalendar(
        string id,
        string summary,
        bool selected = true,
        string? color = null,
        bool deleted = false)
    {
        return new CalendarListEntry
        {
            Id = id,
            Summary = summary,
            Selected = selected,
            BackgroundColor = color ?? "#9fc6e7",
            Deleted = deleted
        };
    }

    /// <summary>
    /// Creates a Google calendar with the specified properties and returns it as JSON string.
    /// </summary>
    public static string CreateGoogleCalendarRaw(
        string id,
        string summary,
        bool selected = true,
        string? color = null,
        bool deleted = false)
    {
        var cal = CreateGoogleCalendar(id, summary, selected, color, deleted);
        return NewtonsoftJsonSerializer.Instance.Serialize(cal);
    }

    /// <summary>
    /// Creates a Google event with the specified properties.
    /// </summary>
    public static string CreateGoogleEventRaw(
        string id,
        string summary,
        DateTime start,
        DateTime end,
        string status = "confirmed")
    {
        var evt = new Event
        {
            Id = id,
            Summary = summary,
            Status = status,
            Start = new EventDateTime { DateTimeRaw = start.ToString("o"), TimeZone = TimeZoneInfo.Utc.Id },
            End = new EventDateTime { DateTimeRaw = end.ToString("o"), TimeZone = TimeZoneInfo.Utc.Id }
        };
        return NewtonsoftJsonSerializer.Instance.Serialize(evt);
    }

    /// <summary>
    /// Creates a cancelled Google event.
    /// </summary>
    public static string CreateCancelledGoogleEvent(string id)
    {
        var evt = new Event
        {
            Id = id,
            Summary = "Cancelled Event",
            Status = "cancelled"
        };
        return NewtonsoftJsonSerializer.Instance.Serialize(evt);
    }

    /// <summary>
    /// Creates a recurring Google event with RRULE.
    /// </summary>
    public static string CreateRecurringGoogleEvent(
        string id,
        string summary,
        DateTime start,
        DateTime end,
        params string[] recurrence)
    {
        var evt = new Event
        {
            Id = id,
            Summary = summary,
            Status = "confirmed",
            Start = new EventDateTime { DateTimeRaw = start.ToString("o"), TimeZone = TimeZoneInfo.Utc.Id },
            End = new EventDateTime { DateTimeRaw = end.ToString("o"), TimeZone = TimeZoneInfo.Utc.Id },
            Recurrence = recurrence.Length > 0 ? new List<string>(recurrence) : null
        };
        return NewtonsoftJsonSerializer.Instance.Serialize(evt);
    }

    /// <summary>
    /// Creates a recurring Google event with timezone-aware times.
    /// </summary>
    public static string CreateRecurringGoogleEventWithTimezone(
        string id,
        string summary,
        DateTime start,
        DateTime end,
        string timeZone,
        params string[] recurrence)
    {
        var evt = new Event
        {
            Id = id,
            Summary = summary,
            Status = "confirmed",
            Start = new EventDateTime
            {
                DateTimeRaw = start.ToString("o"),
                TimeZone = timeZone
            },
            End = new EventDateTime
            {
                DateTimeRaw = end.ToString("o"),
                TimeZone = timeZone
            },
            Recurrence = recurrence.Length > 0 ? new List<string>(recurrence) : null
        };
        return NewtonsoftJsonSerializer.Instance.Serialize(evt);
    }

    /// <summary>
    /// Creates a modified override event for a recurring Google event.
    /// </summary>
    public static string CreateModifiedGoogleEventOverride(
        string id,
        string recurringEventId,
        string summary,
        DateTime originalStartTime,
        DateTime newStart,
        DateTime newEnd)
    {
        var evt = new Event
        {
            Id = id,
            RecurringEventId = recurringEventId,
            Summary = summary,
            Status = "confirmed",
            OriginalStartTime = new EventDateTime { DateTimeRaw = originalStartTime.ToString("o"), TimeZone = TimeZoneInfo.Utc.Id },
            Start = new EventDateTime { DateTimeRaw = newStart.ToString("o"), TimeZone = TimeZoneInfo.Utc.Id },
            End = new EventDateTime { DateTimeRaw = newEnd.ToString("o"), TimeZone = TimeZoneInfo.Utc.Id }
        };
        return NewtonsoftJsonSerializer.Instance.Serialize(evt);
    }

    /// <summary>
    /// Creates a cancelled override event for a recurring Google event.
    /// </summary>
    public static string CreateCancelledGoogleEventOverride(
        string id,
        string recurringEventId,
        DateTime originalStartTime)
    {
        var evt = new Event
        {
            Id = id,
            RecurringEventId = recurringEventId,
            Summary = "Cancelled Event",
            Status = "cancelled",
            OriginalStartTime = new EventDateTime { DateTimeRaw = originalStartTime.ToString("o"), TimeZone = TimeZoneInfo.Utc.Id }
        };
        return NewtonsoftJsonSerializer.Instance.Serialize(evt);
    }

    /// <summary>
    /// Creates a Google event with reminders.
    /// </summary>
    public static string CreateGoogleEventWithReminders(
        string id,
        string summary,
        DateTime start,
        DateTime end,
        bool useDefault,
        params int[] popupReminderMinutes)
    {
        var evt = new Event
        {
            Id = id,
            Summary = summary,
            Status = "confirmed",
            Start = new EventDateTime { DateTimeRaw = start.ToString("o"), TimeZone = TimeZoneInfo.Utc.Id },
            End = new EventDateTime { DateTimeRaw = end.ToString("o"), TimeZone = TimeZoneInfo.Utc.Id },
            Reminders = new Event.RemindersData
            {
                UseDefault = useDefault,
                Overrides = !useDefault ? popupReminderMinutes.Select(m => new EventReminder
                {
                    Method = "popup",
                    Minutes = m
                }).ToList() : null
            }
        };
        return NewtonsoftJsonSerializer.Instance.Serialize(evt);
    }

    /// <summary>
    /// Creates a Google calendar with default reminders.
    /// </summary>
    public static string CreateGoogleCalendarWithDefaultReminders(
        string id,
        string summary,
        params int[] defaultReminderMinutes)
    {
        var cal = new CalendarListEntry
        {
            Id = id,
            Summary = summary,
            Selected = true,
            DefaultReminders = defaultReminderMinutes.Select(m => new EventReminder
            {
                Method = "popup",
                Minutes = m
            }).ToList()
        };
        return NewtonsoftJsonSerializer.Instance.Serialize(cal);
    }

    #endregion

    #region CalDAV/iCalendar Helpers

    private static string SerializeCalendar(ICalCalendar calendar)
    {
        var serializer = new CalendarSerializer();
        return serializer.SerializeToString(calendar) ?? string.Empty;
    }

    private static CalDateTime ToCalDateTime(ZonedDateTime zonedDateTime)
    {
        var dateTime = zonedDateTime.ToDateTimeUnspecified();
        var tzid = zonedDateTime.Zone.Id;
        return new CalDateTime(dateTime, tzid);
    }

    private static CalDateTime ToCalDateTime(DateTime dateTime)
    {
        if (dateTime.Kind == DateTimeKind.Utc || dateTime.Kind == DateTimeKind.Unspecified)
        {
            return new CalDateTime(dateTime, true);
        }

        return new CalDateTime(dateTime.ToUniversalTime(), true);
    }

    private static RecurrencePattern? ParseRrule(string rrule)
    {
        if (string.IsNullOrEmpty(rrule) || !rrule.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rruleValue = rrule[6..];
        var pattern = new RecurrencePattern();
        var parts = rruleValue.Split(';');

        foreach (var part in parts)
        {
            var keyValue = part.Split('=');
            if (keyValue.Length != 2) continue;

            var key = keyValue[0].ToUpperInvariant();
            var value = keyValue[1];

            switch (key)
            {
                case "FREQ":
                    pattern.Frequency = Enum.Parse<FrequencyType>(value, true);
                    break;
                case "INTERVAL":
                    pattern.Interval = int.Parse(value);
                    break;
                case "COUNT":
                    pattern.Count = int.Parse(value);
                    break;
                case "UNTIL":
                    pattern.Until = new CalDateTime(DateTime.ParseExact(value, "yyyyMMdd'T'HHmmss'Z'", null), true);
                    break;
                case "BYDAY":
                    pattern.ByDay = value.Split(',').Select(d => new WeekDay(d)).ToList();
                    break;
                case "BYMONTH":
                    pattern.ByMonth = value.Split(',').Select(int.Parse).ToList();
                    break;
                case "BYMONTHDAY":
                    pattern.ByMonthDay = value.Split(',').Select(int.Parse).ToList();
                    break;
                case "WKST":
                    pattern.FirstDayOfWeek = Enum.Parse<DayOfWeek>(value, true);
                    break;
            }
        }

        return pattern;
    }

    /// <summary>
    /// Creates a simple CalDAV event in iCalendar format.
    /// </summary>
    public static string CreateCalDavEventRaw(
        string uid,
        string summary,
        ZonedDateTime start,
        ZonedDateTime end,
        string status = "CONFIRMED")
    {
        var calendar = new ICalCalendar
        {
            Method = "PUBLISH",
            Version = "2.0",
            ProductId = "-//Test//Test//EN"
        };

        var evt = new ICalEvent
        {
            Uid = uid,
            DtStart = ToCalDateTime(start),
            DtEnd = ToCalDateTime(end),
            Summary = summary,
            Status = status
        };

        calendar.Events.Add(evt);

        return SerializeCalendar(calendar);
    }

    /// <summary>
    /// Creates a recurring CalDAV event with RRULE in iCalendar format.
    /// </summary>
    public static string CreateRecurringCalDavEventRaw(
        string uid,
        string summary,
        ZonedDateTime start,
        ZonedDateTime end,
        string rrule)
    {
        var calendar = new ICalCalendar
        {
            Method = "PUBLISH",
            Version = "2.0",
            ProductId = "-//Test//Test//EN"
        };

        var evt = new ICalEvent
        {
            Uid = uid,
            DtStart = ToCalDateTime(start),
            DtEnd = ToCalDateTime(end),
            Summary = summary,
            Status = "CONFIRMED"
        };

        var pattern = ParseRrule(rrule);
        if (pattern != null)
        {
            evt.RecurrenceRules.Add(pattern);
        }

        calendar.Events.Add(evt);

        return SerializeCalendar(calendar);
    }

    /// <summary>
    /// Creates a recurring CalDAV event with timezone-aware times.
    /// </summary>
    public static string CreateRecurringCalDavEventRawWithTimezone(
        string uid,
        string summary,
        ZonedDateTime start,
        ZonedDateTime end,
        string rrule)
    {
        var calendar = new ICalCalendar
        {
            Method = "PUBLISH",
            Version = "2.0",
            ProductId = "-//Test//Test//EN"
        };

        var tzid = start.Zone.Id;
        calendar.AddTimeZone(tzid);

        var evt = new ICalEvent
        {
            Uid = uid,
            DtStart = ToCalDateTime(start),
            DtEnd = ToCalDateTime(end),
            Summary = summary,
            Status = "CONFIRMED"
        };

        var pattern = ParseRrule(rrule);
        if (pattern != null)
        {
            evt.RecurrenceRules.Add(pattern);
        }

        calendar.Events.Add(evt);

        return SerializeCalendar(calendar);
    }

    /// <summary>
    /// Creates a CalDAV event with VALARM (reminder).
    /// </summary>
    public static string CreateCalDavEventWithAlarm(
        string uid,
        string summary,
        ZonedDateTime start,
        ZonedDateTime end,
        params int[] minutesBefore)
    {
        var calendar = new ICalCalendar
        {
            Method = "PUBLISH",
            Version = "2.0",
            ProductId = "-//Test//Test//EN"
        };

        var evt = new ICalEvent
        {
            Uid = uid,
            DtStart = ToCalDateTime(start),
            DtEnd = ToCalDateTime(end),
            Summary = summary,
            Status = "CONFIRMED"
        };

        foreach (var minutes in minutesBefore)
        {
            var alarm = new Alarm
            {
                Action = AlarmAction.Display,
                Trigger = new Trigger(),
                Summary = "Reminder"
            };
            alarm.Trigger.DateTime = ToCalDateTime(start.Plus(Duration.FromMinutes(-minutes)));
            evt.Alarms.Add(alarm);
        }

        calendar.Events.Add(evt);

        return SerializeCalendar(calendar);
    }

    /// <summary>
    /// Creates a CalDAV event with a day-based alarm.
    /// </summary>
    public static string CreateCalDavEventWithDayAlarm(
        string uid,
        string summary,
        ZonedDateTime start,
        ZonedDateTime end,
        int daysBefore)
    {
        var calendar = new ICalCalendar
        {
            Method = "PUBLISH",
            Version = "2.0",
            ProductId = "-//Test//Test//EN"
        };

        var evt = new ICalEvent
        {
            Uid = uid,
            DtStart = ToCalDateTime(start),
            DtEnd = ToCalDateTime(end),
            Summary = summary,
            Status = "CONFIRMED"
        };

        var alarm = new Alarm
        {
            Action = AlarmAction.Display,
            Trigger = new Trigger(),
            Summary = "Reminder"
        };
        alarm.Trigger.DateTime = ToCalDateTime(start.Plus(Duration.FromDays(-daysBefore)));
        evt.Alarms.Add(alarm);

        calendar.Events.Add(evt);

        return SerializeCalendar(calendar);
    }

    /// <summary>
    /// Creates a CalDavEvent object with the specified properties.
    /// </summary>
    public static CalDavEvent CreateCalDavEvent(
        string uid,
        string url,
        string? summary,
        ZonedDateTime? startTime,
        ZonedDateTime? endTime,
        string status = "CONFIRMED")
    {
        string? rawICal = null;
        DateTime? startTimeDateTime = null;
        DateTime? endTimeDateTime = null;
        if (startTime.HasValue && endTime.HasValue)
        {
            rawICal = CreateCalDavEventRaw(uid, summary ?? "Untitled", startTime.Value, endTime.Value, status);
            startTimeDateTime = startTime.Value.ToDateTimeUtc();
            endTimeDateTime = endTime.Value.ToDateTimeUtc();
        }

        return new CalDavEvent
        {
            Uid = uid,
            Url = url,
            Summary = summary,
            StartTime = startTimeDateTime,
            EndTime = endTimeDateTime,
            Status = status,
            RawICalendar = rawICal,
            Deleted = false
        };
    }

    /// <summary>
    /// Creates a CalDavEvent object with recurrence rules.
    /// </summary>
    public static CalDavEvent CreateCalDavEventWithRecurrence(
        string uid,
        string url,
        string? summary,
        ZonedDateTime startTime,
        ZonedDateTime endTime,
        string rrule)
    {
        var rawICal = CreateRecurringCalDavEventRaw(uid, summary ?? "Untitled", startTime, endTime, rrule);

        return new CalDavEvent
        {
            Uid = uid,
            Url = url,
            Summary = summary,
            StartTime = startTime.ToDateTimeUtc(),
            EndTime = endTime.ToDateTimeUtc(),
            Status = "CONFIRMED",
            RawICalendar = rawICal,
            Deleted = false
        };
    }

    /// <summary>
    /// Creates a CalDavEvent object with recurrence rules and timezone.
    /// </summary>
    public static CalDavEvent CreateCalDavEventWithRecurrenceAndTimezone(
        string uid,
        string url,
        string? summary,
        ZonedDateTime startTime,
        ZonedDateTime endTime,
        string rrule)
    {
        var rawICal = CreateRecurringCalDavEventRawWithTimezone(uid, summary ?? "Untitled", startTime, endTime, rrule);

        return new CalDavEvent
        {
            Uid = uid,
            Url = url,
            Summary = summary,
            StartTime = startTime.ToDateTimeUtc(),
            EndTime = endTime.ToDateTimeUtc(),
            Status = "CONFIRMED",
            RawICalendar = rawICal,
            Deleted = false
        };
    }

    /// <summary>
    /// Creates a full VTIMEZONE component with DST support for Europe/Berlin.
    /// </summary>
    public static string CreateEuropeBerlinVTimezone()
    {
        var calendar = new ICalCalendar
        {
            Version = "2.0",
            ProductId = "-//Test//Test//EN"
        };

        var tz = new VTimeZone("Europe/Berlin")
        {
            TzId = "Europe/Berlin"
        };

        var dst = new VTimeZoneInfo
        {
            Name = "CEST",
            OffsetFrom = new UtcOffset("+0100"),
            OffsetTo = new UtcOffset("+0200"),
            Start = new CalDateTime(1970, 3, 29, 2, 0, 0, "Europe/Berlin"),
            RecurrenceRules = new List<RecurrencePattern>
            {
                new(FrequencyType.Yearly)
                {
                    ByMonth = [3],
                    ByDay = [new WeekDay(DayOfWeek.Sunday, -1)]
                }
            }
        };
        tz.TimeZoneInfos.Add(dst);

        var standard = new VTimeZoneInfo
        {
            Name = "CET",
            OffsetFrom = new UtcOffset("+0200"),
            OffsetTo = new UtcOffset("+0100"),
            Start = new CalDateTime(1970, 10, 25, 3, 0, 0, "Europe/Berlin"),
            RecurrenceRules = new List<RecurrencePattern>
            {
                new(FrequencyType.Yearly)
                {
                    ByMonth = [10],
                    ByDay = [new WeekDay(DayOfWeek.Sunday, -1)]
                }
            }
        };
        tz.TimeZoneInfos.Add(standard);

        calendar.TimeZones.Add(tz);

        return SerializeCalendar(calendar);
    }

    #endregion
}
