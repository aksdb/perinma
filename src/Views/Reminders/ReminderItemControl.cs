using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using perinma.Services;
using perinma.Storage.Models;

namespace perinma.Views.Reminders;

public partial class ReminderItemControl : UserControl
{
    public ReminderItemControl()
    {
        InitializeComponent();
    }

    private async void SnoozeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is SnoozeIntervalItem item && DataContext is ReminderViewModel reminder)
        {
            await reminder.SnoozeByIntervalAsync(item.Interval);
        }
    }
}
