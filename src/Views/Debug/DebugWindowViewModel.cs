using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;
using perinma.Services;
using perinma.Views.Reminders;

namespace perinma.Views.Debug;

public partial class DebugWindowViewModel : ObservableObject
{
    private readonly ReminderService _reminderService;
    private readonly Func<IReadOnlyList<CalendarEvent>> _triggerEventsProvider;
    private readonly Func<string> _triggerRangeDescriptionProvider;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _totalCalendars;

    [ObservableProperty]
    private int _totalEvents;

    [ObservableProperty]
    private int _eventsProcessed;

    [ObservableProperty]
    private ObservableCollection<string> _errors = new();

    [ObservableProperty]
    private string _triggerStatusText = string.Empty;

    [ObservableProperty]
    private string _triggerRangeDescription = string.Empty;

    [ObservableProperty]
    private bool _isTriggeringReminders;

    public ObservableCollection<DebugTriggerEventItemViewModel> TriggerEvents { get; } = [];
    public ObservableCollection<string> TriggerErrors { get; } = [];

    public int ErrorCount => Errors.Count;
    public bool HasErrors => Errors.Count > 0;
    public bool HasTriggerErrors => TriggerErrors.Count > 0;
    public bool HasTriggerEvents => TriggerEvents.Count > 0;
    public bool HasNoTriggerEvents => TriggerEvents.Count == 0;
    public int SelectedTriggerEventCount => TriggerEvents.Count(item => item.IsSelected);

    public DebugWindowViewModel(
        ReminderService reminderService,
        Func<IReadOnlyList<CalendarEvent>>? triggerEventsProvider = null,
        Func<string>? triggerRangeDescriptionProvider = null)
    {
        _reminderService = reminderService;
        _triggerEventsProvider = triggerEventsProvider ?? (() => []);
        _triggerRangeDescriptionProvider = triggerRangeDescriptionProvider ?? (() => string.Empty);
        RefreshTriggerEvents();
    }

    public void RefreshTriggerEvents()
    {
        var selectedEventIds = TriggerEvents
            .Where(item => item.IsSelected)
            .Select(item => item.EventId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in TriggerEvents)
            item.PropertyChanged -= TriggerEventItem_PropertyChanged;

        TriggerEvents.Clear();
        TriggerRangeDescription = _triggerRangeDescriptionProvider();

        foreach (var calendarEvent in _triggerEventsProvider()
                     .Where(calendarEvent => calendarEvent.Reference.Id != Guid.Empty)
                     .DistinctBy(calendarEvent => calendarEvent.Reference.Id)
                     .OrderBy(calendarEvent => calendarEvent.StartTime)
                     .ThenBy(calendarEvent => calendarEvent.Title))
        {
            var item = new DebugTriggerEventItemViewModel(calendarEvent)
            {
                IsSelected = selectedEventIds.Contains(calendarEvent.Reference.Id.ToString())
            };
            item.PropertyChanged += TriggerEventItem_PropertyChanged;
            TriggerEvents.Add(item);
        }

        OnPropertyChanged(nameof(HasTriggerEvents));
        OnPropertyChanged(nameof(HasNoTriggerEvents));
        OnPropertyChanged(nameof(SelectedTriggerEventCount));
    }

    [RelayCommand]
    private async Task TriggerRemindersAsync()
    {
        if (IsTriggeringReminders)
            return;

        ClearTriggerErrors();
        TriggerStatusText = string.Empty;

        var selectedEvents = TriggerEvents
            .Where(item => item.IsSelected)
            .ToList();
        if (selectedEvents.Count == 0)
        {
            TriggerStatusText = HasTriggerEvents
                ? "Select one or more events."
                : "No events are available in the current calendar range.";
            return;
        }

        var eventIds = selectedEvents
            .Select(item => item.EventId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        IsTriggeringReminders = true;

        try
        {
            var result = await _reminderService.TriggerRemindersNowAsync(eventIds);
            foreach (var missingEventId in result.MissingEventIds)
                TriggerErrors.Add($"Event not found: {missingEventId}");

            OnPropertyChanged(nameof(HasTriggerErrors));

            if (result.Reminders.Count == 0)
            {
                TriggerStatusText = result.MissingEventIds.Count == 0
                    ? "No reminders were triggered."
                    : "No reminders were triggered for the selected events.";
                return;
            }

            var ownerWindow = App.MainWindow
                ?? throw new InvalidOperationException("MainWindow not available");
            TriggerStatusText = $"Triggered {result.Reminders.Count} reminder(s) for {eventIds.Count - result.MissingEventIds.Count} event(s).";
            await ReminderNotificationWindow.ShowAsync(ownerWindow, _reminderService, result.Reminders.ToList());
        }
        catch (Exception ex)
        {
            TriggerStatusText = $"Error: {ex.Message}";
            TriggerErrors.Add(ex.Message);
            OnPropertyChanged(nameof(HasTriggerErrors));
        }
        finally
        {
            IsTriggeringReminders = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RebuildRemindersAsync(CancellationToken cancellationToken)
    {
        IsProcessing = true;
        IsComplete = false;
        Errors.Clear();
        Progress = 0;
        StatusText = "Starting reminder rebuild...";
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var result = await _reminderService.RebuildAllRemindersAsync(_cancellationTokenSource.Token);

            TotalCalendars = result.TotalCalendars;
            TotalEvents = result.TotalEvents;
            EventsProcessed = result.EventsProcessed;
            Progress = 100;

            foreach (var error in result.Errors)
                Errors.Add(error);

            StatusText = HasErrors
                ? $"Rebuild completed with {ErrorCount} error(s)"
                : "Rebuild completed successfully";

            IsComplete = true;
        }
        catch (TaskCanceledException)
        {
            StatusText = "Rebuild cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Errors.Add(ex.Message);
        }
        finally
        {
            IsProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void Close()
    {
        _cancellationTokenSource?.Cancel();
    }

    public async Task OnClosingAsync()
    {
        if (!IsProcessing)
            return;

        _cancellationTokenSource?.Cancel();
        await Task.Delay(100);
    }

    private void TriggerEventItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DebugTriggerEventItemViewModel.IsSelected))
            OnPropertyChanged(nameof(SelectedTriggerEventCount));
    }

    private void ClearTriggerErrors()
    {
        TriggerErrors.Clear();
        OnPropertyChanged(nameof(HasTriggerErrors));
    }
}

public partial class DebugTriggerEventItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public DebugTriggerEventItemViewModel(CalendarEvent calendarEvent)
    {
        CalendarEvent = calendarEvent;
    }

    public CalendarEvent CalendarEvent { get; }
    public string EventId => CalendarEvent.Reference.Id.ToString();
    public string Title => string.IsNullOrWhiteSpace(CalendarEvent.Title) ? "[no title]" : CalendarEvent.Title;
    public string CalendarName => CalendarEvent.Reference.Calendar.Name;
    public string ScheduleDescription => FormatSchedule(CalendarEvent);

    private static string FormatSchedule(CalendarEvent calendarEvent)
    {
        var start = calendarEvent.StartTime.ToDateTimeUnspecified();
        var end = calendarEvent.EndTime.ToDateTimeUnspecified();
        var isFullDay = start.TimeOfDay == TimeSpan.Zero && end.TimeOfDay == TimeSpan.Zero;

        if (isFullDay)
        {
            var lastDay = end.Date > start.Date ? end.Date.AddDays(-1) : start.Date;
            return start.Date == lastDay
                ? $"{start:ddd, MMM d, yyyy} · All day · {calendarEvent.Reference.Calendar.Name}"
                : $"{start:ddd, MMM d, yyyy} – {lastDay:ddd, MMM d, yyyy} · All day · {calendarEvent.Reference.Calendar.Name}";
        }

        return start.Date == end.Date
            ? $"{start:ddd, MMM d, yyyy HH:mm} – {end:HH:mm} · {calendarEvent.Reference.Calendar.Name}"
            : $"{start:ddd, MMM d, yyyy HH:mm} – {end:ddd, MMM d, yyyy HH:mm} · {calendarEvent.Reference.Calendar.Name}";
    }
}
