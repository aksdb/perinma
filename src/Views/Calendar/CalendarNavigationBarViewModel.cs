using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace perinma.Views.Calendar;

public partial class CalendarNavigationBarViewModel : ViewModelBase
{
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
    private bool _isMonthView;

    [ObservableProperty]
    private bool _isWeekView;

    [ObservableProperty]
    private bool _isFiveDaysView;

    [ObservableProperty]
    private bool _isDayView;

    [ObservableProperty]
    private bool _isAgendaView;
}
