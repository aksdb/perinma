using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;
using perinma.Storage;
using perinma.ViewModels;

namespace perinma.Views.Calendar;

public partial class CalendarWeekViewModel : ViewModelBase
{
    private readonly ICalendarSource _calendarSource;
    private readonly SqliteStorage? _storage;

    public ObservableCollection<EventItem> Events { get; } = [];

    // Full-day events are kept separate so they don't interfere with timed event column calculations
    public ObservableCollection<EventItem> FullDayEvents { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WeekStartOffset))]
    private DateTime _weekStart;

    public DateTimeOffset WeekStartOffset
    {
        get => new(WeekStart);
        set => WeekStart = value.Date;
    }

    [ObservableProperty]
    private int _dayColumns;

    [ObservableProperty]
    private List<WeekDayHeaderViewModel> _weekDayHeaders = [];

    public CalendarWeekViewModel(ICalendarSource calendarSource, SqliteStorage? storage = null)
    {
        _calendarSource = calendarSource;
        _storage = storage;
        DayColumns = 7;
        WeekStart = DateTime.Now;
    }

    partial void OnWeekStartChanged(DateTime value)
    {
        var diff = ((int)value.DayOfWeek + 6) % 7; // Monday=0
        var actualWeekStart = value.Date.AddDays(-diff);

        if (WeekStart == actualWeekStart)
        {
            // We didn't have to correct the DateTime, so we can load the data.
            _weekDayHeaders.ForEach(vm => vm.ReferenceDate = actualWeekStart);
            Load();
        }
        else
        {
            // We had to adjust the DateTime, so we will trigger a new Change event.
            WeekStart = actualWeekStart;
        }
    }

    [RelayCommand]
    private void NextWeek()
    {
        WeekStart = WeekStart.AddDays(7);
    }

    [RelayCommand]
    private void PreviousWeek()
    {
        WeekStart = WeekStart.AddDays(-7);
    }

    [RelayCommand]
    private void Today()
    {
        WeekStart = DateTime.Today;
    }

    public void Load()
    {
        Events.Clear();
        FullDayEvents.Clear();
        
        // TODO: why the fuck is this even initialized to year 0 at one point?!
        //   Make sure we don't actually set that; for now, this is good enough as a workaround.
        if (WeekStart.Year < 1900)
        {
            return;
        }

        var start = WeekStart;
        var end = start.AddDays(DayColumns);

        var tieBreaker = 0;

        // Build items
        var allItems = _calendarSource.GetCalendarEvents(start, end)
            .SelectMany<CalendarEvent, EventItem>(e =>
            {
                var viewModels = new List<EventItem>();

                var effectiveStart = e.StartTime.Date >= start ? e.StartTime : start;
                var startDate = effectiveStart.Date;
                var effectiveEnd = e.EndTime.Date <= end ? e.EndTime : end;
                var endDate = effectiveEnd.Date;

                // Split event into multiple items if it spans multiple days.
                var dayIndex = -1;
                var currentDate = start.Date.AddDays(-1);
                while (true)
                {
                    dayIndex++;
                    currentDate = currentDate.AddDays(1);

                    if (currentDate < startDate)
                    {
                        // This event is not of interest to us, yet.
                        continue;
                    }

                    if (currentDate > endDate)
                    {
                        // Remaining events will not be of interest to us.
                        break;
                    }

                    if (currentDate == effectiveEnd)
                    {
                        // The end of the event is exactly the start of the new day. So it effectively
                        // ends at the last day.
                        break;
                    }

                    var startSlot = 0;
                    var endSlot = 0;
                    if (currentDate == startDate)
                    {
                        startSlot = effectiveStart.Hour * 4 + ((effectiveStart.Minute + 7) / 15);
                    }

                    if (currentDate == endDate)
                    {
                        endSlot = effectiveEnd.Hour * 4 + ((effectiveEnd.Minute + 7) / 15) - 1;
                    }
                    else
                    {
                        endSlot = 24 * 4;
                    }

                    // Detect all-day events: modeled as midnight-to-midnight spans
                    var isFullDay = e.StartTime.TimeOfDay == TimeSpan.Zero && e.EndTime.TimeOfDay == TimeSpan.Zero;

                    var vm = new EventItem
                    {
                        Title = string.IsNullOrEmpty(e.Title) ? "[no title]" : e.Title,
                        DaySlot = dayIndex,
                        StartSlot = startSlot,
                        EndSlot = endSlot,
                        Color = Color.Parse(e.Calendar.Color ?? string.Empty),
                        TieBreaker = tieBreaker++,
                        ColumnSlot = 0,
                        TotalColumns = 1,
                        IsFullDay = isFullDay,
                        StartTimeText = e.StartTime.ToString("HH:mm"),
                        EndTimeText = e.EndTime.ToString("HH:mm"),
                        ShowInlineTimes = true,
                        CalendarEvent = e,
                        Storage = _storage,
                    };
                    viewModels.Add(vm);
                }

                return viewModels;
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

    private static void AssignEventColumns(List<EventItem> items)
    {
        // Assign columns using competitor discovery.
        EventItem? lastEvent = null;
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
            foreach (var t in usedColumns)
            {
                if (t != ew.ColumnSlot)
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
    private static List<EventItem> FindCompetitors(
        EventItem ew,
        EventItem competitor,
        HashSet<EventItem>? circuitBreaker)
    {
        circuitBreaker ??= [ew];
        if (!circuitBreaker.Add(competitor)) return new List<EventItem>();

        var result = new List<EventItem>();
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

    partial void OnDayColumnsChanged(int value)
    {
        var newHeaders = new List<WeekDayHeaderViewModel>();
        for (var i = 0; i < value; i++)
        {
            newHeaders.Add(new WeekDayHeaderViewModel {ReferenceDate = WeekStart, Offset = i});
        }
        WeekDayHeaders = newHeaders;
        Load();
    }
}

public partial class WeekDayHeaderViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveDate))]
    private DateTime _referenceDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveDate))]
    private int _offset;
    
    public DateTime EffectiveDate => ReferenceDate.AddDays(Offset);
}
