using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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

    // Windows API declaration for flashing taskbar
    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hWnd, bool bInvert);

    public ReminderNotificationWindow(ReminderService reminderService, List<ReminderWithEvent> reminders)
    {
        InitializeComponent();

        _tcs = new TaskCompletionSource<bool>();
        _viewModel = new ReminderListViewModel(reminderService, reminders);

        DataContext = _viewModel;

        _viewModel.AllRemindersDismissed += OnAllRemindersDismissed;
        Closed += OnClosed;

        // Initialize async without awaiting (fire and forget)
#pragma warning disable CS4014
        InitializeAsync();
#pragma warning restore CS4014
    }

    private async Task InitializeAsync()
    {
        await _viewModel.InitializeAsync();

        // After initialization, try to bring window to attention
        _ = BringWindowToAttentionAsync();
    }

    private async Task BringWindowToAttentionAsync()
    {
        // Wait a bit for the window to be fully shown
        await Task.Delay(100);

        // Try to activate the window and bring it to foreground
        // This is a gentle approach that doesn't force the window to stay on top
        Activate();

        // Flash the taskbar to get user attention
        // Platform-specific behavior: Windows API will flash the taskbar icon
        FlashTaskbar();
    }

    private void FlashTaskbar()
    {
        // Flash the taskbar to get user attention (Windows only)
        // On Linux/Mac, the window activation is usually sufficient
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var handle = TryGetPlatformHandle()?.Handle;
                if (handle != null)
                {
                    // Flash multiple times to be more noticeable
                    for (int i = 0; i < 5; i++)
                    {
                        FlashWindow(handle.Value, true);
                        Task.Delay(100).Wait();
                    }
                }
            }
            catch
            {
                // If flashing fails, silently ignore - window will still activate
            }
        }
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
