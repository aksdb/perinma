using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;
using perinma.Utils;

namespace perinma.Views.Calendar;

public partial class AgendaDayViewModel : ObservableObject
{
    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    private bool _isToday;

    public ObservableCollection<AgendaEventViewModel> Events { get; } = [];

    public string DayOfWeek => Date.ToString("dddd");

    public string DateDisplay => Date.ToString("MMMM d, yyyy");

    public bool HasEvents => Events.Count > 0;
}

public partial class AgendaEventViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private DateTime _startTime;

    [ObservableProperty]
    private DateTime _endTime;

    [ObservableProperty]
    private bool _isFullDay;

    [ObservableProperty]
    private Color _color;

    [ObservableProperty]
    private string _calendarName = string.Empty;

    [ObservableProperty]
    private CalendarEvent? _calendarEvent;

    public string TimeDisplay => IsFullDay ? "All day" : StartTime.ToString("HH:mm");

    public string DurationDisplay
    {
        get
        {
            if (IsFullDay) return string.Empty;

            var duration = EndTime - StartTime;
            if (duration.TotalMinutes < 60)
                return $"{(int)duration.TotalMinutes} min";
            if (duration.TotalHours < 24)
                return duration.Minutes > 0
                    ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
                    : $"{(int)duration.TotalHours}h";
            return $"{(int)duration.TotalDays} days";
        }
    }

    public bool ShowDuration => !IsFullDay;

    public IBrush Background => new SolidColorBrush(Color);

    public IBrush Foreground
    {
        get
        {
            var textColor = ColorUtils.ContrastTextColor(Color);
            return new SolidColorBrush(textColor);
        }
    }

    public IBrush SecondaryForeground
    {
        get
        {
            var textColor = ColorUtils.ContrastTextColor(Color);
            // Make it slightly more transparent for secondary text
            return new SolidColorBrush(Color.FromArgb(180, textColor.R, textColor.G, textColor.B));
        }
    }
}
