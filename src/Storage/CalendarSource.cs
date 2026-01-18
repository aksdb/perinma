using System;
using System.Collections.Generic;
using System.Linq;
using Google.Apis.Json;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using perinma.Models;
using perinma.Storage.Models;
using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;
using GoogleEventDateTime = Google.Apis.Calendar.v3.Data.EventDateTime;
using ICalCalendar = Ical.Net.Calendar;
using ICalCalendarEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace perinma.Storage;

public interface ICalendarSource
{
    List<CalendarEvent> GetCalendarEvents(DateTime startTime, DateTime endTime);
}

public class DummyCalendarSource : ICalendarSource
{
    private readonly Account _account;
    private readonly Calendar _calRed;
    private readonly Calendar _calBlue;
    private readonly Calendar _calYellow;
    private readonly List<CalendarEvent> _allEvents;

    public DummyCalendarSource(DateTime reference)
    {
        // Create one dummy account and three calendars (red, blue, yellow)
        _account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Dummy Account",
            Type = AccountType.Google
        };

        _calRed = new Calendar
        {
            Account = _account,
            Id = Guid.NewGuid(),
            Name = "Red Calendar",
            Color = "#C80000",
            Enabled = true,
            LastSync = DateTime.Now
        };

        _calBlue = new Calendar
        {
            Account = _account,
            Id = Guid.NewGuid(),
            Name = "Blue Calendar",
            Color = "#0000C8",
            Enabled = true,
            LastSync = DateTime.Now
        };

        _calYellow = new Calendar
        {
            Account = _account,
            Id = Guid.NewGuid(),
            Name = "Yellow Calendar",
            Color = "#C8C800",
            Enabled = true,
            LastSync = DateTime.Now
        };

        int diff = ((int)reference.DayOfWeek + 6) % 7; // Monday=0
        var weekStart = reference.AddDays(-diff);
        _allEvents = BuildEvents(weekStart);
    }

    public List<CalendarEvent> GetCalendarEvents(DateTime startTime, DateTime endTime)
    {
        return (from ev in _allEvents
            let startInside = ev.StartTime > startTime && ev.StartTime < endTime
            let endInside = ev.EndTime > startTime && ev.EndTime < endTime
            where startInside || endInside
            select ev).ToList();
    }

    private static DateTime TruncateDay(DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Kind);
    }

    private List<CalendarEvent> BuildEvents(DateTime reference)
    {
        // Colors are represented by calendar membership
        var day1 = TruncateDay(reference);
        var day2 = day1.AddDays(1);
        var day3 = day2.AddDays(1);
        var day4 = day3.AddDays(1);
        var day5 = day4.AddDays(1);
        var day6 = day5.AddDays(1);
        var day7 = day6.AddDays(1);

        var events = new List<CalendarEvent>
        {
            // Day 1
            Ev(_calRed, day1.AddHours(-5), day1.AddHours(2), ""),
            Ev(_calBlue, day1.AddHours(9), day1.AddHours(12), "Customer Meeting"),
            Ev(_calBlue, day1.AddHours(13).AddMinutes(15), day1.AddHours(13).AddMinutes(45), "Recap"),
            Ev(_calBlue, day1.AddHours(15), day1.AddHours(16).AddMinutes(25), "Chapter 1"),
            Ev(_calBlue, day1.AddHours(16).AddMinutes(30), day1.AddHours(17).AddMinutes(30), "Chapter 2"),

            // Day 2
            Ev(_calYellow, day2.AddHours(14), day2.AddHours(15), "Meeting 1"),
            Ev(_calBlue, day2.AddHours(14).AddMinutes(30), day2.AddHours(15).AddMinutes(30), "Meeting 2"),
            Ev(_calBlue, day2.AddHours(16).AddMinutes(30), day2.AddHours(17).AddMinutes(30), "Chapter 3"),

            // Day 3
            Ev(_calBlue, day3.AddHours(9), day3.AddHours(14), "Team Event"),
            Ev(_calBlue, day3.AddHours(10), day3.AddHours(12), "Get-Together"),
            Ev(_calBlue, day3.AddHours(11), day3.AddHours(13), "Lunch"),
            Ev(_calYellow, day3.AddHours(13).AddMinutes(30), day3.AddHours(15), "Customer Meeting"),

            // Day 4 (dense overlaps)
            Ev(_calYellow, day4.AddHours(9), day4.AddHours(10), "E1"),
            Ev(_calBlue, day4.AddHours(9).AddMinutes(30), day4.AddHours(10).AddMinutes(30), "E2"),
            Ev(_calBlue, day4.AddHours(10), day4.AddHours(11), "E3"),
            Ev(_calRed, day4.AddHours(10).AddMinutes(30), day4.AddHours(11).AddMinutes(30), "E4"),
            Ev(_calBlue, day4.AddHours(11).AddMinutes(30), day4.AddHours(12), "E5"),
            Ev(_calRed, day4.AddHours(14), day4.AddHours(18), "E6"),
            Ev(_calRed, day4.AddHours(14), day4.AddHours(15), "E7"),
            Ev(_calRed, day4.AddHours(14), day4.AddHours(15), "E8"),
            Ev(_calRed, day4.AddHours(14), day4.AddHours(17), "E9"),
            Ev(_calBlue, day4.AddHours(15), day4.AddHours(16), "E10"),
            Ev(_calYellow, day4.AddHours(17), day4.AddHours(18), "E11"),
            Ev(_calRed, day4.AddHours(19), day4.AddHours(22), "E12"),
            Ev(_calRed, day4.AddHours(19), day4.AddHours(20), "E13"),
            Ev(_calRed, day4.AddHours(19), day4.AddHours(20), "E14"),
            Ev(_calRed, day4.AddHours(19), day4.AddHours(23), "E15"),
            Ev(_calBlue, day4.AddHours(20), day4.AddHours(21), "E16"),
            Ev(_calYellow, day4.AddHours(22), day4.AddHours(23), "E17"),

            // Day 5
            Ev(_calBlue, day5.AddHours(9), day5.AddHours(15), "E1"),
            Ev(_calYellow, day5.AddHours(9), day5.AddHours(11), "E2"),
            Ev(_calBlue, day5.AddHours(10), day5.AddHours(12), "E3"),
            Ev(_calRed, day5.AddHours(11), day5.AddHours(16), "E4"),
            Ev(_calYellow, day5.AddHours(15), day5.AddHours(17), "E5"),
            Ev(_calBlue, day5.AddHours(15), day5.AddHours(17), "E6"),
            Ev(_calRed, day5.AddHours(22), day6.AddHours(2), "Party"),

            // Day 7
            Ev(_calRed, day7.AddHours(10), day7.AddHours(11), "Trekking"),
            Ev(_calRed, day7.AddHours(10).AddMinutes(45), day7.AddHours(11).AddMinutes(20), "Phonecall"),
            Ev(_calRed, day7.AddHours(22), day7.AddHours(28), "Party"),
        };

        // All-day events: model as midnight-to-midnight spanning events
        events.AddRange([
            Ev(_calYellow, day1.AddDays(-2), day2, "Conference"),
            Ev(_calRed, day1, day1.AddDays(1), "New Week"),
            Ev(_calRed, day3, day3.AddDays(1), "FD1"),
            Ev(_calRed, day3, day4, "FD2"),
            Ev(_calYellow, day3, day5, "FD3"),
            Ev(_calBlue, day3, day3.AddDays(1), "FD4"),
            Ev(_calBlue, day3, day3.AddDays(1), "FD5")
        ]);

        return events;
    }

    private static CalendarEvent Ev(Calendar cal, DateTime start, DateTime end,
        string title)
    {
        return new CalendarEvent
        {
            Calendar = cal,
            Id = Guid.NewGuid(),
            StartTime = start,
            EndTime = end,
            Title = title,
            ChangedAt = DateTime.Now
        };
    }
}

public class DatabaseCalendarSource : ICalendarSource
{
    private readonly SqliteStorage _storage;

    public DatabaseCalendarSource(SqliteStorage storage)
    {
        _storage = storage;
    }

    public List<CalendarEvent> GetCalendarEvents(DateTime startTime, DateTime endTime)
    {
        var events =
            _storage.GetEventsByTimeRangeAsync(startTime, endTime)
                .GetAwaiter()
                .GetResult()
                .ToList();

        var calendarEvents = new List<CalendarEvent>();

        // For Google events, we need to handle "exception instances" that shadow occurrences
        // of recurring events. These have a RecurringEventId pointing to their parent.
        var googleEvents = events.Where(e => e.AccountTypeEnum == AccountType.Google).ToList();
        var nonGoogleEvents = events.Where(e => e.AccountTypeEnum != AccountType.Google).ToList();

        // Process Google events with shadowing support
        calendarEvents.AddRange(GetGoogleEventsWithShadowing(googleEvents, startTime, endTime));

        // Process non-Google events normally
        foreach (var e in nonGoogleEvents)
        {
            var occurrences = e.AccountTypeEnum switch
            {
                AccountType.CalDav => GetCalDavEventOccurrences(e, startTime, endTime),
                _ => GetFallbackOccurrences(e, startTime, endTime)
            };

            calendarEvents.AddRange(occurrences);
        }

        return calendarEvents;
    }

    private List<CalendarEvent> GetGoogleEventsWithShadowing(
        List<CalendarEventQueryResult> events,
        DateTime queryStart,
        DateTime queryEnd)
    {
        var result = new List<CalendarEvent>();

        // Parse all events and separate into parent recurring events and exception instances
        var parsedEvents = new List<(CalendarEventQueryResult queryResult, GoogleEvent? googleEvent)>();
        foreach (var e in events)
        {
            var googleEvent = TryParseGoogleEvent(e.RawData);
            parsedEvents.Add((e, googleEvent));
        }

        // Group exception instances by their parent's external ID
        // An exception instance has RecurringEventId set to the parent's ID
        var exceptionsByParentId = parsedEvents
            .Where(p => p.googleEvent != null && !string.IsNullOrEmpty(p.googleEvent.RecurringEventId))
            .GroupBy(p => p.googleEvent!.RecurringEventId)
            .ToDictionary(g => g.Key!, g => g.ToList());

        // Process each event
        foreach (var (queryResult, googleEvent) in parsedEvents)
        {
            // Skip exception instances - they'll be handled when processing their parent
            // or added directly if they're standalone modified instances
            if (googleEvent != null && !string.IsNullOrEmpty(googleEvent.RecurringEventId))
            {
                // This is an exception instance - add it if it's not cancelled
                if (googleEvent.Status != "cancelled")
                {
                    result.AddRange(GetGoogleEventOccurrences(queryResult, queryStart, queryEnd));
                }
                continue;
            }

            // Check if this is a recurring event with exceptions
            var parentExternalId = queryResult.ExternalId;
            var hasExceptions = parentExternalId != null && exceptionsByParentId.ContainsKey(parentExternalId);

            if (hasExceptions && googleEvent?.Recurrence != null && googleEvent.Recurrence.Count > 0)
            {
                // This is a recurring event with exception instances - apply shadowing
                var exceptions = exceptionsByParentId[parentExternalId!];
                result.AddRange(GetGoogleRecurringOccurrencesWithShadowing(
                    queryResult, googleEvent, exceptions, queryStart, queryEnd));
            }
            else
            {
                // Regular event (no exceptions) - process normally
                result.AddRange(GetGoogleEventOccurrences(queryResult, queryStart, queryEnd));
            }
        }

        return result;
    }

    private List<CalendarEvent> GetGoogleRecurringOccurrencesWithShadowing(
        CalendarEventQueryResult e,
        GoogleEvent googleEvent,
        List<(CalendarEventQueryResult queryResult, GoogleEvent? googleEvent)> exceptions,
        DateTime queryStart,
        DateTime queryEnd)
    {
        // Build a set of occurrence start times that are shadowed by exceptions (normalized to UTC)
        var shadowedOccurrencesUtc = new List<DateTime>();
        foreach (var (_, exceptionEvent) in exceptions)
        {
            if (exceptionEvent?.OriginalStartTime != null)
            {
                var originalStart = ParseGoogleEventDateTime(exceptionEvent.OriginalStartTime);
                if (originalStart.HasValue)
                {
                    // Normalize to UTC for comparison (ParseGoogleEventDateTime returns local time)
                    shadowedOccurrencesUtc.Add(originalStart.Value.ToUniversalTime());
                }
            }
        }

        // Get all occurrences from the recurrence rule
        var allOccurrences = GetGoogleRecurringOccurrences(e, googleEvent, queryStart, queryEnd);

        // Filter out shadowed occurrences - compare in UTC.
        // Occurrence times from iCal have Kind=Unspecified but represent UTC values
        var result = allOccurrences
            .Where(occ =>
            {
                return shadowedOccurrencesUtc.All(shadow => shadow != occ.StartTime.ToUniversalTime());
            })
            .ToList();

        return result;
    }

    private (DateTime startTime, DateTime endTime) ExtractGoogleEventTimes(CalendarEventQueryResult result)
    {
        var googleEvent = TryParseGoogleEvent(result.RawData);
        if (googleEvent == null)
        {
            return GetFallbackTimes(result);
        }

        var startTime = googleEvent.Start != null
            ? ParseGoogleEventDateTime(googleEvent.Start) ?? GetFallbackStartTime(result)
            : GetFallbackStartTime(result);

        var endTime = googleEvent.End != null
            ? ParseGoogleEventDateTime(googleEvent.End) ?? GetFallbackEndTime(result)
            : GetFallbackEndTime(result);

        return (startTime, endTime);
    }

    private (DateTime startTime, DateTime endTime) ExtractCalDavEventTimes(CalendarEventQueryResult result)
    {
        var calDavEvent = TryParseCalDavEvent(result.RawData);
        if (calDavEvent == null)
        {
            return GetFallbackTimes(result);
        }

        var startTime = calDavEvent.Start != null
            ? ParseCalDavDateTime(calDavEvent.Start) ?? GetFallbackStartTime(result)
            : GetFallbackStartTime(result);

        var endTime = calDavEvent.End != null
            ? ParseCalDavDateTime(calDavEvent.End) ?? GetFallbackEndTime(result)
            : GetFallbackEndTime(result);

        return (startTime, endTime);
    }

    private List<CalendarEvent> GetGoogleEventOccurrences(CalendarEventQueryResult e, DateTime queryStart, DateTime queryEnd)
    {
        var googleEvent = TryParseGoogleEvent(e.RawData);
        if (googleEvent == null)
        {
            return GetFallbackOccurrences(e, queryStart, queryEnd);
        }

        if (googleEvent.Recurrence == null || googleEvent.Recurrence.Count == 0)
        {
            var (eventStartTime, eventEndTime) = ExtractGoogleEventTimes(e);
            return CreateCalendarEvent(e, eventStartTime, eventEndTime);
        }

        return GetGoogleRecurringOccurrences(e, googleEvent, queryStart, queryEnd);
    }

    private List<CalendarEvent> GetCalDavEventOccurrences(CalendarEventQueryResult e, DateTime queryStart, DateTime queryEnd)
    {
        var calDavEvent = TryParseCalDavEvent(e.RawData);
        if (calDavEvent == null)
        {
            return GetFallbackOccurrences(e, queryStart, queryEnd);
        }

        if (calDavEvent.RecurrenceRules.Count == 0)
        {
            var (eventStartTime, eventEndTime) = ExtractCalDavEventTimes(e);
            return CreateCalendarEvent(e, eventStartTime, eventEndTime);
        }

        var baseStartTime = ParseCalDavDateTime(calDavEvent.Start) ?? GetFallbackStartTime(e);
        var baseEndTime = ParseCalDavDateTime(calDavEvent.End) ?? GetFallbackEndTime(e);

        return GetOccurrencesFromICalendarEvent(e, calDavEvent, baseStartTime, baseEndTime, queryStart, queryEnd);
    }

    private List<CalendarEvent> GetFallbackOccurrences(CalendarEventQueryResult e, DateTime queryStart, DateTime queryEnd)
    {
        var (baseStartTime, baseEndTime) = GetFallbackTimes(e);
        return CreateCalendarEvent(e, baseStartTime, baseEndTime);
    }

    private List<CalendarEvent> CreateCalendarEvent(CalendarEventQueryResult e, DateTime eventStartTime, DateTime eventEndTime)
    {
        return
        [
            new CalendarEvent
            {
                Calendar = new Calendar
                {
                    Account = new Account
                    {
                        Id = Guid.Parse(e.AccountId),
                        Name = e.AccountName,
                        Type = e.AccountTypeEnum
                    },
                    Id = Guid.Parse(e.CalendarId),
                    ExternalId = e.CalendarExternalId,
                    Name = e.CalendarName,
                    Color = e.CalendarColor,
                    Enabled = e.CalendarEnabled == 1,
                    LastSync = e.CalendarLastSync.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(e.CalendarLastSync.Value).DateTime
                        : null
                },
                Id = Guid.Parse(e.EventId),
                ExternalId = e.ExternalId,
                StartTime = eventStartTime,
                EndTime = eventEndTime,
                Title = e.Title,
                ChangedAt = e.ChangedAt.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(e.ChangedAt.Value).DateTime
                    : null
            }
        ];
    }

    private List<CalendarEvent> GetGoogleRecurringOccurrences(CalendarEventQueryResult e, GoogleEvent googleEvent, DateTime queryStart, DateTime queryEnd)
    {
        try
        {
            var eventStart = googleEvent.Start != null
                ? ParseGoogleEventDateTime(googleEvent.Start) ?? GetFallbackStartTime(e)
                : GetFallbackStartTime(e);

            var eventEnd = googleEvent.End != null
                ? ParseGoogleEventDateTime(googleEvent.End) ?? GetFallbackEndTime(e)
                : GetFallbackEndTime(e);

            var timeZone = googleEvent.Start?.TimeZone;
            var isAllDayEvent = !string.IsNullOrEmpty(googleEvent.Start?.Date);

            var icalBuilder = new System.Text.StringBuilder();
            icalBuilder.AppendLine("BEGIN:VCALENDAR");
            icalBuilder.AppendLine("VERSION:2.0");
            icalBuilder.AppendLine("BEGIN:VEVENT");
            icalBuilder.AppendLine("UID:temp-uid@perinma");

            if (isAllDayEvent)
            {
                icalBuilder.AppendLine($"DTSTART;VALUE=DATE:{FormatDate(eventStart)}");
                icalBuilder.AppendLine($"DTEND;VALUE=DATE:{FormatDate(eventEnd)}");
            }
            else if (!string.IsNullOrEmpty(timeZone))
            {
                var tzid = timeZone;
                var dtstart = FormatDateTime(eventStart);
                var dtend = FormatDateTime(eventEnd);
                icalBuilder.AppendLine($"DTSTART;TZID={tzid}:{dtstart}");
                icalBuilder.AppendLine($"DTEND;TZID={tzid}:{dtend}");
            }
            else
            {
                icalBuilder.AppendLine($"DTSTART:{FormatDateTime(eventStart.ToUniversalTime())}'Z'");
                icalBuilder.AppendLine($"DTEND:{FormatDateTime(eventEnd.ToUniversalTime())}'Z'");
            }

            foreach (var rule in googleEvent.Recurrence ?? [])
            {
                icalBuilder.AppendLine(rule);
            }

            icalBuilder.AppendLine("END:VEVENT");
            icalBuilder.AppendLine("END:VCALENDAR");

            var icalContent = icalBuilder.ToString();
            var calendar = ICalCalendar.Load(icalContent);
            var calendarEvent = calendar?.Events.FirstOrDefault();

            if (calendarEvent != null)
            {
                return GetOccurrencesFromICalendarEvent(e, calendarEvent, eventStart, eventEnd, queryStart, queryEnd);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing Google recurrence rule: {ex.Message}");
        }

        var fallbackStart = googleEvent.Start != null
            ? ParseGoogleEventDateTime(googleEvent.Start) ?? GetFallbackStartTime(e)
            : GetFallbackStartTime(e);

        var fallbackEnd = googleEvent.End != null
            ? ParseGoogleEventDateTime(googleEvent.End) ?? GetFallbackEndTime(e)
            : GetFallbackEndTime(e);

        return CreateCalendarEvent(e, fallbackStart, fallbackEnd);
    }

    private List<CalendarEvent> GetOccurrencesFromICalendarEvent(CalendarEventQueryResult e, ICalCalendarEvent calDavEvent, DateTime eventStart, DateTime eventEnd, DateTime queryStart, DateTime queryEnd)
    {
        try
        {
            var result = new List<CalendarEvent>();
            var duration = eventEnd - eventStart;
            var queryStartUtc = queryStart.ToUniversalTime();
            var queryEndUtc = queryEnd.ToUniversalTime();

            var occurrences = calDavEvent.GetOccurrences(new CalDateTime(eventStart.ToUniversalTime()));

            // Use AsUtc for filtering to ensure proper UTC comparison
            var filteredOccurrences = occurrences
                .TakeWhile(o => o.Period.StartTime.AsUtc <= queryEndUtc)
                .Where(o => o.Period.StartTime.AsUtc >= queryStartUtc);

            foreach (var occurrence in filteredOccurrences)
            {
                // Use .Value which returns the time as stored in the iCal
                // This preserves consistency with the original time representation
                var occStart = occurrence.Period.StartTime.Value;
                var occEnd = occStart.Add(duration);

                var calendarEvent = new CalendarEvent
                {
                    Calendar = new Calendar
                    {
                        Account = new Account
                        {
                            Id = Guid.Parse(e.AccountId),
                            Name = e.AccountName,
                            Type = e.AccountTypeEnum
                        },
                        Id = Guid.Parse(e.CalendarId),
                        ExternalId = e.CalendarExternalId,
                        Name = e.CalendarName,
                        Color = e.CalendarColor,
                        Enabled = e.CalendarEnabled == 1,
                        LastSync = e.CalendarLastSync.HasValue
                            ? DateTimeOffset.FromUnixTimeSeconds(e.CalendarLastSync.Value).DateTime
                            : null
                    },
                    Id = Guid.Parse(e.EventId),
                    ExternalId = e.ExternalId,
                    StartTime = occStart,
                    EndTime = occEnd,
                    Title = e.Title,
                    ChangedAt = e.ChangedAt.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(e.ChangedAt.Value).DateTime
                        : null
                };

                result.Add(calendarEvent);
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting occurrences for event: {ex.Message}");
            return CreateCalendarEvent(e, eventStart, eventEnd);
        }
    }

    private (DateTime startTime, DateTime endTime) GetFallbackTimes(CalendarEventQueryResult result)
    {
        return (GetFallbackStartTime(result), GetFallbackEndTime(result));
    }

    private DateTime GetFallbackStartTime(CalendarEventQueryResult result)
    {
        return result.StartTime.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(result.StartTime.Value).DateTime
            : DateTime.MinValue;
    }

    private DateTime GetFallbackEndTime(CalendarEventQueryResult result)
    {
        return result.EndTime.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(result.EndTime.Value).DateTime
            : DateTime.MinValue;
    }

    private GoogleEvent? TryParseGoogleEvent(string? rawData)
    {
        if (string.IsNullOrEmpty(rawData))
        {
            return null;
        }

        try
        {
            return NewtonsoftJsonSerializer.Instance.Deserialize<GoogleEvent>(rawData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to parse Google Calendar event: {ex.Message}");
            return null;
        }
    }

    private ICalCalendarEvent? TryParseCalDavEvent(string? rawData)
    {
        if (string.IsNullOrEmpty(rawData))
        {
            return null;
        }

        try
        {
            var calendar = ICalCalendar.Load(rawData);
            return calendar?.Events.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to parse CalDAV iCalendar event: {ex.Message}");
            return null;
        }
    }

    private DateTime? ParseGoogleEventDateTime(GoogleEventDateTime eventDateTime)
    {
        // Handle specific date/time with timezone (most events)
        if (!string.IsNullOrEmpty(eventDateTime.DateTimeRaw))
        {
            if (DateTime.TryParse(eventDateTime.DateTimeRaw, out var parsedDateTime))
            {
                return parsedDateTime;
            }
        }

        // Handle all-day events (date only)
        if (!string.IsNullOrEmpty(eventDateTime.Date))
        {
            if (DateTime.TryParse(eventDateTime.Date, out var parsedDate))
            {
                return parsedDate;
            }
        }

        return null;
    }

    private DateTime? ParseCalDavDateTime(CalDateTime? calDavDateTime)
    {
        if (calDavDateTime == null)
        {
            return null;
        }

        // Use AsUtc to get UTC time, then convert to local time
        // This properly handles timezone information from the iCalendar data
        var utcDateTime = calDavDateTime.AsUtc;

        if (utcDateTime == DateTime.MinValue)
        {
            // If AsUtc fails, fall back to the Value property
            return calDavDateTime.Value;
        }

        return utcDateTime.ToLocalTime();
    }

    private string FormatDateTime(DateTime dt)
    {
        return dt.ToString("yyyyMMdd'T'HHmmss");
    }

    private string FormatDate(DateTime dt)
    {
        return dt.ToString("yyyyMMdd");
    }
}