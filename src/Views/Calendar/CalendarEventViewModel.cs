using System;
using CommunityToolkit.Mvvm.ComponentModel;
using NodaTime;
using perinma.Models;

namespace perinma.Views.Calendar;

public partial class CalendarEventViewModel : ViewModelBase
{
    [ObservableProperty]
    private CalendarEvent _calendarEvent;

    public CalendarEventViewModel(CalendarEvent calendarEvent)
    {
        _calendarEvent = calendarEvent;
    }
}
