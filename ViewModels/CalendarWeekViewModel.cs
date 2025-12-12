using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using perinma.Storage;

namespace perinma.ViewModels;

public sealed class CalendarWeekViewModel : ViewModelBase
{
    private readonly ICalendarSource _calendarSource;

    public ObservableCollection<EventItemViewModel> Events { get; } = new();
    // Full-day events are kept separate so they don't interfere with timed event column calculations
    public ObservableCollection<EventItemViewModel> FullDayEvents { get; } = new();

    // Monday-based week start (local)
    public DateTime WeekStartLocal { get; private set; }

    public int DayColumns { get; set; } = 5; // Mon-Fri by default to match current view

    private CalendarWeekViewModel(ICalendarSource calendarSource)
    {
        _calendarSource = calendarSource;
        SetCurrentWeekStart();
    }

    public static CalendarWeekViewModel Instance { get; } = new(new DummyCalendarSource(DateTime.Now));

    private void SetCurrentWeekStart()
    {
        var today = DateTime.Today;
        int diff = ((int)today.DayOfWeek + 6) % 7; // Monday=0
        WeekStartLocal = today.AddDays(-diff);
    }

    public void Load()
    {
        Events.Clear();
        FullDayEvents.Clear();

        var start = WeekStartLocal;
        var end = start.AddDays(DayColumns);

        var tieBreaker = 0;

        // Build items
        var allItems = _calendarSource.GetCalendarEvents(start, end).Select(e =>
            {
                // Map start time
                var dayIndex = (int)Math.Clamp((e.StartTime.Date - start.Date).TotalDays, 0, DayColumns - 1);

                // Compute 15-minute slots relative to day start
                var dayStart = start.AddDays(dayIndex);
                var minutesFromDayStart = (int)(e.StartTime - dayStart).TotalMinutes;
                if (minutesFromDayStart < 0) minutesFromDayStart = 0;
                var startSlot = minutesFromDayStart / 15;

                // Duration in 15-minute slots (ensure at least 1 slot)
                var durationMinutes = (int)Math.Max(15, (e.EndTime - e.StartTime).TotalMinutes);
                var durationSlots = Math.Max(1, durationMinutes / 15);

                // Detect all-day events: modeled as midnight-to-midnight spans
                var isFullDay = e.StartTime.TimeOfDay == TimeSpan.Zero && e.EndTime.TimeOfDay == TimeSpan.Zero;

                var vm = new EventItemViewModel
                {
                    Title = e.Title ?? string.Empty,
                    DaySlot = dayIndex,
                    StartSlot = startSlot,
                    // EndSlot represents the inclusive end-slot index
                    EndSlot = startSlot + durationSlots - 1,
                    Color = Color.Parse(e.Calendar.Color ?? string.Empty),
                    TieBreaker = tieBreaker++,
                    ColumnSlot = 0,
                    TotalColumns = 1,
                    IsFullDay = isFullDay,
                };
                return vm;
            })
            .ToList();

        // Partition into full-day and timed events
        var fullDay = allItems
            .Where(i => i.IsFullDay)
            .OrderBy(e => e.DaySlot)
            .ThenBy(e => e.StartSlot)
            .ThenBy(e => e.TieBreaker)
            .ToList();

        var timed = allItems
            .Where(i => !i.IsFullDay)
            .OrderBy(e => e.DaySlot)
            .ThenBy(e => e.StartSlot)
            .ThenBy(e => e.TieBreaker)
            .ToList();

        // Assign columns only for timed events
        AssignEventColumns(timed);

        timed.ForEach(Events.Add);
        fullDay.ForEach(FullDayEvents.Add);
    }

    private static void AssignEventColumns(List<EventItemViewModel> items)
    {
        // Assign columns using competitor discovery.
        EventItemViewModel? lastEvent = null;
        foreach (var ew in items)
        {
            var competingEvent = lastEvent;
            lastEvent = ew;

            if (competingEvent == null || competingEvent.DaySlot != ew.DaySlot)
            {
                // No competition yet
                continue;
            }

            var allCompetitors = FindCompetitors(ew, competingEvent, null);

            // Find first free column among competitors
            var usedColumns = allCompetitors.Select(c => c.ColumnSlot).OrderBy(i => i).ToList();
            for (var i = 0; i < usedColumns.Count; i++)
            {
                if (usedColumns[i] != ew.ColumnSlot)
                {
                    break;
                }

                ew.ColumnSlot++;
            }

            // Link competitors that actually overlap in time
            foreach (var competitor in allCompetitors)
            {
                if (competitor.EndSlot < ew.StartSlot)
                {
                    continue;
                }

                competitor.CompetingWidgets.Add(ew);
                ew.CompetingWidgets.Add(competitor);
            }
        }

        // Compute total columns based on overlapping competitors' assigned columns
        foreach (var ew in items)
        {
            var maxColumn = (from c in ew.CompetingWidgets
                    where !(c.EndSlot < ew.StartSlot || c.StartSlot > ew.EndSlot)
                    select c.ColumnSlot)
                .Prepend(ew.ColumnSlot)
                .Max();
            // Consider direct competitors for now
            ew.TotalColumns = maxColumn + 1;
        }
    }

    // Helpers for column assignment (Go-equivalent)
    private static List<EventItemViewModel> FindCompetitors(
        EventItemViewModel ew,
        EventItemViewModel competitor,
        HashSet<EventItemViewModel>? circuitBreaker)
    {
        circuitBreaker ??= [ew];
        if (!circuitBreaker.Add(competitor)) return new List<EventItemViewModel>();

        var result = new List<EventItemViewModel>();
        if (ew.StartSlot <= competitor.EndSlot && ew.DaySlot == competitor.DaySlot)
        {
            result.Add(competitor);
        }

        foreach (var nextCompetitor in competitor.CompetingWidgets)
        {
            result.AddRange(FindCompetitors(ew, nextCompetitor, circuitBreaker));
        }

        return result;
    }
}

public sealed class EventItemViewModel : ViewModelBase
{
    public string Title { get; set; } = string.Empty;
    public int StartSlot { get; set; }
    public int EndSlot { get; set; } // inclusive end-slot index
    public int DaySlot { get; set; }
    public Color Color { get; set; } = Color.FromArgb(0x99, 0x33, 0x99, 0xFF);

    public int TieBreaker { get; set; }
    public bool IsFullDay { get; set; }

    // Additional fields for column assignment
    public int ColumnSlot { get; set; }
    public int TotalColumns { get; set; } = 1;
    public List<EventItemViewModel> CompetingWidgets { get; } = new();
}