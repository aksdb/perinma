using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using perinma.Storage;

namespace perinma.ViewModels;

public sealed class CalendarWeekViewModel : ViewModelBase
{
    private readonly ICalendarSource _calendarSource;

    public ObservableCollection<EventItemViewModel> Events { get; } = new();

    // Monday-based week start (local)
    public DateTime WeekStartLocal { get; private set; }

    public int DayColumns { get; set; } = 5; // Mon-Fri by default to match current view

    private CalendarWeekViewModel(ICalendarSource calendarSource)
    {
        _calendarSource = calendarSource;
        SetCurrentWeekStart();
    }

    public static CalendarWeekViewModel Instance { get; } = new(new DummyCalendarSource(DateTime.Now));

    private void SetCurrentWeekStart()
    {
        var today = DateTime.Today;
        int diff = ((int)today.DayOfWeek + 6) % 7; // Monday=0
        WeekStartLocal = today.AddDays(-diff);
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Events.Clear();

        var start = WeekStartLocal;
        var end = start.AddDays(DayColumns);

        var events = _calendarSource.GetCalendarEvents(start, end);
        foreach (var e in events)
        {
            // Map start time
            int dayIndex = (int)Math.Clamp((e.StartTime.Date - start.Date).TotalDays, 0, DayColumns - 1);

            // Compute 15-minute slots relative to day start
            var dayStart = start.AddDays(dayIndex);
            var minutesFromDayStart = (int)(e.StartTime - dayStart).TotalMinutes;
            if (minutesFromDayStart < 0) minutesFromDayStart = 0;
            int startSlot = minutesFromDayStart / 15;

            // Duration in 15-minute slots (ensure at least 1 slot)
            var durationMinutes = (int)Math.Max(15, (e.EndTime - e.StartTime).TotalMinutes);
            int durationSlots = Math.Max(1, durationMinutes / 15);

            Events.Add(new EventItemViewModel
            {
                Title = e.Title ?? string.Empty,
                DaySlot = dayIndex,
                StartSlot = startSlot,
                EndSlot = durationSlots,
                Color =  Color.Parse(e.Calendar.Color ?? string.Empty),
            });
        }
    }
}

public sealed class EventItemViewModel : ViewModelBase
{
    public string Title { get; set; } = string.Empty;
    public int StartSlot { get; set; }
    public int EndSlot { get; set; } // used as duration slots by current view
    public int DaySlot { get; set; }
    public Color Color { get; set; } = Color.FromArgb(0x99, 0x33, 0x99, 0xFF);
}
