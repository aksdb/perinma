using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public CalendarWeekViewModel CalendarWeekViewModel => CalendarWeekViewModel.Instance;
}
