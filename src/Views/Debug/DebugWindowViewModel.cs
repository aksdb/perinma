using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;
using perinma.Views.Reminders;

namespace perinma.Views.Debug;

public partial class DebugWindowViewModel : ObservableObject
{
    private readonly ReminderService _reminderService;
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
    private string _triggerEventIdsText = string.Empty;

    [ObservableProperty]
    private string _triggerStatusText = string.Empty;

    [ObservableProperty]
    private bool _isTriggeringReminders;

    public ObservableCollection<string> TriggerErrors { get; } = [];

    public int ErrorCount => Errors.Count;
    public bool HasErrors => Errors.Count > 0;
    public bool HasTriggerErrors => TriggerErrors.Count > 0;

    public DebugWindowViewModel(ReminderService reminderService)
    {
        _reminderService = reminderService;
    }

    [RelayCommand]
    private async Task TriggerRemindersAsync()
    {
        if (IsTriggeringReminders)
        {
            return;
        }

        ClearTriggerErrors();
        TriggerStatusText = string.Empty;

        var eventIds = ParseEventIds(TriggerEventIdsText);
        if (eventIds.Count == 0)
        {
            TriggerStatusText = "Enter one or more event ids.";
            return;
        }

        IsTriggeringReminders = true;

        try
        {
            var result = await _reminderService.TriggerRemindersNowAsync(eventIds);
            foreach (var missingEventId in result.MissingEventIds)
            {
                TriggerErrors.Add($"Event not found: {missingEventId}");
            }

            OnPropertyChanged(nameof(HasTriggerErrors));

            if (result.Reminders.Count == 0)
            {
                TriggerStatusText = result.MissingEventIds.Count == 0
                    ? "No reminders were triggered."
                    : "No reminders were triggered for the supplied event ids.";
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
            {
                Errors.Add(error);
            }

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
        if (IsProcessing)
        {
            _cancellationTokenSource?.Cancel();
            await Task.Delay(100); // Give the operation time to cancel
        }
    }

    private void ClearTriggerErrors()
    {
        TriggerErrors.Clear();
        OnPropertyChanged(nameof(HasTriggerErrors));
    }

    private static List<string> ParseEventIds(string input)
    {
        return input
            .Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
