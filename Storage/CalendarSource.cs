using System;
using System.Collections.Generic;
using System.Linq;
using perinma.Models;

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
            Type = "dummy"
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

        _allEvents = BuildEvents(reference);
    }

    public List<CalendarEvent> GetCalendarEvents(DateTime startTime, DateTime endTime)
    {
        return (from ev in _allEvents 
            let startInside = ev.StartTime > startTime && ev.StartTime < endTime
            let endInside = ev.EndTime > startTime && ev.EndTime < endTime
            where startInside || endInside select ev).ToList();
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
            Ev(_calRed,  day1.AddHours(-5),               day1.AddHours(2),                ""),
            Ev(_calBlue, day1.AddHours(9),                day1.AddHours(12),               "Customer Meeting"),
            Ev(_calBlue, day1.AddHours(13).AddMinutes(15),day1.AddHours(13).AddMinutes(45),"Recap"),
            Ev(_calBlue, day1.AddHours(15),               day1.AddHours(16).AddMinutes(25),"Chapter 1"),
            Ev(_calBlue, day1.AddHours(16).AddMinutes(30),day1.AddHours(17).AddMinutes(30),"Chapter 2"),

            // Day 2
            Ev(_calYellow, day2.AddHours(14),                day2.AddHours(15),                 "Meeting 1"),
            Ev(_calBlue,   day2.AddHours(14).AddMinutes(30), day2.AddHours(15).AddMinutes(30),  "Meeting 2"),
            Ev(_calBlue,   day2.AddHours(16).AddMinutes(30), day2.AddHours(17).AddMinutes(30),  "Chapter 3"),

            // Day 3
            Ev(_calBlue,   day3.AddHours(9),                 day3.AddHours(14),                 "Team Event"),
            Ev(_calBlue,   day3.AddHours(10),                day3.AddHours(12),                 "Get-Together"),
            Ev(_calBlue,   day3.AddHours(11),                day3.AddHours(13),                 "Lunch"),
            Ev(_calYellow, day3.AddHours(13).AddMinutes(30), day3.AddHours(15),                 "Customer Meeting"),

            // Day 4 (dense overlaps)
            Ev(_calYellow, day4.AddHours(9),                 day4.AddHours(10),                 "E1"),
            Ev(_calBlue,   day4.AddHours(9).AddMinutes(30),  day4.AddHours(10).AddMinutes(30),  "E2"),
            Ev(_calBlue,   day4.AddHours(10),                day4.AddHours(11),                 "E3"),
            Ev(_calRed,    day4.AddHours(10).AddMinutes(30), day4.AddHours(11).AddMinutes(30),  "E4"),
            Ev(_calBlue,   day4.AddHours(11).AddMinutes(30), day4.AddHours(12),                 "E5"),
            Ev(_calRed,    day4.AddHours(14),                day4.AddHours(18),                 "E6"),
            Ev(_calRed,    day4.AddHours(14),                day4.AddHours(15),                 "E7"),
            Ev(_calRed,    day4.AddHours(14),                day4.AddHours(15),                 "E8"),
            Ev(_calRed,    day4.AddHours(14),                day4.AddHours(17),                 "E9"),
            Ev(_calBlue,   day4.AddHours(15),                day4.AddHours(16),                 "E10"),
            Ev(_calYellow, day4.AddHours(17),                day4.AddHours(18),                 "E11"),
            Ev(_calRed,    day4.AddHours(19),                day4.AddHours(22),                 "E12"),
            Ev(_calRed,    day4.AddHours(19),                day4.AddHours(20),                 "E13"),
            Ev(_calRed,    day4.AddHours(19),                day4.AddHours(20),                 "E14"),
            Ev(_calRed,    day4.AddHours(19),                day4.AddHours(23),                 "E15"),
            Ev(_calBlue,   day4.AddHours(20),                day4.AddHours(21),                 "E16"),
            Ev(_calYellow, day4.AddHours(22),                day4.AddHours(23),                 "E17"),

            // Day 5
            Ev(_calBlue,   day5.AddHours(9),                 day5.AddHours(15),                 "E1"),
            Ev(_calYellow, day5.AddHours(9),                 day5.AddHours(11),                 "E2"),
            Ev(_calBlue,   day5.AddHours(10),                day5.AddHours(12),                 "E3"),
            Ev(_calRed,    day5.AddHours(11),                day5.AddHours(16),                 "E4"),
            Ev(_calYellow, day5.AddHours(15),                day5.AddHours(17),                 "E5"),
            Ev(_calBlue,   day5.AddHours(15),                day5.AddHours(17),                 "E6"),
            Ev(_calRed,    day5.AddHours(22),                day6.AddHours(2),                  "Party"),

            // Day 7
            Ev(_calRed,    day7.AddHours(10),                day7.AddHours(11),                 "Trekking"),
            Ev(_calRed,    day7.AddHours(10).AddMinutes(45), day7.AddHours(11).AddMinutes(20),  "Phonecall"),
            Ev(_calRed,    day7.AddHours(22),                day7.AddHours(28),                 "Party"),
        };

        // All-day events: model as midnight-to-midnight spanning events
        events.AddRange([
            Ev(_calYellow, day1.AddDays(-2), day2, "Conference"),
            Ev(_calRed,    day1,             day1.AddDays(1), "New Week"),
            Ev(_calRed,    day3,             day3.AddDays(1), "FD1"),
            Ev(_calRed,    day3,             day4,            "FD2"),
            Ev(_calYellow, day3,             day5,            "FD3"),
            Ev(_calBlue,   day3,             day3.AddDays(1), "FD4"),
            Ev(_calBlue,   day3,             day3.AddDays(1), "FD5")
        ]);

        return events;
    }

    private static CalendarEvent Ev(Calendar cal, DateTime start, DateTime end, string title)
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