using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;
using perinma.Utils;

namespace perinma.Views.Calendar;

public partial class MonthDayViewModel : ObservableObject
{
    [ObservableProperty]
    private int _dayNumber;

    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    private bool _isCurrentMonth;

    [ObservableProperty]
    private bool _isToday;

    public ObservableCollection<MonthEventViewModel> Events { get; } = [];

    public IBrush Background => IsToday
        ? new SolidColorBrush(Color.FromArgb(30, 0x33, 0x99, 0xFF))
        : IsCurrentMonth
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0));

    public IBrush Foreground => IsCurrentMonth ? Brushes.Black : Brushes.Gray;

    public Avalonia.Media.FontWeight FontWeight => IsToday
        ? Avalonia.Media.FontWeight.Bold
        : Avalonia.Media.FontWeight.Normal;
}

public partial class MonthEventViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private Color _color;

    [ObservableProperty]
    private CalendarEvent? _calendarEvent;

    [ObservableProperty]
    private bool _isFullDay;

    [ObservableProperty]
    private string _timeText = string.Empty;

    public IBrush Background => new SolidColorBrush(Color);

    public IBrush Foreground
    {
        get
        {
            var textColor = ColorUtils.ContrastTextColor(Color);
            return new SolidColorBrush(textColor);
        }
    }
}
