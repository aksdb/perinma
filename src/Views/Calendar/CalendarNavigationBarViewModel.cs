using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace perinma.Views.Calendar;

public partial class CalendarNavigationBarViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _dateRangeDisplay = string.Empty;

    public IRelayCommand? PreviousCommand { get; set; }
    public IRelayCommand? NextCommand { get; set; }
    public IRelayCommand? TodayCommand { get; set; }
    public IRelayCommand? CreateNewEventCommand { get; set; }

    public IRelayCommand? ShowMonthViewCommand { get; set; }
    public IRelayCommand? ShowWeekViewCommand { get; set; }
    public IRelayCommand? ShowFiveDaysViewCommand { get; set; }
    public IRelayCommand? ShowDayViewCommand { get; set; }
    public IRelayCommand? ShowAgendaViewCommand { get; set; }

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
