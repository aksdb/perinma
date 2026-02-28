using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NodaTime;
using NodaTime.Extensions;
using NodaTime.TimeZones;
using perinma.Messaging;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Views.MessageBox;

namespace perinma.Views.Calendar;

public partial class CalendarAgendaViewModel : CalendarViewModelBase, IRecipient<EventsChangedMessage>
{
    public ObservableCollection<AgendaDayViewModel> AgendaDays { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateRangeDisplay))]
    private DateTime _viewStart = DateTime.Today;

    public string DateRangeDisplay => FormatDateRange(ViewStart);

    private static string FormatDateRange(DateTime start)
    {
        var end = start.AddDays(30);
        return $"{start:MMM d} - {end:MMM d, yyyy}";
    }

    public CalendarAgendaViewModel(
        ICalendarSource calendarSource,
        SettingsService? settingsService = null)
        : base(calendarSource, settingsService)
    {
        WeakReferenceMessenger.Default.Register<EventsChangedMessage>(this);
    }

    [RelayCommand]
    private void Next()
    {
        ViewStart = ViewStart.AddDays(30);
        Load();
    }

    [RelayCommand]
    private void Previous()
    {
        ViewStart = ViewStart.AddDays(-30);
        Load();
    }

    [RelayCommand]
    private void Today()
    {
        ViewStart = DateTime.Today;
        Load();
    }

    [RelayCommand]
    private void CreateNewEvent()
    {
        var onCompleted = new Action<string>(async (errorMessage) =>
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                await MessageBoxWindow.ShowAsync(
                    null,
                    "Error",
                    errorMessage,
                    MessageBoxType.Error,
                    MessageBoxButtons.Ok);
            }
            else
            {
                Load();
            }
        });

        var editor = new EventEditView
        {
            DataContext = new EventEditViewModel(
                null,
                null,
                onCompleted)
        };
        editor.Show();
    }

    public void Load()
    {
        AgendaDays.Clear();

        var startDate = ViewStart.ToLocalDateTime();
        var endDate = startDate.PlusDays(30);
        var zone = DateTimeZoneProviders.Tzdb.GetSystemDefault();
        var interval = new Interval(startDate.InZoneLeniently(zone).ToInstant(), endDate.InZoneLeniently(zone).ToInstant());

        var events = _calendarSource.GetCalendarEvents(interval)
            .Where(e => e.StartTime >= startDate && e.StartTime < endDate)
            .OrderBy(e => e.StartTime)
            .ThenBy(e => e.Title)
            .ToList();

        // Group events by date
        var groupedEvents = events
            .GroupBy(e => e.StartTime.Date)
            .OrderBy(g => g.Key);

        foreach (var group in groupedEvents)
        {
            var dayVm = new AgendaDayViewModel
            {
                Date = group.Key,
                IsToday = group.Key == SystemClock.Instance.InTzdbSystemDefaultZone().GetCurrentDate()
            };

            foreach (var evt in group.OrderBy(e => e.StartTime))
            {
                var isFullDay = evt.StartTime.TimeOfDay == LocalTime.Midnight && evt.EndTime.TimeOfDay == LocalTime.Midnight;

                dayVm.Events.Add(new AgendaEventViewModel
                {
                    Title = string.IsNullOrEmpty(evt.Title) ? "[no title]" : evt.Title,
                    StartTime = evt.StartTime.ToDateTimeUnspecified(),
                    EndTime = evt.EndTime.ToDateTimeUnspecified(),
                    IsFullDay = isFullDay,
                    Color = string.IsNullOrEmpty(evt.Reference.Calendar.Color)
                        ? Color.FromArgb(0x99, 0x33, 0x99, 0xFF)
                        : Color.Parse(evt.Reference.Calendar.Color),
                    CalendarName = evt.Reference.Calendar.Name,
                    CalendarEvent = evt
                });
            }

            AgendaDays.Add(dayVm);
        }
    }

    public void Receive(EventsChangedMessage message)
    {
        Load();
    }
}
