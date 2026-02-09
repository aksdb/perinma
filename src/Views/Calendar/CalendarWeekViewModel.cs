using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NodaTime;
using NodaTime.Extensions;
using perinma.Models;
using perinma.Services;
using perinma.Storage;

namespace perinma.Views.Calendar;

public partial class CalendarWeekViewModel : ViewModelBase
{
    private readonly ICalendarSource _calendarSource;
    private readonly SqliteStorage? _storage;
    private readonly IReadOnlyDictionary<AccountType, ICalendarProvider>? _providers;

    public SqliteStorage? Storage => _storage;
    public IReadOnlyDictionary<AccountType, ICalendarProvider>? Providers => _providers;

    [ObservableProperty]
    private CalendarEvent? _selectedEvent;

    public SettingsService? SettingsService { get; }

    public ObservableCollection<EventItem> Events { get; } = [];

    // Full-day events are kept separate so they don't interfere with timed event column calculations
    public ObservableCollection<EventItem> FullDayEvents { get; } = [];

    // Month view data
    public ObservableCollection<MonthDayViewModel> MonthDays { get; } = [];

    // Agenda/List view data
    public ObservableCollection<AgendaDayViewModel> AgendaDays { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewStartOffset))]
    [NotifyPropertyChangedFor(nameof(DateRangeDisplay))]
    private DateTime _viewStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateRangeDisplay))]
    [NotifyPropertyChangedFor(nameof(IsMonthView))]
    [NotifyPropertyChangedFor(nameof(IsWeekView))]
    [NotifyPropertyChangedFor(nameof(IsFiveDaysView))]
    [NotifyPropertyChangedFor(nameof(IsDayView))]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    [NotifyPropertyChangedFor(nameof(IsTimeGridView))]
    private CalendarViewMode _viewMode = CalendarViewMode.Week;

    public bool IsMonthView => ViewMode == CalendarViewMode.Month;
    public bool IsWeekView => ViewMode == CalendarViewMode.Week;
    public bool IsFiveDaysView => ViewMode == CalendarViewMode.FiveDays;
    public bool IsDayView => ViewMode == CalendarViewMode.Day;
    public bool IsListView => ViewMode == CalendarViewMode.List;

    /// <summary>
    /// True for views that show a time grid (Week, FiveDays, Day). False for Month and List views.
    /// </summary>
    public bool IsTimeGridView =>
        ViewMode is CalendarViewMode.Week or CalendarViewMode.FiveDays or CalendarViewMode.Day;

    public string DateRangeDisplay
    {
        get
        {
            return ViewMode switch
            {
                CalendarViewMode.Month => ViewStart.ToString("MMMM yyyy"),
                CalendarViewMode.Day => ViewStart.ToString("dddd, MMMM d, yyyy"),
                CalendarViewMode.List => $"Upcoming from {ViewStart:MMM d, yyyy}",
                _ => FormatDateRange(ViewStart, ViewStart.AddDays(DayColumns - 1))
            };
        }
    }

    private static string FormatDateRange(DateTime start, DateTime end)
    {
        var sameYear = start.Year == end.Year;
        var sameMonth = sameYear && start.Month == end.Month;

        var startFormat = sameYear ? "MMM d" : "MMM d, yyyy";
        var endFormat = sameMonth ? "d, yyyy" : "MMM d, yyyy";

        return $"{start.ToString(startFormat)} - {end.ToString(endFormat)}";
    }

    // Legacy property for backward compatibility with CalendarListView's CalendarPicker
    public DateTime WeekStart
    {
        get => ViewStart;
        set => ViewStart = value;
    }

    // Legacy property for backward compatibility
    public string WeekDisplay => DateRangeDisplay;

    public DateTimeOffset ViewStartOffset
    {
        get => new(ViewStart);
        set => ViewStart = value.Date;
    }

    // Legacy property for backward compatibility
    public DateTimeOffset WeekStartOffset
    {
        get => ViewStartOffset;
        set => ViewStartOffset = value;
    }

    [ObservableProperty]
    private int _dayColumns;

    [ObservableProperty]
    private List<WeekDayHeaderViewModel> _weekDayHeaders = [];

    public CalendarWeekViewModel(
        ICalendarSource calendarSource,
        SqliteStorage? storage = null,
        SettingsService? settingsService = null,
        IReadOnlyDictionary<AccountType, ICalendarProvider>? providers = null)
    {
        _calendarSource = calendarSource;
        _storage = storage;
        _providers = providers;
        SettingsService = settingsService;
        DayColumns = 7;
        ViewStart = DateTime.Now;
    }

    public async Task InitializeAsync()
    {
        if (SettingsService == null)
            return;

        try
        {
            var lastViewMode = await SettingsService.GetLastCalendarViewModeAsync();
            if (Enum.TryParse<CalendarViewMode>(lastViewMode, out var savedViewMode))
            {
                ViewMode = savedViewMode;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load last calendar view mode: {ex.Message}");
        }
    }

    partial void OnViewModeChanged(CalendarViewMode value)
    {
        // Update day columns based on view mode
        DayColumns = value switch
        {
            CalendarViewMode.Month => 7, // Month view still uses 7 columns for the grid
            CalendarViewMode.Week => 7,
            CalendarViewMode.FiveDays => 5,
            CalendarViewMode.Day => 1,
            CalendarViewMode.List => 1, // List view doesn't use columns but needs a value
            _ => 7
        };

        // Recalculate view start based on new mode
        AdjustViewStartForMode(ViewStart);
    }

    partial void OnViewStartChanged(DateTime value)
    {
        AdjustViewStartForMode(value);
    }

    private void AdjustViewStartForMode(DateTime value)
    {
        DateTime adjustedStart;

        switch (ViewMode)
        {
            case CalendarViewMode.Month:
                // Start at first day of month
                adjustedStart = new DateTime(value.Year, value.Month, 1);
                break;

            case CalendarViewMode.Week:
                // Start at Monday of the week
                var weekDiff = ((int)value.DayOfWeek + 6) % 7;
                adjustedStart = value.Date.AddDays(-weekDiff);
                break;

            case CalendarViewMode.FiveDays:
                // Start at Monday of the week (work week)
                var fiveDayDiff = ((int)value.DayOfWeek + 6) % 7;
                adjustedStart = value.Date.AddDays(-fiveDayDiff);
                break;

            case CalendarViewMode.Day:
            case CalendarViewMode.List:
                // Just use the date as-is
                adjustedStart = value.Date;
                break;

            default:
                adjustedStart = value.Date;
                break;
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

    [RelayCommand]
    private void Next()
    {
        ViewStart = ViewMode switch
        {
            CalendarViewMode.Month => ViewStart.AddMonths(1),
            CalendarViewMode.Week => ViewStart.AddDays(7),
            CalendarViewMode.FiveDays => ViewStart.AddDays(7),
            CalendarViewMode.Day => ViewStart.AddDays(1),
            CalendarViewMode.List => ViewStart.AddDays(7),
            _ => ViewStart.AddDays(7)
        };
    }

    [RelayCommand]
    private void Previous()
    {
        ViewStart = ViewMode switch
        {
            CalendarViewMode.Month => ViewStart.AddMonths(-1),
            CalendarViewMode.Week => ViewStart.AddDays(-7),
            CalendarViewMode.FiveDays => ViewStart.AddDays(-7),
            CalendarViewMode.Day => ViewStart.AddDays(-1),
            CalendarViewMode.List => ViewStart.AddDays(-7),
            _ => ViewStart.AddDays(-7)
        };
    }

    [RelayCommand]
    private void Today()
    {
        ViewStart = DateTime.Today;
    }

    [RelayCommand]
    private void SetViewMode(CalendarViewMode mode)
    {
        ViewMode = mode;
    }

    // Legacy commands for backward compatibility
    [RelayCommand]
    private void NextWeek() => Next();

    [RelayCommand]
    private void PreviousWeek() => Previous();

    public void Load()
    {
        Events.Clear();
        FullDayEvents.Clear();
        MonthDays.Clear();
        AgendaDays.Clear();

        // TODO: why the fuck is this even initialized to year 0 at one point?!
        //   Make sure we don't actually set that; for now, this is good enough as a workaround.
        if (ViewStart.Year < 1900)
        {
            return;
        }

        switch (ViewMode)
        {
            case CalendarViewMode.Month:
                LoadMonthView();
                break;
            case CalendarViewMode.List:
                LoadAgendaView();
                break;
            default:
                LoadTimeGridView();
                break;
        }
    }

    private void LoadTimeGridView()
    {
        var start = ViewStart;
        var end = start.AddDays(DayColumns);
        var interval = new Interval(start.ToUniversalTime().ToInstant(), end.ToUniversalTime().ToInstant());
        
        var tieBreaker = 0;

        // Build items
        var allItems = _calendarSource.GetCalendarEvents(interval)
            .SelectMany<CalendarEvent, EventItem>(e =>
            {
                var viewModels = new List<EventItem>();

                var startLocalDate = LocalDate.FromDateTime(start);
                var endLocalDate = LocalDate.FromDateTime(end);
                var effectiveStart = e.StartTime.Date >= startLocalDate ? e.StartTime : LocalDateTime.FromDateTime(start);
                var startDate = effectiveStart.Date;
                var effectiveEnd = e.EndTime.Date <= endLocalDate ? e.EndTime : LocalDateTime.FromDateTime(end);
                var endDate = effectiveEnd.Date;

                // Split event into multiple items if it spans multiple days.
                var dayIndex = -1;
                var currentDate = LocalDate.FromDateTime(start.Date).PlusDays(-1);
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

                    if (currentDate == effectiveEnd.Date)
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
                    var isFullDay = e.StartTime.TimeOfDay == LocalTime.Midnight && e.StartTime.TimeOfDay == LocalTime.Midnight;

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
                        Storage = _storage,
                        Providers = _providers,
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

        timed.ForEach(Events.Add);
        fullDay.ForEach(FullDayEvents.Add);
    }

    private void LoadMonthView()
    {
        var firstOfMonth = new DateTime(ViewStart.Year, ViewStart.Month, 1);
        var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);

        // Find the Monday before or on the first of the month
        var startOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        var gridStart = firstOfMonth.AddDays(-startOffset);

        // Always show 6 weeks (42 days) for consistent grid
        var gridEnd = gridStart.AddDays(42);

        // Get all events for the visible range
        var events = _calendarSource.GetCalendarEvents(new Interval(gridStart.ToInstant(), gridEnd.ToInstant())).ToList();

        // Build day view models
        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            var dayVm = new MonthDayViewModel
            {
                Date = date,
                DayNumber = date.Day,
                IsCurrentMonth = date.Month == ViewStart.Month,
                IsToday = date.Date == DateTime.Today
            };

            // Add events for this day
            var dayEvents = events
                .Where(e => e.StartTime.Date <= LocalDate.FromDateTime(date) && e.EndTime.Date >= LocalDate.FromDateTime(date))
                .OrderBy(e => e.StartTime);

            foreach (var evt in dayEvents)
            {
                var isFullDay = evt.StartTime.TimeOfDay == LocalTime.Midnight && evt.EndTime.TimeOfDay == LocalTime.Midnight;
                dayVm.Events.Add(new MonthEventViewModel
                {
                    Title = string.IsNullOrEmpty(evt.Title) ? "[no title]" : evt.Title,
                    Color = string.IsNullOrEmpty(evt.Reference.Calendar.Color)
                        ? Color.FromArgb(0x99, 0x33, 0x99, 0xFF)
                        : Color.Parse(evt.Reference.Calendar.Color),
                    CalendarEvent = evt,
                    IsFullDay = isFullDay,
                    TimeText = isFullDay ? string.Empty : evt.StartTime.ToString("HH:mm", null),
                    Storage = _storage,
                    Providers = _providers
                });
            }

            MonthDays.Add(dayVm);
        }
    }

    private void LoadAgendaView()
    {
        // Show 14 days in agenda view
        const int agendaDays = 14;
        var start = ViewStart;
        var end = start.AddDays(agendaDays);
        var systemZone = DateTimeZoneProviders.Tzdb.GetSystemDefault();

        var events = _calendarSource.GetCalendarEvents(new Interval(start.ToInstant(), end.ToInstant()))
            .OrderBy(e => e.StartTime)
            .ToList();

        // Group events by day
        for (var i = 0; i < agendaDays; i++)
        {
            var date = start.AddDays(i);
            var dayVm = new AgendaDayViewModel
            {
                Date = date,
                IsToday = date.Date == DateTime.Today
            };

            var dateLocal = LocalDate.FromDateTime(date);
            var dayEvents = events
                .Where(e => e.StartTime.Date == dateLocal || (e.StartTime.Date < dateLocal && e.EndTime.Date >= dateLocal))
                .OrderBy(e => e.StartTime.TimeOfDay == LocalTime.Midnight ? TimeSpan.MinValue : new TimeSpan(e.StartTime.TimeOfDay.Hour, e.StartTime.TimeOfDay.Minute, e.StartTime.TimeOfDay.Second));

            foreach (var evt in dayEvents)
            {
                var startLocal = evt.StartTime;
                var endLocal = evt.EndTime;
                var isFullDay = startLocal.TimeOfDay == LocalTime.Midnight && endLocal.TimeOfDay == LocalTime.Midnight;
                dayVm.Events.Add(new AgendaEventViewModel
                {
                    Title = string.IsNullOrEmpty(evt.Title) ? "[no title]" : evt.Title,
                    StartTime = startLocal.ToDateTimeUnspecified(),
                    EndTime = endLocal.ToDateTimeUnspecified(),
                    IsFullDay = isFullDay,
                    Color = string.IsNullOrEmpty(evt.Reference.Calendar.Color)
                        ? Color.FromArgb(0x99, 0x33, 0x99, 0xFF)
                        : Color.Parse(evt.Reference.Calendar.Color),
                    CalendarName = evt.Reference.Calendar.Name,
                    CalendarEvent = evt,
                    Storage = _storage,
                    Providers = _providers
                });
            }

            AgendaDays.Add(dayVm);
        }
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
        Load();
    }

    [RelayCommand]
    private void OpenEventEditor(CalendarEvent? existingEvent)
    {
        if (_calendarSource == null)
            return;

        SelectedEvent = existingEvent;
    }

    [RelayCommand]
    private void CloseEventEditor()
    {
        SelectedEvent = null;
    }

    [RelayCommand]
    private void CreateNewEvent()
    {
        SelectedEvent = null;
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