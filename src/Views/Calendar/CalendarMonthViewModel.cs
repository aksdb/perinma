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

public partial class CalendarMonthViewModel : CalendarViewModelBase, IRecipient<EventsChangedMessage>
{
    public ObservableCollection<MonthDayViewModel> MonthDays { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateRangeDisplay))]
    private DateTime _viewStart = DateTime.Now;

    public string DateRangeDisplay => ViewStart.ToString("MMMM yyyy");

    public CalendarMonthViewModel(
        ICalendarSource calendarSource,
        SettingsService? settingsService = null)
        : base(calendarSource, settingsService)
    {
        WeakReferenceMessenger.Default.Register<EventsChangedMessage>(this);
    }

    [RelayCommand]
    private void Next()
    {
        ViewStart = ViewStart.AddMonths(1);
        Load();
    }

    [RelayCommand]
    private void Previous()
    {
        ViewStart = ViewStart.AddMonths(-1);
        Load();
    }

    [RelayCommand]
    private void Today()
    {
        ViewStart = DateTime.Today;
        Load();
    }

    public override void Load()
    {
        MonthDays.Clear();

        var firstOfMonth = new LocalDate(ViewStart.Year, ViewStart.Month, 1);
        var lastOfMonth = firstOfMonth.PlusMonths(1).PlusDays(-1);

        // Find the Monday before or on the first of month
        var startOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        var gridStart = firstOfMonth.PlusDays(-startOffset);

        // Always show 6 weeks (42 days) for consistent grid
        var gridEnd = gridStart.PlusDays(42);

        // Get all events for the visible range
        var zone = DateTimeZoneProviders.Tzdb.GetSystemDefault();
        var startInstant = gridStart.AtMidnight().InZoneLeniently(zone).ToInstant();
        var endInstant = gridEnd.AtMidnight().InZoneLeniently(zone).ToInstant();
        var events = _calendarSource.GetCalendarEvents(new Interval(startInstant, endInstant)).ToList();

        // Build day view models
        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.PlusDays(i);
            var dayVm = new MonthDayViewModel
            {
                Date = date,
                DayNumber = date.Day,
                IsCurrentMonth = date.Month == ViewStart.Month,
                IsToday = date == SystemClock.Instance.InTzdbSystemDefaultZone().GetCurrentDate()
            };

            // Add events for this day
            var dayEvents = events
                .Where(e => e.StartTime.Date <= date && e.EndTime.Date >= date)
                .OrderBy(e => e.StartTime);

            foreach (var evt in dayEvents)
            {
                var isFullDay = evt.StartTime.TimeOfDay == LocalTime.Midnight && evt.EndTime.TimeOfDay == LocalTime.Midnight;
                dayVm.Events.Add(new MonthEventViewModel
                {
                    Title = string.IsNullOrEmpty(evt.Title) ? "[no title]" : evt.Title,
                    Color = string.IsNullOrEmpty(evt.Reference.Calendar.Color)
                        ? Color.FromArgb(0x99, 0x33, 0x99, 0xFF)
                        : Color.Parse(evt.Reference.Calendar.Color),
                    CalendarEvent = evt,
                    IsFullDay = isFullDay,
                    TimeText = isFullDay ? string.Empty : evt.StartTime.ToString("HH:mm", null)
                });
            }

            MonthDays.Add(dayVm);
        }
    }

    public void Receive(EventsChangedMessage message)
    {
        Load();
    }
}
