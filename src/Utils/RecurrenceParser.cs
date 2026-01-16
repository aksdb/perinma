using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace perinma.Utils;

/// <summary>
/// Parses recurrence rules (RRULE) from Google Calendar and CalDAV/iCalendar formats
/// to determine the actual end time of recurring events.
/// </summary>
public static class RecurrenceParser
{
    /// <summary>
    /// Parses Google Calendar recurrence strings (RRULE, RDATE, EXDATE) and calculates
    /// the recurrence end time.
    /// </summary>
    /// <param name="recurrence">List of recurrence strings from Google Calendar API</param>
    /// <param name="eventStart">The start time of the event</param>
    /// <param name="eventEnd">The end time of the event</param>
    /// <returns>The recurrence end time, or null if the event recurs forever</returns>
    public static DateTime? GetRecurrenceEndTime(IList<string>? recurrence, DateTime eventStart, DateTime eventEnd)
    {
        if (recurrence == null || recurrence.Count == 0)
            return null;

        // Build a minimal iCalendar to parse using Ical.Net
        var icalBuilder = new StringBuilder();
        icalBuilder.AppendLine("BEGIN:VCALENDAR");
        icalBuilder.AppendLine("VERSION:2.0");
        icalBuilder.AppendLine("BEGIN:VEVENT");
        icalBuilder.AppendLine($"DTSTART:{FormatDateTime(eventStart)}");
        icalBuilder.AppendLine($"DTEND:{FormatDateTime(eventEnd)}");
        icalBuilder.AppendLine("UID:temp-uid@perinma");

        foreach (var rule in recurrence)
        {
            // Google returns recurrence rules with prefixes like "RRULE:", "EXDATE:", etc.
            icalBuilder.AppendLine(rule);
        }

        icalBuilder.AppendLine("END:VEVENT");
        icalBuilder.AppendLine("END:VCALENDAR");

        try
        {
            var calendar = Calendar.Load(icalBuilder.ToString());
            var calendarEvent = calendar?.Events.FirstOrDefault();

            if (calendarEvent != null)
            {
                return CalculateRecurrenceEndTime(calendarEvent);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing Google recurrence rule: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Calculates the recurrence end time from an Ical.Net CalendarEvent.
    /// </summary>
    /// <param name="calendarEvent">The parsed iCalendar event</param>
    /// <returns>The recurrence end time, or null if the event recurs forever</returns>
    public static DateTime? CalculateRecurrenceEndTime(CalendarEvent calendarEvent)
    {
        if (calendarEvent.RecurrenceRules == null || calendarEvent.RecurrenceRules.Count == 0)
            return null;

        DateTime? latestEndTime = null;
        var duration = GetEventDuration(calendarEvent);
        var startTime = calendarEvent.Start?.AsUtc ?? DateTime.UtcNow;

        foreach (var rrule in calendarEvent.RecurrenceRules)
        {
            var ruleEndTime = GetEndTimeFromRecurrencePattern(rrule, startTime, duration);
            if (ruleEndTime == null)
            {
                // If any rule has no end (infinite), the whole series is infinite
                return null;
            }

            if (latestEndTime == null || ruleEndTime > latestEndTime)
            {
                latestEndTime = ruleEndTime;
            }
        }

        return latestEndTime;
    }

    /// <summary>
    /// Gets the end time from a single recurrence pattern.
    /// </summary>
    private static DateTime? GetEndTimeFromRecurrencePattern(RecurrencePattern rrule, DateTime startTime, TimeSpan duration)
    {
        // Check for UNTIL clause (explicit end date)
        if (rrule.Until != null && rrule.Until.Value != DateTime.MinValue)
        {
            // The UNTIL date is the last possible occurrence start.
            // We need to add the event duration to get the actual end time.
            return rrule.Until.AsUtc.Add(duration);
        }

        // Check for COUNT clause (finite number of occurrences)
        if (rrule.Count > 0)
        {
            return CalculateLastOccurrenceManually(rrule, startTime, duration);
        }

        // No UNTIL or COUNT means infinite recurrence
        return null;
    }

    /// <summary>
    /// Gets the duration of an event.
    /// </summary>
    private static TimeSpan GetEventDuration(CalendarEvent calendarEvent)
    {
        if (calendarEvent.End != null && calendarEvent.Start != null)
        {
            return calendarEvent.End.AsUtc - calendarEvent.Start.AsUtc;
        }
        return TimeSpan.Zero;
    }

    /// <summary>
    /// Manual calculation for recurrence end based on COUNT.
    /// </summary>
    private static DateTime? CalculateLastOccurrenceManually(RecurrencePattern rrule, DateTime startTime, TimeSpan duration)
    {
        var countNullable = rrule.Count;
        if (!countNullable.HasValue || countNullable.Value <= 0) return null;
        int count = countNullable.Value;
        
        int interval = rrule.Interval > 0 ? rrule.Interval : 1;

        // Calculate intervals between occurrences (count - 1 intervals for count occurrences)
        int intervals = count - 1;

        DateTime lastStart = rrule.Frequency switch
        {
            FrequencyType.Yearly => startTime.AddYears(intervals * interval),
            FrequencyType.Monthly => startTime.AddMonths(intervals * interval),
            FrequencyType.Weekly => startTime.AddDays((double)(intervals * interval * 7)),
            FrequencyType.Daily => startTime.AddDays((double)(intervals * interval)),
            FrequencyType.Hourly => startTime.AddHours((double)(intervals * interval)),
            FrequencyType.Minutely => startTime.AddMinutes((double)(intervals * interval)),
            FrequencyType.Secondly => startTime.AddSeconds((double)(intervals * interval)),
            _ => startTime
        };

        return lastStart.Add(duration);
    }

    /// <summary>
    /// Formats a DateTime for iCalendar format.
    /// </summary>
    private static string FormatDateTime(DateTime dt)
    {
        // Use UTC format for iCalendar
        var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'");
    }
}
