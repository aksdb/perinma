using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Services;

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

    public int ErrorCount => Errors.Count;
    public bool HasErrors => Errors.Count > 0;

    public DebugWindowViewModel(ReminderService reminderService)
    {
        _reminderService = reminderService;
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
}
