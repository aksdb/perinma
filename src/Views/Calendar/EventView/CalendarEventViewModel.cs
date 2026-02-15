using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;

namespace perinma.Views.Calendar.EventView;

public partial class CalendarEventViewModel(CalendarEvent calendarEvent) : ViewModelBase
{
    [ObservableProperty]
    private CalendarEvent _calendarEvent = calendarEvent;

    public ObservableCollection<ViewModelBase> EventDetails { get; } = [];
}
