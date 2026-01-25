using System.Threading.Tasks;
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

    private async void Snooze1MinClick(object? sender, RoutedEventArgs e) => await SnoozeAsync(SnoozeInterval.OneMinute);
    private async void Snooze5MinClick(object? sender, RoutedEventArgs e) => await SnoozeAsync(SnoozeInterval.FiveMinutes);
    private async void Snooze10MinClick(object? sender, RoutedEventArgs e) => await SnoozeAsync(SnoozeInterval.TenMinutes);
    private async void Snooze15MinClick(object? sender, RoutedEventArgs e) => await SnoozeAsync(SnoozeInterval.FifteenMinutes);
    private async void Snooze30MinClick(object? sender, RoutedEventArgs e) => await SnoozeAsync(SnoozeInterval.ThirtyMinutes);
    private async void Snooze1HourClick(object? sender, RoutedEventArgs e) => await SnoozeAsync(SnoozeInterval.OneHour);
    private async void Snooze2HourClick(object? sender, RoutedEventArgs e) => await SnoozeAsync(SnoozeInterval.TwoHours);
    private async void SnoozeTomorrowClick(object? sender, RoutedEventArgs e) => await SnoozeAsync(SnoozeInterval.Tomorrow);
    private async void Snooze1MinBeforeClick(object? sender, RoutedEventArgs e) => await SnoozeAsync(SnoozeInterval.OneMinuteBeforeStart);
    private async void SnoozeStartClick(object? sender, RoutedEventArgs e) => await SnoozeAsync(SnoozeInterval.WhenItStarts);

    private async Task SnoozeAsync(SnoozeInterval interval)
    {
        if (DataContext is ReminderViewModel reminder)
        {
            await reminder.SnoozeByIntervalAsync(interval);
        }
    }
}
