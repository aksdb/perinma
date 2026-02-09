using System;
using CommunityToolkit.Mvvm.ComponentModel;
using NodaTime;
using perinma.Models;

namespace perinma.Views.Calendar;

public partial class CalendarEventViewModel : ViewModelBase
{
    [ObservableProperty]
    private CalendarEvent _calendarEvent;

    private static readonly DateTimeZone SystemZone = DateTimeZoneProviders.Tzdb.GetSystemDefault();

    public CalendarEventViewModel(CalendarEvent calendarEvent)
    {
        _calendarEvent = calendarEvent;
    }

    public DateTime StartTimeDisplay => CalendarEvent.StartTime
        .InZone(SystemZone)
        .ToDateTimeUnspecified();

    public DateTime EndTimeDisplay => CalendarEvent.EndTime
        .InZone(SystemZone)
        .ToDateTimeUnspecified();
}
