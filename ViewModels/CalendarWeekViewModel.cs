using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using CommunityToolkit.Mvvm.ComponentModel;
using Lucdem.Avalonia.SourceGenerators.Attributes;
using perinma.Models;
using perinma.Storage;

namespace perinma.ViewModels;

public partial class CalendarWeekViewModel : ViewModelBase
{
    private readonly ICalendarSource _calendarSource;

    public ObservableCollection<EventItemViewModel> Events { get; } = [];

    // Full-day events are kept separate so they don't interfere with timed event column calculations
    public ObservableCollection<EventItemViewModel> FullDayEvents { get; } = [];

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

    private CalendarWeekViewModel(ICalendarSource calendarSource)
    {
        _calendarSource = calendarSource;
        DayColumns = 7;
        WeekStart = DateTime.Now;
    }

    partial void OnWeekStartChanged(DateTime value)
    {
        var diff = ((int)value.DayOfWeek + 6) % 7; // Monday=0
        var actualWeekStart = value.AddDays(-diff);

        if (WeekStart == actualWeekStart)
        {
            return;
        }
        
        WeekStart = actualWeekStart;
        _weekDayHeaders.ForEach(vm => vm.ReferenceDate = actualWeekStart);
        Load();
    }

    public static CalendarWeekViewModel Instance { get; } = new(new DummyCalendarSource(DateTime.Now));

    private void Load()
    {
        Events.Clear();
        FullDayEvents.Clear();

        var start = WeekStart;
        var end = start.AddDays(DayColumns);

        var tieBreaker = 0;

        // Build items
        var allItems = _calendarSource.GetCalendarEvents(start, end)
            .SelectMany<CalendarEvent, EventItemViewModel>(e =>
            {
                var viewModels = new List<EventItemViewModel>();

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

                    var vm = new EventItemViewModel
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

public partial class EventItemViewModel : TemplatedControl
{
    [AvaStyledProperty]
    private string _title = "[no title]";

    [AvaStyledProperty]
    private string _startTimeText = string.Empty;

    [AvaStyledProperty]
    private string _endTimeText = string.Empty;

    public int StartSlot { get; set; }
    public int EndSlot { get; set; } // inclusive end-slot index
    public int DaySlot { get; set; }

    [AvaStyledProperty]
    private Color _color = Color.FromArgb(0x99, 0x33, 0x99, 0xFF);

    [AvaStyledProperty]
    private IBrush _backgroundBrush;

    [AvaStyledProperty]
    private IBrush _foregroundBrush;

    public int TieBreaker { get; set; }
    public bool IsFullDay { get; set; }

    // Additional fields for column assignment
    public int ColumnSlot { get; set; }
    public int TotalColumns { get; set; } = 1;
    public List<EventItemViewModel> CompetingWidgets { get; } = [];

    [AvaStyledProperty]
    private string _inlineTimeText = string.Empty;

    [AvaStyledProperty]
    private Rect? _availableBounds;

    [AvaStyledProperty]
    private bool _showInlineTimes = true;
    
    [AvaStyledProperty]
    private bool _showStackedTimes = false;

    private double _inlineTimeTextWidth;
    
    [AvaStyledProperty]
    private CalendarEvent _calendarEvent;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        switch (change.Property.Name)
        {
            case nameof(AvailableBounds):
                ShowInlineTimes = AvailableBounds?.Width > _inlineTimeTextWidth;
                ShowStackedTimes = !ShowInlineTimes;
                break;
            case nameof(StartTimeText):
            case nameof(EndTimeText):
                InlineTimeText = $"🕐 {StartTimeText}-{EndTimeText}";
                break;
            case nameof(Color):
                BackgroundBrush = new SolidColorBrush(Color, 0.8);
                ForegroundBrush = new SolidColorBrush(ColorUtils.ContrastTextColor(Color));
                break;
            case nameof(InlineTimeText):
            case nameof(FontFamily):
            case nameof(FontSize):
            case nameof(FontStretch):
            case nameof(FontStyle):
            case nameof(FontWeight):
                RecalculateInlineTimeWidth();
                break;
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var border = e.NameScope.Find<Border>("Border");

        CancellationTokenSource? singleTapCtx = null;
        border.Tapped += async (sender, args) =>
        {
            singleTapCtx?.Cancel();
            singleTapCtx = new CancellationTokenSource();
            try
            {
                await Task.Delay(150, singleTapCtx.Token);
                FlyoutBase.ShowAttachedFlyout(border);
            } catch (TaskCanceledException) { }
        };
        border.DoubleTapped += (sender, args) =>
        {
            singleTapCtx?.Cancel();
            Console.Out.WriteLine("Double-tapped");
        };
    }

    private void RecalculateInlineTimeWidth()
    {
        var text = InlineTimeText;
        if (string.IsNullOrWhiteSpace(text))
        {
            _inlineTimeTextWidth = 0;
            return;
        }

        // Measure the text using current font properties
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var layout = new TextLayout(
            text,
            typeface,
            FontSize,
            ForegroundBrush);

        _inlineTimeTextWidth = layout.Width;
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
