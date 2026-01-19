using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using perinma.Services;
using perinma.Storage.Models;

namespace perinma.Views.Reminders;

public partial class ReminderNotificationWindow : Window
{
    private readonly ReminderListViewModel _viewModel;
    private readonly TaskCompletionSource<bool> _tcs;

    public ReminderNotificationWindow(ReminderService reminderService, List<ReminderWithEvent> reminders)
    {
        InitializeComponent();
        
        _tcs = new TaskCompletionSource<bool>();
        _viewModel = new ReminderListViewModel(reminderService, reminders);
        
        DataContext = _viewModel;
        
        _viewModel.AllRemindersDismissed += OnAllRemindersDismissed;
        Closed += OnClosed;
    }

    public static Task ShowAsync(Window owner, ReminderService reminderService, List<ReminderWithEvent> reminders)
    {
        var window = new ReminderNotificationWindow(reminderService, reminders);
        window.Show(owner);
        return window._tcs.Task;
    }

    private void OnAllRemindersDismissed(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.AllRemindersDismissed -= OnAllRemindersDismissed;
        Closed -= OnClosed;
        _tcs.TrySetResult(true);
    }
}
