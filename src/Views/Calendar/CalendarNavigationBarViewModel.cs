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

    private bool _suppressSelectedViewIndexChanged;

    [ObservableProperty]
    private string _dateRangeDisplay = string.Empty;

    [ObservableProperty]
    private IRelayCommand? _previousCommand;

    [ObservableProperty]
    private IRelayCommand? _nextCommand;

    [ObservableProperty]
    private IRelayCommand? _todayCommand;

    [ObservableProperty]
    private IRelayCommand? _createNewEventCommand;

    [ObservableProperty]
    private IRelayCommand? _showMonthViewCommand;

    [ObservableProperty]
    private IRelayCommand? _showWeekViewCommand;

    [ObservableProperty]
    private IRelayCommand? _showFiveDaysViewCommand;

    [ObservableProperty]
    private IRelayCommand? _showDayViewCommand;

    [ObservableProperty]
    private IRelayCommand? _showAgendaViewCommand;

    [ObservableProperty]
    private int _selectedViewIndex = (int)CalendarNavigationViewMode.Week;

    public void SetSelectedViewMode(CalendarNavigationViewMode mode)
    {
        _suppressSelectedViewIndexChanged = true;
        try
        {
            SelectedViewIndex = (int)mode;
        }
        finally
        {
            _suppressSelectedViewIndexChanged = false;
        }
    }

    partial void OnSelectedViewIndexChanged(int value)
    {
        if (_suppressSelectedViewIndexChanged)
        {
            return;
        }

        switch ((CalendarNavigationViewMode)value)
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
