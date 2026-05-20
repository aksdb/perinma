using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace perinma.Views.Calendar;

public partial class CalendarNavigationBarViewModel : ViewModelBase
{
    public enum CalendarNavigationViewMode
    {
        Month,
        Week,
        WorkWeek,
        Day,
        Agenda
    }

    [ObservableProperty]
    public partial string DateRangeDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IRelayCommand? PreviousCommand { get; set; }

    [ObservableProperty]
    public partial IRelayCommand? NextCommand { get; set; }

    [ObservableProperty]
    public partial IRelayCommand? TodayCommand { get; set; }

    [ObservableProperty]
    public partial IRelayCommand? CreateNewEventCommand { get; set; }

    [ObservableProperty]
    public partial IRelayCommand? ShowMonthViewCommand { get; set; }

    [ObservableProperty]
    public partial IRelayCommand? ShowWeekViewCommand { get; set; }

    [ObservableProperty]
    public partial IRelayCommand? ShowFiveDaysViewCommand { get; set; }

    [ObservableProperty]
    public partial IRelayCommand? ShowDayViewCommand { get; set; }

    [ObservableProperty]
    public partial IRelayCommand? ShowAgendaViewCommand { get; set; }

    [ObservableProperty]
    public partial CalendarNavigationViewMode SelectedView { get; set; } = CalendarNavigationViewMode.Week;

    public void SetSelectedViewMode(CalendarNavigationViewMode mode)
    {
        SelectedView = mode;
    }

    partial void OnSelectedViewChanged(CalendarNavigationViewMode value)
    {
        switch (value)
        {
            case CalendarNavigationViewMode.Month:
                ShowMonthViewCommand?.Execute(null);
                break;
            case CalendarNavigationViewMode.Week:
                ShowWeekViewCommand?.Execute(null);
                break;
            case CalendarNavigationViewMode.WorkWeek:
                ShowFiveDaysViewCommand?.Execute(null);
                break;
            case CalendarNavigationViewMode.Day:
                ShowDayViewCommand?.Execute(null);
                break;
            case CalendarNavigationViewMode.Agenda:
                ShowAgendaViewCommand?.Execute(null);
                break;
        }
    }
}