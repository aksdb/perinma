using System;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using Ical.Net.CalendarComponents;
using Ical.Net.Serialization;
using perinma.Services.CalDAV;

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

    /// <summary>
    /// Formats a DateTime for iCalendar (UTC format).
    /// </summary>
    private static string FormatICalDateTime(DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'");
    }

    /// <summary>
    /// Formats a DateTime for iCalendar (local format, without timezone).
    /// </summary>
    private static string FormatICalDateTimeLocal(DateTime dt)
    {
        return dt.ToString("yyyyMMdd'T'HHmmss");
    }

    /// <summary>
    /// Creates a simple CalDAV event in iCalendar format.
    /// </summary>
    public static string CreateCalDavEventRaw(
        string uid,
        string summary,
        DateTime start,
        DateTime end,
        string status = "CONFIRMED")
    {
        return $"BEGIN:VCALENDAR\r\n" +
               $"VERSION:2.0\r\n" +
               $"PRODID:-//Test//Test//EN\r\n" +
               $"BEGIN:VEVENT\r\n" +
               $"UID:{uid}\r\n" +
               $"DTSTART:{FormatICalDateTime(start)}\r\n" +
               $"DTEND:{FormatICalDateTime(end)}\r\n" +
               $"SUMMARY:{summary}\r\n" +
               $"STATUS:{status}\r\n" +
               $"END:VEVENT\r\n" +
               $"END:VCALENDAR";
    }

    /// <summary>
    /// Creates a recurring CalDAV event with RRULE in iCalendar format.
    /// </summary>
    public static string CreateRecurringCalDavEventRaw(
        string uid,
        string summary,
        DateTime start,
        DateTime end,
        string rrule)
    {
        return $"BEGIN:VCALENDAR\r\n" +
               $"VERSION:2.0\r\n" +
               $"PRODID:-//Test//Test//EN\r\n" +
               $"BEGIN:VEVENT\r\n" +
               $"UID:{uid}\r\n" +
               $"DTSTART:{FormatICalDateTime(start)}\r\n" +
               $"DTEND:{FormatICalDateTime(end)}\r\n" +
               $"SUMMARY:{summary}\r\n" +
               $"STATUS:CONFIRMED\r\n" +
               $"{rrule}\r\n" +
               $"END:VEVENT\r\n" +
               $"END:VCALENDAR";
    }

    /// <summary>
    /// Creates a recurring CalDAV event with timezone-aware times.
    /// </summary>
    public static string CreateRecurringCalDavEventRawWithTimezone(
        string uid,
        string summary,
        DateTime start,
        DateTime end,
        string tzid,
        string rrule)
    {
        var vtimezone = CreateVTimezoneComponent(tzid);

        return $"BEGIN:VCALENDAR\r\n" +
               $"VERSION:2.0\r\n" +
               $"PRODID:-//Test//Test//EN\r\n" +
               $"{vtimezone}\r\n" +
               $"BEGIN:VEVENT\r\n" +
               $"UID:{uid}\r\n" +
               $"DTSTART;TZID={tzid}:{FormatICalDateTimeLocal(start)}\r\n" +
               $"DTEND;TZID={tzid}:{FormatICalDateTimeLocal(end)}\r\n" +
               $"SUMMARY:{summary}\r\n" +
               $"STATUS:CONFIRMED\r\n" +
               $"{rrule}\r\n" +
               $"END:VEVENT\r\n" +
               $"END:VCALENDAR";
    }

    /// <summary>
    /// Creates a CalDAV event with VALARM (reminder).
    /// </summary>
    public static string CreateCalDavEventWithAlarm(
        string uid,
        string summary,
        DateTime start,
        DateTime end,
        params int[] minutesBefore)
    {
        var alarms = string.Join("\r\n", minutesBefore.Select(m =>
            $"BEGIN:VALARM\r\n" +
            $"ACTION:DISPLAY\r\n" +
            $"TRIGGER:-PT{m}M\r\n" +
            $"DESCRIPTION:Reminder\r\n" +
            $"END:VALARM"));

        return $"BEGIN:VCALENDAR\r\n" +
               $"VERSION:2.0\r\n" +
               $"PRODID:-//Test//Test//EN\r\n" +
               $"BEGIN:VEVENT\r\n" +
               $"UID:{uid}\r\n" +
               $"DTSTART:{FormatICalDateTime(start)}\r\n" +
               $"DTEND:{FormatICalDateTime(end)}\r\n" +
               $"SUMMARY:{summary}\r\n" +
               $"STATUS:CONFIRMED\r\n" +
               $"{alarms}\r\n" +
               $"END:VEVENT\r\n" +
               $"END:VCALENDAR";
    }

    /// <summary>
    /// Creates a CalDAV event with a day-based alarm.
    /// </summary>
    public static string CreateCalDavEventWithDayAlarm(
        string uid,
        string summary,
        DateTime start,
        DateTime end,
        int daysBefore)
    {
        var alarm = $"BEGIN:VALARM\r\n" +
                    $"ACTION:DISPLAY\r\n" +
                    $"TRIGGER:-P{daysBefore}D\r\n" +
                    $"DESCRIPTION:Reminder\r\n" +
                    $"END:VALARM";

        return $"BEGIN:VCALENDAR\r\n" +
               $"VERSION:2.0\r\n" +
               $"PRODID:-//Test//Test//EN\r\n" +
               $"BEGIN:VEVENT\r\n" +
               $"UID:{uid}\r\n" +
               $"DTSTART:{FormatICalDateTime(start)}\r\n" +
               $"DTEND:{FormatICalDateTime(end)}\r\n" +
               $"SUMMARY:{summary}\r\n" +
               $"STATUS:CONFIRMED\r\n" +
               $"{alarm}\r\n" +
               $"END:VEVENT\r\n" +
               $"END:VCALENDAR";
    }

    /// <summary>
    /// Creates a basic VTIMEZONE component.
    /// </summary>
    private static string CreateVTimezoneComponent(string tzId)
    {
        return $"BEGIN:VTIMEZONE\r\n" +
               $"TZID:{tzId}\r\n" +
               $"BEGIN:STANDARD\r\n" +
               $"DTSTART:19700101T000000\r\n" +
               $"TZOFFSETFROM:+0000\r\n" +
               $"RRULE:FREQ=YEARLY\r\n" +
               $"END:STANDARD\r\n" +
               $"END:VTIMEZONE";
    }

    /// <summary>
    /// Creates a CalDavEvent object with the specified properties.
    /// </summary>
    public static CalDavEvent CreateCalDavEvent(
        string uid,
        string url,
        string? summary,
        DateTime? startTime,
        DateTime? endTime,
        string status = "CONFIRMED")
    {
        string? rawICal = null;
        if (startTime.HasValue && endTime.HasValue)
        {
            rawICal = CreateCalDavEventRaw(uid, summary ?? "Untitled", startTime.Value, endTime.Value, status);
        }

        return new CalDavEvent
        {
            Uid = uid,
            Url = url,
            Summary = summary,
            StartTime = startTime,
            EndTime = endTime,
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
        DateTime startTime,
        DateTime endTime,
        string rrule)
    {
        var rawICal = CreateRecurringCalDavEventRaw(uid, summary ?? "Untitled", startTime, endTime, rrule);

        return new CalDavEvent
        {
            Uid = uid,
            Url = url,
            Summary = summary,
            StartTime = startTime,
            EndTime = endTime,
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
        DateTime startTime,
        DateTime endTime,
        string tzid,
        string rrule)
    {
        var rawICal = CreateRecurringCalDavEventRawWithTimezone(uid, summary ?? "Untitled", startTime, endTime, tzid, rrule);

        return new CalDavEvent
        {
            Uid = uid,
            Url = url,
            Summary = summary,
            StartTime = startTime,
            EndTime = endTime,
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
        return "BEGIN:VTIMEZONE\r\n" +
               "TZID:Europe/Berlin\r\n" +
               "LAST-MODIFIED:20250324T091428Z\r\n" +
               "X-LIC-LOCATION:Europe/Berlin\r\n" +
               "BEGIN:DAYLIGHT\r\n" +
               "TZNAME:CEST\r\n" +
               "TZOFFSETFROM:+0100\r\n" +
               "TZOFFSETTO:+0200\r\n" +
               "DTSTART:19700329T020000\r\n" +
               "RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU\r\n" +
               "END:DAYLIGHT\r\n" +
               "BEGIN:STANDARD\r\n" +
               "TZNAME:CET\r\n" +
               "TZOFFSETFROM:+0200\r\n" +
               "TZOFFSETTO:+0100\r\n" +
               "DTSTART:19701025T030000\r\n" +
               "RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU\r\n" +
               "END:STANDARD\r\n" +
               "END:VTIMEZONE";
    }

    #endregion
}
