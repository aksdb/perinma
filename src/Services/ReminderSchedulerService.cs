using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using perinma.Storage.Models;
using perinma.Views.Reminders;

namespace perinma.Services;

public class ReminderSchedulerService
{
    private readonly ReminderService _reminderService;
    private readonly Window _ownerWindow;
    private readonly CancellationTokenSource _cts = new();
    private Task? _schedulerTask;
    private bool _isWindowOpen;

    public ReminderSchedulerService(ReminderService reminderService, Window ownerWindow)
    {
        _reminderService = reminderService;
        _ownerWindow = ownerWindow;
    }

    public void Start()
    {
        if (_schedulerTask != null)
        {
            return;
        }

        _schedulerTask = Task.Run(async () => await RunSchedulerAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts.Cancel();
        _schedulerTask?.Wait(TimeSpan.FromSeconds(5));
        _schedulerTask = null;
        _reminderService.ClearFiredReminders();
    }

    private async Task RunSchedulerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckForDueRemindersAsync(cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking reminders: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckForDueRemindersAsync(CancellationToken cancellationToken)
    {
        if (_isWindowOpen)
        {
            return;
        }

        var dueReminders = await _reminderService.GetDueRemindersAsync(cancellationToken);

        if (dueReminders.Count > 0)
        {
            await ShowReminderWindowAsync(dueReminders, cancellationToken);
        }
    }

    private async Task ShowReminderWindowAsync(System.Collections.Generic.List<ReminderWithEvent> reminders, CancellationToken cancellationToken)
    {
        _isWindowOpen = true;

        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await ReminderNotificationWindow.ShowAsync(_ownerWindow, _reminderService, reminders);
            });
        }
        finally
        {
            _isWindowOpen = false;
        }
    }
}
