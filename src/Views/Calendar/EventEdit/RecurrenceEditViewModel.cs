using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using NodaTime;
using perinma.Models;
using perinma.Utils;

namespace perinma.Views.Calendar.EventEdit;

public enum RecurrenceEndMode
{
    Never,
    AfterCount,
    UntilDate,
}

public sealed record RecurrenceFrequencyOption(RecurrenceFrequency Value, string Label);
public sealed record RecurrenceEndOption(RecurrenceEndMode Value, string Label);

public partial class WeekdaySelectionViewModel : ObservableObject
{
    public IsoDayOfWeek Day { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    public WeekdaySelectionViewModel(IsoDayOfWeek day, string label)
    {
        Day = day;
        Label = label;
    }
}

public partial class RecurrenceEditViewModel : ViewModelBase, IEditableField
{
    private readonly EventRecurrenceInfo _originalInfo;
    private readonly IsoDayOfWeek _defaultDay;

    public string Label => "Repeat";

    public ObservableCollection<RecurrenceFrequencyOption> FrequencyOptions { get; } =
    [
        new(RecurrenceFrequency.Daily, "Daily"),
        new(RecurrenceFrequency.Weekly, "Weekly"),
        new(RecurrenceFrequency.Monthly, "Monthly"),
        new(RecurrenceFrequency.Yearly, "Yearly"),
    ];

    public ObservableCollection<RecurrenceEndOption> EndOptions { get; } =
    [
        new(RecurrenceEndMode.Never, "Never"),
        new(RecurrenceEndMode.AfterCount, "After number of occurrences"),
        new(RecurrenceEndMode.UntilDate, "On date"),
    ];

    public ObservableCollection<WeekdaySelectionViewModel> Weekdays { get; } =
    [
        new(IsoDayOfWeek.Monday, "Mon"),
        new(IsoDayOfWeek.Tuesday, "Tue"),
        new(IsoDayOfWeek.Wednesday, "Wed"),
        new(IsoDayOfWeek.Thursday, "Thu"),
        new(IsoDayOfWeek.Friday, "Fri"),
        new(IsoDayOfWeek.Saturday, "Sat"),
        new(IsoDayOfWeek.Sunday, "Sun"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(HasValue))]
    [NotifyPropertyChangedFor(nameof(ShowEditor))]
    private bool _isRecurring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(ShowEditor))]
    private bool _canEdit = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string? _readOnlyReason;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(ShowWeeklyDays))]
    private RecurrenceFrequencyOption? _selectedFrequency;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _interval = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(ShowEndCount))]
    [NotifyPropertyChangedFor(nameof(ShowEndDate))]
    private RecurrenceEndOption? _selectedEndOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _count = 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private DateTime? _untilDate;

    public bool ShowEditor => IsRecurring && CanEdit;
    public bool ShowWeeklyDays => ShowEditor && SelectedFrequency?.Value == RecurrenceFrequency.Weekly;
    public bool ShowEndCount => ShowEditor && SelectedEndOption?.Value == RecurrenceEndMode.AfterCount;
    public bool ShowEndDate => ShowEditor && SelectedEndOption?.Value == RecurrenceEndMode.UntilDate;

    public string Summary
    {
        get
        {
            if (!IsRecurring)
                return "Does not repeat";
            if (!CanEdit)
                return _originalInfo.Summary;

            var rule = BuildRule();
            return rule == null ? "Repeats" : RecurrenceParser.BuildSummary(rule);
        }
    }

    public bool HasValue => IsRecurring;

    public RecurrenceEditViewModel(EventRecurrenceInfo? recurrenceInfo, bool canEdit, LocalDateTime startTime)
    {
        _originalInfo = recurrenceInfo ?? new EventRecurrenceInfo();
        _defaultDay = startTime.DayOfWeek;

        foreach (var weekday in Weekdays)
            weekday.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary));

        SelectedFrequency = FrequencyOptions.First(option => option.Value == RecurrenceFrequency.Weekly);
        SelectedEndOption = EndOptions.First(option => option.Value == RecurrenceEndMode.Never);
        UntilDate = startTime.Date.PlusMonths(1).ToDateTimeUnspecified();

        if (_originalInfo is { IsRecurring: true, Rule: not null })
        {
            IsRecurring = true;
            SelectedFrequency = FrequencyOptions.First(option => option.Value == _originalInfo.Rule.Frequency);
            Interval = _originalInfo.Rule.Interval;
            foreach (var weekday in Weekdays)
                weekday.IsSelected = _originalInfo.Rule.ByDay.Contains(weekday.Day);

            if (_originalInfo.Rule.Count is > 0)
            {
                SelectedEndOption = EndOptions.First(option => option.Value == RecurrenceEndMode.AfterCount);
                Count = _originalInfo.Rule.Count.Value;
            }
            else if (_originalInfo.Rule.UntilDate is { } untilDate)
            {
                SelectedEndOption = EndOptions.First(option => option.Value == RecurrenceEndMode.UntilDate);
                UntilDate = untilDate.ToDateTimeUnspecified();
            }
        }

        if (!canEdit)
        {
            CanEdit = false;
            ReadOnlyReason = "Edit the entire series to change recurrence.";
            IsRecurring = _originalInfo.IsRecurring;
            return;
        }

        if (!_originalInfo.CanEdit)
        {
            CanEdit = false;
            ReadOnlyReason = "This recurrence pattern can't be edited here yet.";
            IsRecurring = _originalInfo.IsRecurring;
        }
    }

    public EventRecurrenceInfo GetRecurrenceInfo()
    {
        if (!CanEdit)
            return _originalInfo;
        if (!IsRecurring)
            return new EventRecurrenceInfo { Summary = "Does not repeat" };

        var rule = BuildRule();
        if (rule == null)
            return _originalInfo;

        return new EventRecurrenceInfo
        {
            IsRecurring = true,
            CanEdit = true,
            Rule = rule,
            Summary = RecurrenceParser.BuildSummary(rule)
        };
    }

    private EventRecurrenceRule? BuildRule()
    {
        if (SelectedFrequency == null || Interval <= 0)
            return null;

        var byDay = SelectedFrequency.Value == RecurrenceFrequency.Weekly
            ? Weekdays.Where(day => day.IsSelected).Select(day => day.Day).ToList()
            : [];
        if (SelectedFrequency.Value == RecurrenceFrequency.Weekly && byDay.Count == 0)
            byDay = [_defaultDay];

        return new EventRecurrenceRule
        {
            Frequency = SelectedFrequency.Value,
            Interval = Math.Max(1, Interval),
            ByDay = byDay,
            Count = SelectedEndOption?.Value == RecurrenceEndMode.AfterCount ? Math.Max(1, Count) : null,
            UntilDate = SelectedEndOption?.Value == RecurrenceEndMode.UntilDate && UntilDate.HasValue
                ? LocalDate.FromDateTime(UntilDate.Value.Date)
                : null,
        };
    }
}
