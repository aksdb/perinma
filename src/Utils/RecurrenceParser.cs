using System;
using System.Collections.Generic;
using System.Formats.Asn1;
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
        if (calendarEvent.RecurrenceRules.Count == 0)
            return null;

        if (HasOpenEndedRecurrence(calendarEvent.RecurrenceRules))
        {
            // If the rule is open-ended, the event could potentially run forever.
            return DateTime.MaxValue;
        }

        return calendarEvent.GetOccurrences()
            .MaxBy(occurrence => occurrence.Period.EffectiveEndTime)?
            .Period.EffectiveEndTime?.AsUtc;
    }

    private static bool HasOpenEndedRecurrence(IList<RecurrencePattern> recurrencePatterns)
    {
        foreach (var recurrencePattern in recurrencePatterns)
        {
            if (recurrencePattern.Until == null && recurrencePattern.Count == null)
            {
                return true;
            }
        }
        return false;
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
    /// Formats a DateTime for iCalendar format.
    /// </summary>
    private static string FormatDateTime(DateTime dt)
    {
        // Use UTC format for iCalendar
        var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'");
    }
}
