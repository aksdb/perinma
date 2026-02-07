using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;
using perinma.Services;
using perinma.Storage.Models;

namespace perinma.Views.Reminders;

public partial class ReminderViewModel : ViewModelBase
{
    private readonly ReminderService _reminderService;
    private readonly string _reminderId;
    private readonly string _eventId;
    private readonly AccountType _accountType;
    private readonly long _targetTime;

    private bool _startTimeInitialized;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _calendarName = string.Empty;

    [ObservableProperty]
    private string _calendarColor = string.Empty;

    [ObservableProperty]
    private ZonedDateTime? _startTime;

    [ObservableProperty]
    private TimeSpan _timeUntilEvent;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<SnoozeIntervalItem> SnoozeIntervals { get; } = [];

    public ReminderViewModel(ReminderService reminderService, ReminderWithEvent reminder)
    {
        _reminderService = reminderService;
        _reminderId = reminder.ReminderId;
        _eventId = reminder.TargetId;
        _accountType = Enum.TryParse<AccountType>(reminder.AccountType, ignoreCase: true, out var accountType)
            ? accountType
            : throw new ArgumentException($"Unknown account type: {reminder.AccountType}");
        _targetTime = reminder.TargetTime;
        _title = reminder.EventTitle;
        _calendarName = reminder.CalendarName;
        _calendarColor = reminder.CalendarColor ?? "#1976D2";

        InitializeSnoozeIntervals();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_startTimeInitialized)
        {
            return;
        }

        var occurrenceTime = new ZonedDateTime(DateTimeOffset.FromUnixTimeSeconds(_targetTime).LocalDateTime, TimeZoneInfo.Local);
        var startTime = await _reminderService.GetEventStartTimeAsync(_eventId, occurrenceTime, _accountType, cancellationToken);
        if (startTime.HasValue)
        {
            StartTime = startTime.Value;
            TimeUntilEvent = startTime.Value.DateTime - DateTime.Now;
        }
        else
        {
            // Fallback to the stored occurrence time (already local time)
            StartTime = occurrenceTime;
            TimeUntilEvent = occurrenceTime.DateTime - DateTime.Now;
        }

        _startTimeInitialized = true;
    }

    private void InitializeSnoozeIntervals()
    {
        SnoozeIntervals.Add(new SnoozeIntervalItem(SnoozeInterval.OneMinute, "1 minute"));
        SnoozeIntervals.Add(new SnoozeIntervalItem(SnoozeInterval.FiveMinutes, "5 minutes"));
        SnoozeIntervals.Add(new SnoozeIntervalItem(SnoozeInterval.TenMinutes, "10 minutes"));
        SnoozeIntervals.Add(new SnoozeIntervalItem(SnoozeInterval.FifteenMinutes, "15 minutes"));
        SnoozeIntervals.Add(new SnoozeIntervalItem(SnoozeInterval.ThirtyMinutes, "30 minutes"));
        SnoozeIntervals.Add(new SnoozeIntervalItem(SnoozeInterval.OneHour, "1 hour"));
        SnoozeIntervals.Add(new SnoozeIntervalItem(SnoozeInterval.TwoHours, "2 hours"));
        SnoozeIntervals.Add(new SnoozeIntervalItem(SnoozeInterval.Tomorrow, "Tomorrow"));
        SnoozeIntervals.Add(new SnoozeIntervalItem(SnoozeInterval.OneMinuteBeforeStart, "1 minute before start"));
        SnoozeIntervals.Add(new SnoozeIntervalItem(SnoozeInterval.WhenItStarts, "When it starts"));
    }

    [RelayCommand]
    private async Task DismissAsync()
    {
        IsLoading = true;
        try
        {
            await _reminderService.DismissReminderAsync(_reminderId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SnoozeAsync(SnoozeIntervalItem? item)
    {
        if (item == null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            await _reminderService.SnoozeReminderAsync(_reminderId, item.Interval);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SnoozeByIntervalAsync(SnoozeInterval interval)
    {
        IsLoading = true;
        try
        {
            await _reminderService.SnoozeReminderAsync(_reminderId, interval);
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public partial class ReminderListViewModel : ViewModelBase
{
    private readonly ReminderService _reminderService;

    public ObservableCollection<ReminderViewModel> Reminders { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasReminders;

    public event EventHandler? AllRemindersDismissed;

    public ReminderListViewModel(ReminderService reminderService, List<ReminderWithEvent> reminders)
    {
        _reminderService = reminderService;
        HasReminders = reminders.Count > 0;

        foreach (var reminder in reminders)
        {
            var reminderViewModel = new ReminderViewModel(reminderService, reminder);
            reminderViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ReminderViewModel.IsLoading) && s is ReminderViewModel vm && !vm.IsLoading)
                {
                    Reminders.Remove(vm);
                    HasReminders = Reminders.Count > 0;
                    if (!HasReminders)
                    {
                        AllRemindersDismissed?.Invoke(this, EventArgs.Empty);
                    }
                }
            };
            Reminders.Add(reminderViewModel);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var tasks = Reminders.Select(vm => vm.InitializeAsync(cancellationToken));
        await Task.WhenAll(tasks);
    }

    [RelayCommand]
    private void DismissAll()
    {
        foreach (var reminder in Reminders.ToList())
        {
            _ = reminder.DismissCommand.ExecuteAsync(null);
        }
    }
}

public class SnoozeIntervalItem
{
    public SnoozeInterval Interval { get; }
    public string Label { get; }

    public SnoozeIntervalItem(SnoozeInterval interval, string label)
    {
        Interval = interval;
        Label = label;
    }
}
