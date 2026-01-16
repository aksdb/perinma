using NUnit.Framework;
using perinma.Utils;
using Ical.Net;

namespace tests;

[TestFixture]
public class RecurrenceParserTests
{
    [Test]
    public void GetRecurrenceEndTime_WithUntil_ReturnsUntilPlusDuration()
    {
        // RRULE with UNTIL clause - weekly until March 15, 2025
        var recurrence = new List<string>
        {
            "RRULE:FREQ=WEEKLY;UNTIL=20250315T235959Z"
        };
        var eventStart = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 15, 11, 0, 0, DateTimeKind.Utc); // 1 hour duration

        var result = RecurrenceParser.GetRecurrenceEndTime(recurrence, eventStart, eventEnd);

        Assert.That(result, Is.Not.Null);
        // UNTIL is 2025-03-15T23:59:59Z, plus 1 hour duration = 2025-03-16T00:59:59Z
        Assert.That(result!.Value.Year, Is.EqualTo(2025));
        Assert.That(result.Value.Month, Is.EqualTo(3));
        Assert.That(result.Value.Day, Is.GreaterThanOrEqualTo(15));
    }

    [Test]
    public void GetRecurrenceEndTime_WithCount_ReturnsCalculatedEndTime()
    {
        // RRULE with COUNT clause - daily for 10 occurrences
        var recurrence = new List<string>
        {
            "RRULE:FREQ=DAILY;COUNT=10"
        };
        var eventStart = new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 1, 10, 30, 0, DateTimeKind.Utc); // 1.5 hour duration

        var result = RecurrenceParser.GetRecurrenceEndTime(recurrence, eventStart, eventEnd);

        Assert.That(result, Is.Not.Null);
        // 10 daily occurrences starting Jan 1 means last occurrence starts Jan 10
        // Jan 10, 9:00 AM + 1.5 hours = Jan 10, 10:30 AM
        Assert.That(result!.Value.Year, Is.EqualTo(2025));
        Assert.That(result.Value.Month, Is.EqualTo(1));
        Assert.That(result.Value.Day, Is.EqualTo(10));
        Assert.That(result.Value.Hour, Is.EqualTo(10));
        Assert.That(result.Value.Minute, Is.EqualTo(30));
    }

    [Test]
    public void GetRecurrenceEndTime_WithWeeklyCountAndInterval_ReturnsCorrectEndTime()
    {
        // RRULE with COUNT and INTERVAL - every 2 weeks for 5 occurrences
        var recurrence = new List<string>
        {
            "RRULE:FREQ=WEEKLY;INTERVAL=2;COUNT=5"
        };
        var eventStart = new DateTime(2025, 1, 6, 14, 0, 0, DateTimeKind.Utc); // Monday
        var eventEnd = new DateTime(2025, 1, 6, 15, 0, 0, DateTimeKind.Utc); // 1 hour duration

        var result = RecurrenceParser.GetRecurrenceEndTime(recurrence, eventStart, eventEnd);

        Assert.That(result, Is.Not.Null);
        // 5 occurrences every 2 weeks: Jan 6, Jan 20, Feb 3, Feb 17, Mar 3
        // Last occurrence starts Mar 3 at 14:00 + 1 hour = Mar 3 at 15:00
        Assert.That(result!.Value.Year, Is.EqualTo(2025));
        Assert.That(result.Value.Month, Is.EqualTo(3));
        Assert.That(result.Value.Day, Is.EqualTo(3));
    }

    [Test]
    public void GetRecurrenceEndTime_WithNoEndClause_ReturnsNull()
    {
        // RRULE without UNTIL or COUNT - infinite recurrence
        var recurrence = new List<string>
        {
            "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR"
        };
        var eventStart = new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var result = RecurrenceParser.GetRecurrenceEndTime(recurrence, eventStart, eventEnd);

        Assert.That(result, Is.Null); // Infinite recurrence returns null
    }

    [Test]
    public void GetRecurrenceEndTime_WithNullRecurrence_ReturnsNull()
    {
        var eventStart = new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var result = RecurrenceParser.GetRecurrenceEndTime(null, eventStart, eventEnd);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetRecurrenceEndTime_WithEmptyRecurrence_ReturnsNull()
    {
        var recurrence = new List<string>();
        var eventStart = new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var result = RecurrenceParser.GetRecurrenceEndTime(recurrence, eventStart, eventEnd);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetRecurrenceEndTime_WithMonthlyCount_ReturnsCorrectEndTime()
    {
        // RRULE monthly for 6 occurrences
        var recurrence = new List<string>
        {
            "RRULE:FREQ=MONTHLY;COUNT=6"
        };
        var eventStart = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc); // 2 hour duration

        var result = RecurrenceParser.GetRecurrenceEndTime(recurrence, eventStart, eventEnd);

        Assert.That(result, Is.Not.Null);
        // 6 monthly occurrences: Jan, Feb, Mar, Apr, May, Jun 15
        // Last occurrence starts Jun 15 at 10:00 + 2 hours = Jun 15 at 12:00
        Assert.That(result!.Value.Year, Is.EqualTo(2025));
        Assert.That(result.Value.Month, Is.EqualTo(6));
        Assert.That(result.Value.Day, Is.EqualTo(15));
        Assert.That(result.Value.Hour, Is.EqualTo(12));
    }

    [Test]
    public void GetRecurrenceEndTime_WithYearlyCount_ReturnsCorrectEndTime()
    {
        // RRULE yearly for 3 occurrences
        var recurrence = new List<string>
        {
            "RRULE:FREQ=YEARLY;COUNT=3"
        };
        var eventStart = new DateTime(2025, 7, 4, 18, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 7, 4, 21, 0, 0, DateTimeKind.Utc); // 3 hour duration

        var result = RecurrenceParser.GetRecurrenceEndTime(recurrence, eventStart, eventEnd);

        Assert.That(result, Is.Not.Null);
        // 3 yearly occurrences: 2025, 2026, 2027 Jul 4
        // Last occurrence starts Jul 4, 2027 at 18:00 + 3 hours = Jul 4, 2027 at 21:00
        Assert.That(result!.Value.Year, Is.EqualTo(2027));
        Assert.That(result.Value.Month, Is.EqualTo(7));
        Assert.That(result.Value.Day, Is.EqualTo(4));
        Assert.That(result.Value.Hour, Is.EqualTo(21));
    }

    [Test]
    public void CalculateRecurrenceEndTime_WithICalendarEvent_ParsesCorrectly()
    {
        // Test parsing directly from iCalendar format (like CalDAV events)
        var icalData = @"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
DTSTART:20250101T090000Z
DTEND:20250101T100000Z
UID:test@example.com
RRULE:FREQ=DAILY;COUNT=5
END:VEVENT
END:VCALENDAR";

        var calendar = Calendar.Load(icalData);
        var calendarEvent = calendar.Events.First();

        var result = RecurrenceParser.CalculateRecurrenceEndTime(calendarEvent);

        Assert.That(result, Is.Not.Null);
        // 5 daily occurrences starting Jan 1: Jan 1, 2, 3, 4, 5
        // Last occurrence ends Jan 5 at 10:00
        Assert.That(result!.Value.Year, Is.EqualTo(2025));
        Assert.That(result.Value.Month, Is.EqualTo(1));
        Assert.That(result.Value.Day, Is.EqualTo(5));
        Assert.That(result.Value.Hour, Is.EqualTo(10));
    }

    [Test]
    public void CalculateRecurrenceEndTime_WithNoRecurrence_ReturnsNull()
    {
        var icalData = @"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
DTSTART:20250101T090000Z
DTEND:20250101T100000Z
UID:test@example.com
END:VEVENT
END:VCALENDAR";

        var calendar = Calendar.Load(icalData);
        var calendarEvent = calendar.Events.First();

        var result = RecurrenceParser.CalculateRecurrenceEndTime(calendarEvent);

        Assert.That(result, Is.Null); // Non-recurring event returns null
    }
}
