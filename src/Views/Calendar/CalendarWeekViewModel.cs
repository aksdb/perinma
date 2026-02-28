using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
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

    public ObservableCollection<EventItem> Events { get; } = [];

    // Full-day events are kept separate so they don't interfere with timed event column calculations
    public ObservableCollection<EventItem> FullDayEvents { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewStartOffset))]
    [NotifyPropertyChangedFor(nameof(DateRangeDisplay))]
    private DateTime _viewStart;



    public string DateRangeDisplay
    {
        get
        {
            return FormatDateRange(ViewStart, ViewStart.AddDays(DayColumns - 1));
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
        SettingsService? settingsService = null)
        : base(calendarSource, settingsService)
    {
        DayColumns = 7;
        ViewStart = DateTime.Now;
    }



    partial void OnViewStartChanged(DateTime value)
    {
        AdjustViewStartForMode(value);
    }

    private void AdjustViewStartForMode(DateTime value)
    {
        // Start at Monday of the week
        var weekDiff = ((int)value.DayOfWeek + 6) % 7;
        var adjustedStart = value.Date.AddDays(-weekDiff);

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
        ViewStart = ViewStart.AddDays(7);
        Load();
    }

    [RelayCommand]
    private void Previous()
    {
        ViewStart = ViewStart.AddDays(-7);
        Load();
    }

    [RelayCommand]
    private void Today()
    {
        ViewStart = DateTime.Today;
        Load();
    }

    [RelayCommand]
    private void CreateNewEvent()
    {
        var onCompleted = new Action<string>(async (errorMessage) =>
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                await MessageBoxWindow.ShowAsync(
                    null,
                    "Error",
                    errorMessage,
                    MessageBoxType.Error,
                    MessageBoxButtons.Ok);
            }
            else
            {
                Load();
            }
        });

        var editor = new EventEditView
        {
            DataContext = new EventEditViewModel(
                null,
                null,
                onCompleted)
        };
        editor.Show();
    }

    public void Load()
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
            newHeaders.Add(new WeekDayHeaderViewModel { ReferenceDate = ViewStart, Offset = i });
        }

        WeekDayHeaders = newHeaders;
        Load();
    }

    protected override void OnEventDeleted()
    {
        Load();
    }

    protected override void OnEventChanged()
    {
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