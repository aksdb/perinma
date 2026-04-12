using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NodaTime;
using NodaTime.Extensions;
using perinma.Messaging;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Utils;
using perinma.Views.MessageBox;

namespace perinma.Views.Calendar;

public partial class CalendarWeekViewModel : CalendarViewModelBase, IRecipient<EventsChangedMessage>
{
    [ObservableProperty]
    private CalendarEvent? _selectedEvent;

    public AvaloniaList<EventItem> Events { get; } = [];

    // Full-day events are kept separate so they don't interfere with timed event column calculations
    public AvaloniaList<EventItem> FullDayEvents { get; } = [];

    // Working days array: index 0=Sunday through 6=Saturday
    private bool[] _workingDays = [false, true, true, true, true, true, false];

    // The DayOfWeek (0=Sunday) of the first and last working day in the configured range.
    // Used to compute the work week span.
    private int _firstWorkDay;
    private int _lastWorkDay;

    public override string DateRangeDisplay => DayColumns == 1
        ? ViewStart.ToString("dddd, MMM d, yyyy")
        : FormatDateRange(ViewStart, ViewStart.AddDays(DayColumns - 1));

    private static string FormatDateRange(DateTime start, DateTime end)
    {
        var sameYear = start.Year == end.Year;
        var sameMonth = sameYear && start.Month == end.Month;

        var startFormat = sameYear ? "MMM d" : "MMM d, yyyy";
        var endFormat = sameMonth ? "d, yyyy" : "MMM d, yyyy";

        return $"{start.ToString(startFormat)} - {end.ToString(endFormat)}";
    }

    public DateTimeOffset ViewStartOffset
    {
        get => new(ViewStart);
        set => ViewStart = value.Date;
    }

    [ObservableProperty]
    private int _dayColumns;

    [ObservableProperty]
    private List<WeekDayHeaderViewModel> _weekDayHeaders = [];

    public CalendarWeekViewModel(
        ICalendarSource calendarSource,
        SettingsService? settingsService = null)
        : base(calendarSource, settingsService)
    {
        DayColumns = 7;
        ViewStart = DateTime.Now;
        WeakReferenceMessenger.Default.Register<EventsChangedMessage>(this);
        WeakReferenceMessenger.Default.Register<WorkingDaysChangedMessage>(this, (r, m) => ((CalendarWeekViewModel)r).OnWorkingDaysChanged());
        _ = InitializeWorkingDaysAsync();
    }

    private async Task InitializeWorkingDaysAsync()
    {
        _workingDays = SettingsService != null
            ? await SettingsService.GetWorkingDaysAsync()
            : [false, true, true, true, true, true, false];
        ComputeWorkWeekRange();
    }

    private void ComputeWorkWeekRange()
    {
        var first = -1;
        var last = -1;
        for (var i = 0; i < 7; i++)
        {
            if (_workingDays[i])
            {
                if (first < 0) first = i;
                last = i;
            }
        }

        // Fallback to Mon-Fri if no working days are selected
        if (first < 0)
        {
            first = 1;
            last = 5;
        }

        _firstWorkDay = first;
        _lastWorkDay = last;
    }

    public int WorkWeekDayCount => _lastWorkDay - _firstWorkDay + 1;

    private async void OnWorkingDaysChanged()
    {
        await InitializeWorkingDaysAsync();
        AdjustViewStartForMode(ViewStart);
    }

    protected override void OnViewStartDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(ViewStartOffset));
        UpdateHighlightRange();
        AdjustViewStartForMode(value);
    }

    private void UpdateHighlightRange()
    {
        if (ViewStart.Year < 1900)
            return;

        HighlightStart = ViewStart;
        HighlightEnd = ViewStart.AddDays(DayColumns - 1);
        OnPropertyChanged(nameof(HighlightStart));
        OnPropertyChanged(nameof(HighlightEnd));
    }

    private void AdjustViewStartForMode(DateTime value)
    {
        DateTime adjustedStart;

        // Day view: show the exact selected date.
        if (DayColumns == 1)
        {
            adjustedStart = value.Date;
        }
        // Work week (5 days): snap to the first configured working day of the week.
        else if (DayColumns < 7)
        {
            // Convert to DayOfWeek (0=Sunday) and find the Monday of the containing week
            var currentDow = (int)value.DayOfWeek;
            var mondayOffset = ((currentDow + 6) % 7);
            var monday = value.Date.AddDays(-mondayOffset);
            // _firstWorkDay is a DayOfWeek index (0=Sun, 1=Mon, ...). Offset from Monday is _firstWorkDay - 1.
            adjustedStart = monday.AddDays(_firstWorkDay - 1);
        }
        // Full week: snap to Monday.
        else
        {
            var weekDiff = ((int)value.DayOfWeek + 6) % 7;
            adjustedStart = value.Date.AddDays(-weekDiff);
        }

        if (ViewStart != adjustedStart)
        {
            ViewStart = adjustedStart;
        }
        else
        {
            // Update headers and load data
            WeekDayHeaders.ForEach(vm => vm.ReferenceDate = adjustedStart);
            Load();
        }
    }

    protected override void PerformNavigationNext()
    {
        // Advance by 1 day in day view, otherwise by a full week.
        // Work week (5 days) advances by 7 to jump to the next work week,
        // matching the conventional calendar behavior.
        var step = DayColumns == 1 ? 1 : 7;
        ViewStart = ViewStart.AddDays(step);
        Load();
    }

    protected override void PerformNavigationPrevious()
    {
        var step = DayColumns == 1 ? 1 : 7;
        ViewStart = ViewStart.AddDays(-step);
        Load();
    }

    protected override void PerformNavigationToday()
    {
        ViewStart = DateTime.Today;
        Load();
    }

    public void CreateNewEventInternal(DateTime? startTime, DateTime? endTime)
    {
        var isFullDay = false;
        if (startTime?.Date != endTime?.Date)
        {
            endTime = endTime?.Date.AddDays(-1);
            isFullDay = true;
        }

        OpenEventEditor(
            initialStartTime: startTime,
            initialEndTime: endTime,
            isFullDay: isFullDay
        );
    }

    public override void Load()
    {
        // Clear collections
        Events.Clear();
        FullDayEvents.Clear();

        // TODO: why the fuck is this even initialized to year 0 at one point?! //   Make sure we don't actually set that; for now, this is good enough as a workaround.
        if (ViewStart.Year < 1900)
        {
            return;
        }

        LoadTimeGridView();
    }

    private void LoadTimeGridView()
    {
        var start = ViewStart.ToLocalDateTime();
        var end = start.PlusDays(DayColumns);
        var interval = new Interval(start.ToInstant(), end.ToInstant());

        var tieBreaker = 0;

        // Build items
        var allItems = _calendarSource.GetCalendarEvents(interval)
            .SelectMany<CalendarEvent, EventItem>(e =>
            {
                var viewModels = new List<EventItem>();

                var effectiveStart = e.StartTime >= start ? e.StartTime : start;
                var startDate = effectiveStart.Date;
                var effectiveEnd = e.EndTime <= end ? e.EndTime : end;
                var endDate = effectiveEnd.Date;

                // Split event into multiple items if it spans multiple days.
                var dayIndex = -1;
                var currentDate = start.Date.PlusDays(-1);
                while (true)
                {
                    dayIndex++;
                    currentDate = currentDate.PlusDays(1);

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

                    if (currentDate.AtMidnight() == effectiveEnd)
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
                    var isFullDay = e.Extensions.Get(CalendarEventExtensions.FullDay);

                    // Determine if this event needs a response (not yet accepted, tentative, or declined)
                    var needsResponse = e.ResponseStatus is EventResponseStatus.NeedsAction
                        or EventResponseStatus.Tentative or EventResponseStatus.Declined;

                    // Determine if this event has been declined
                    var isDeclined = e.ResponseStatus == EventResponseStatus.Declined;

                    var vm = new EventItem
                    {
                        Title = string.IsNullOrEmpty(e.Title) ? "[no title]" : e.Title,
                        DaySlot = dayIndex,
                        StartSlot = startSlot,
                        EndSlot = endSlot,
                        Color = string.IsNullOrEmpty(e.Reference.Calendar.Color)
                            ? Color.FromArgb(0x99, 0x33, 0x99, 0xFF)
                            : Color.Parse(e.Reference.Calendar.Color),
                        TieBreaker = tieBreaker++,
                        ColumnSlot = 0,
                        TotalColumns = 1,
                        IsFullDay = isFullDay,
                        StartTimeText = e.StartTime.ToString("HH:mm", null),
                        EndTimeText = e.EndTime.ToString("HH:mm", null),
                        ShowInlineTimes = true,
                        CalendarEvent = e,
                        NeedsResponse = needsResponse,
                        IsDeclined = isDeclined,
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

        Events.AddRange(timed);
        FullDayEvents.AddRange(fullDay);
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
            newHeaders.Add(new WeekDayHeaderViewModel { ReferenceDate = ViewStart, Offset = i });
        }

        WeekDayHeaders = newHeaders;
        UpdateHighlightRange();
        Load();
    }

    public void Receive(EventsChangedMessage message)
    {
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