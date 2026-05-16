using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using perinma.Models;
using NodaTime;
using ICalEvent = Ical.Net.CalendarComponents.CalendarEvent;
using ICalCalendar = Ical.Net.Calendar;

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

        var icalBuilder = new StringBuilder();
        icalBuilder.AppendLine("BEGIN:VCALENDAR");
        icalBuilder.AppendLine("VERSION:2.0");
        icalBuilder.AppendLine("BEGIN:VEVENT");
        icalBuilder.AppendLine($"DTSTART:{FormatDateTime(eventStart)}");
        icalBuilder.AppendLine($"DTEND:{FormatDateTime(eventEnd)}");
        icalBuilder.AppendLine("UID:temp-uid@perinma");

        foreach (var rule in recurrence)
            icalBuilder.AppendLine(rule);

        icalBuilder.AppendLine("END:VEVENT");
        icalBuilder.AppendLine("END:VCALENDAR");

        try
        {
            var calendar = ICalCalendar.Load(icalBuilder.ToString());
            var calendarEvent = calendar?.Events.FirstOrDefault();
            if (calendarEvent != null)
                return CalculateRecurrenceEndTime(calendarEvent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing Google recurrence rule: {ex.Message}");
        }

        return null;
    }

    public static EventRecurrenceInfo GetGoogleRecurrenceInfo(IList<string>? recurrence, LocalDateTime eventStart)
    {
        if (recurrence == null || recurrence.Count == 0)
            return NoRecurrenceInfo();

        var rules = recurrence
            .Where(value => value.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (rules.Count != 1)
            return UnsupportedRecurrenceInfo();

        return TryParseRule(rules[0][6..], eventStart, out var rule)
            ? SupportedRecurrenceInfo(rule!)
            : UnsupportedRecurrenceInfo();
    }

    public static EventRecurrenceInfo GetCalDavRecurrenceInfo(ICalEvent calendarEvent, LocalDateTime eventStart)
    {
        if (calendarEvent.RecurrenceRules.Count == 0)
            return NoRecurrenceInfo();
        if (calendarEvent.RecurrenceRules.Count != 1)
            return UnsupportedRecurrenceInfo();

        return TryParseRule(calendarEvent.RecurrenceRules[0], eventStart, out var rule)
            ? SupportedRecurrenceInfo(rule!)
            : UnsupportedRecurrenceInfo();
    }

    public static string BuildGoogleRecurrence(EventRecurrenceRule rule, LocalDateTime eventStart)
    {
        var parts = new List<string>
        {
            $"FREQ={rule.Frequency.ToString().ToUpperInvariant()}",
        };

        if (rule.Interval > 1)
            parts.Add($"INTERVAL={rule.Interval}");

        if (rule.Frequency == RecurrenceFrequency.Weekly)
        {
            var days = (rule.ByDay.Count > 0 ? rule.ByDay : [eventStart.DayOfWeek])
                .Select(ToIcalDay)
                .ToArray();
            parts.Add($"BYDAY={string.Join(",", days)}");
        }

        if (rule.Count is > 0)
            parts.Add($"COUNT={rule.Count.Value}");
        else if (rule.UntilDate is { } untilDate)
            parts.Add($"UNTIL={BuildUntilValue(untilDate, eventStart)}");

        return $"RRULE:{string.Join(";", parts)}";
    }

    public static RecurrencePattern BuildCalDavPattern(EventRecurrenceRule rule, LocalDateTime eventStart)
    {
        var pattern = new RecurrencePattern
        {
            Frequency = rule.Frequency switch
            {
                RecurrenceFrequency.Daily => FrequencyType.Daily,
                RecurrenceFrequency.Weekly => FrequencyType.Weekly,
                RecurrenceFrequency.Monthly => FrequencyType.Monthly,
                RecurrenceFrequency.Yearly => FrequencyType.Yearly,
                _ => throw new InvalidOperationException($"Unsupported frequency {rule.Frequency}")
            },
            Interval = Math.Max(1, rule.Interval)
        };

        if (rule.Frequency == RecurrenceFrequency.Weekly)
        {
            var days = rule.ByDay.Count > 0 ? rule.ByDay : [eventStart.DayOfWeek];
            pattern.ByDay = days.Select(day => new WeekDay(ToSystemDayOfWeek(day))).ToList();
        }

        if (rule.Count is > 0)
            pattern.Count = rule.Count.Value;
        else if (rule.UntilDate is { } untilDate)
            pattern.Until = new CalDateTime(untilDate.At(new LocalTime(eventStart.Hour, eventStart.Minute)).ToDateTimeUnspecified(), true);

        return pattern;
    }

    public static string BuildSummary(EventRecurrenceRule rule)
    {
        var every = rule.Interval <= 1
            ? $"Every {SingularFrequency(rule.Frequency)}"
            : $"Every {rule.Interval} {PluralFrequency(rule.Frequency, rule.Interval)}";

        if (rule.Frequency == RecurrenceFrequency.Weekly)
        {
            var days = rule.ByDay.Count > 0
                ? string.Join(", ", rule.ByDay.Select(FormatDay))
                : "the start day";
            every = $"{every} on {days}";
        }

        if (rule.Count is > 0)
            return $"{every}, {rule.Count.Value} times";
        if (rule.UntilDate is { } untilDate)
            return $"{every} until {untilDate:MMM d, yyyy}";
        return every;
    }

    /// <summary>
    /// Calculates the recurrence end time from an Ical.Net CalendarEvent.
    /// </summary>
    public static DateTime? CalculateRecurrenceEndTime(ICalEvent calendarEvent)
    {
        if (calendarEvent.RecurrenceRules.Count == 0)
            return null;

        if (HasOpenEndedRecurrence(calendarEvent.RecurrenceRules))
            return DateTime.MaxValue;

        return calendarEvent.GetOccurrences()
            .MaxBy(occurrence => occurrence.Period.EffectiveEndTime)?
            .Period.EffectiveEndTime?.AsUtc;
    }

    private static EventRecurrenceInfo NoRecurrenceInfo() => new()
    {
        Summary = "Does not repeat"
    };

    private static EventRecurrenceInfo UnsupportedRecurrenceInfo() => new()
    {
        IsRecurring = true,
        CanEdit = false,
        Summary = "Custom recurrence"
    };

    private static EventRecurrenceInfo SupportedRecurrenceInfo(EventRecurrenceRule rule) => new()
    {
        IsRecurring = true,
        CanEdit = true,
        Rule = rule,
        Summary = BuildSummary(rule)
    };

    private static bool TryParseRule(string ruleValue, LocalDateTime eventStart, out EventRecurrenceRule? rule)
    {
        rule = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in ruleValue.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var splitIndex = part.IndexOf('=');
            if (splitIndex <= 0 || splitIndex == part.Length - 1)
                return false;

            var key = part[..splitIndex];
            if (!values.TryAdd(key, part[(splitIndex + 1)..]))
                return false;
        }

        foreach (var key in values.Keys)
        {
            if (key is not ("FREQ" or "INTERVAL" or "COUNT" or "UNTIL" or "BYDAY" or "BYMONTHDAY" or "BYMONTH" or "WKST"))
                return false;
        }

        if (!values.TryGetValue("FREQ", out var frequencyValue) ||
            !TryMapFrequency(frequencyValue, out var frequency))
        {
            return false;
        }

        var interval = values.TryGetValue("INTERVAL", out var intervalValue) && int.TryParse(intervalValue, out var parsedInterval)
            ? parsedInterval
            : 1;
        if (interval <= 0)
            return false;

        int? count = null;
        if (values.TryGetValue("COUNT", out var countValue))
        {
            if (!int.TryParse(countValue, out var parsedCount) || parsedCount <= 0)
                return false;
            count = parsedCount;
        }

        LocalDate? untilDate = null;
        if (values.TryGetValue("UNTIL", out var untilValue))
        {
            if (!TryParseUntilDate(untilValue, out var parsedUntil))
                return false;
            untilDate = parsedUntil;
        }

        if (count.HasValue && untilDate.HasValue)
            return false;

        IReadOnlyList<IsoDayOfWeek> byDay = [];
        if (values.TryGetValue("BYDAY", out var byDayValue))
        {
            var parsedDays = new List<IsoDayOfWeek>();
            foreach (var token in byDayValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length != 2 || !TryParseIsoDay(token, out var day))
                    return false;
                parsedDays.Add(day);
            }

            byDay = parsedDays;
        }

        if (values.TryGetValue("BYMONTHDAY", out var byMonthDayValue))
        {
            var monthDays = byMonthDayValue.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (monthDays.Length != 1 || !int.TryParse(monthDays[0], out var day) || day != eventStart.Day)
                return false;
        }

        if (values.TryGetValue("BYMONTH", out var byMonthValue))
        {
            var months = byMonthValue.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (months.Length != 1 || !int.TryParse(months[0], out var month) || month != eventStart.Month)
                return false;
        }

        if (frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly && byDay.Count > 0)
            return false;

        if (frequency == RecurrenceFrequency.Weekly && byDay.Count == 0)
            byDay = [eventStart.DayOfWeek];

        rule = new EventRecurrenceRule
        {
            Frequency = frequency,
            Interval = interval,
            ByDay = byDay,
            Count = count,
            UntilDate = untilDate,
        };
        return true;
    }

    private static bool TryParseRule(RecurrencePattern pattern, LocalDateTime eventStart, out EventRecurrenceRule? rule)
    {
        rule = null;

        if (!TryMapFrequency(pattern.Frequency, out var frequency))
            return false;
        if (pattern.Count.HasValue && pattern.Until != null)
            return false;
        if (pattern.BySetPosition is { Count: > 0 })
            return false;

        var byDay = new List<IsoDayOfWeek>();
        foreach (var day in pattern.ByDay ?? [])
        {
            var token = day.ToString() ?? string.Empty;
            if (token.Length != 2 || !TryParseIsoDay(token, out var parsedDay))
                return false;
            byDay.Add(parsedDay);
        }

        if (frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly && byDay.Count > 0)
            return false;

        if (pattern.ByMonthDay is { Count: > 0 })
        {
            if (pattern.ByMonthDay.Count != 1 || pattern.ByMonthDay[0] != eventStart.Day)
                return false;
        }

        if (pattern.ByMonth is { Count: > 0 })
        {
            if (pattern.ByMonth.Count != 1 || pattern.ByMonth[0] != eventStart.Month)
                return false;
        }

        if (frequency == RecurrenceFrequency.Weekly && byDay.Count == 0)
            byDay.Add(eventStart.DayOfWeek);

        rule = new EventRecurrenceRule
        {
            Frequency = frequency,
            Interval = Math.Max(1, pattern.Interval),
            ByDay = byDay,
            Count = pattern.Count,
            UntilDate = pattern.Until != null ? LocalDate.FromDateTime(pattern.Until.AsUtc.Date) : null,
        };
        return true;
    }

    private static bool TryMapFrequency(string value, out RecurrenceFrequency frequency) => Enum.TryParse<RecurrenceFrequency>(value, true, out frequency);

    private static bool TryMapFrequency(FrequencyType value, out RecurrenceFrequency frequency)
    {
        frequency = value switch
        {
            FrequencyType.Daily => RecurrenceFrequency.Daily,
            FrequencyType.Weekly => RecurrenceFrequency.Weekly,
            FrequencyType.Monthly => RecurrenceFrequency.Monthly,
            FrequencyType.Yearly => RecurrenceFrequency.Yearly,
            _ => default,
        };
        return value is FrequencyType.Daily or FrequencyType.Weekly or FrequencyType.Monthly or FrequencyType.Yearly;
    }

    private static bool HasOpenEndedRecurrence(IList<RecurrencePattern> recurrencePatterns)
    {
        foreach (var recurrencePattern in recurrencePatterns)
        {
            if (recurrencePattern.Until == null && recurrencePattern.Count == null)
                return true;
        }
        return false;
    }

    private static string BuildUntilValue(LocalDate untilDate, LocalDateTime eventStart)
    {
        var until = untilDate.At(new LocalTime(eventStart.Hour, eventStart.Minute));
        return until.ToDateTimeUnspecified().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    private static bool TryParseUntilDate(string value, out LocalDate date)
    {
        date = default;
        if (DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utcDateTime))
        {
            date = LocalDate.FromDateTime(utcDateTime.Date);
            return true;
        }

        if (DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var localDateTime))
        {
            date = LocalDate.FromDateTime(localDateTime.Date);
            return true;
        }

        if (DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            date = LocalDate.FromDateTime(dateOnly.Date);
            return true;
        }

        return false;
    }

    private static bool TryParseIsoDay(string token, out IsoDayOfWeek day)
    {
        day = token.ToUpperInvariant() switch
        {
            "MO" => IsoDayOfWeek.Monday,
            "TU" => IsoDayOfWeek.Tuesday,
            "WE" => IsoDayOfWeek.Wednesday,
            "TH" => IsoDayOfWeek.Thursday,
            "FR" => IsoDayOfWeek.Friday,
            "SA" => IsoDayOfWeek.Saturday,
            "SU" => IsoDayOfWeek.Sunday,
            _ => default,
        };
        return day != default;
    }

    private static string ToIcalDay(IsoDayOfWeek day) => day switch
    {
        IsoDayOfWeek.Monday => "MO",
        IsoDayOfWeek.Tuesday => "TU",
        IsoDayOfWeek.Wednesday => "WE",
        IsoDayOfWeek.Thursday => "TH",
        IsoDayOfWeek.Friday => "FR",
        IsoDayOfWeek.Saturday => "SA",
        IsoDayOfWeek.Sunday => "SU",
        _ => throw new InvalidOperationException($"Unsupported day {day}")
    };

    private static DayOfWeek ToSystemDayOfWeek(IsoDayOfWeek day) => day switch
    {
        IsoDayOfWeek.Monday => DayOfWeek.Monday,
        IsoDayOfWeek.Tuesday => DayOfWeek.Tuesday,
        IsoDayOfWeek.Wednesday => DayOfWeek.Wednesday,
        IsoDayOfWeek.Thursday => DayOfWeek.Thursday,
        IsoDayOfWeek.Friday => DayOfWeek.Friday,
        IsoDayOfWeek.Saturday => DayOfWeek.Saturday,
        IsoDayOfWeek.Sunday => DayOfWeek.Sunday,
        _ => throw new InvalidOperationException($"Unsupported day {day}")
    };

    private static string FormatDay(IsoDayOfWeek day) => day switch
    {
        IsoDayOfWeek.Monday => "Mon",
        IsoDayOfWeek.Tuesday => "Tue",
        IsoDayOfWeek.Wednesday => "Wed",
        IsoDayOfWeek.Thursday => "Thu",
        IsoDayOfWeek.Friday => "Fri",
        IsoDayOfWeek.Saturday => "Sat",
        IsoDayOfWeek.Sunday => "Sun",
        _ => throw new InvalidOperationException($"Unsupported day {day}")
    };

    private static string SingularFrequency(RecurrenceFrequency frequency) => frequency switch
    {
        RecurrenceFrequency.Daily => "day",
        RecurrenceFrequency.Weekly => "week",
        RecurrenceFrequency.Monthly => "month",
        RecurrenceFrequency.Yearly => "year",
        _ => throw new InvalidOperationException($"Unsupported frequency {frequency}")
    };

    private static string PluralFrequency(RecurrenceFrequency frequency, int count) => count == 1
        ? SingularFrequency(frequency)
        : SingularFrequency(frequency) + "s";

    /// <summary>
    /// Formats a DateTime for iCalendar format.
    /// </summary>
    private static string FormatDateTime(DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }
}
