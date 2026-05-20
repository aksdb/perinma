using CommunityToolkit.Mvvm.Input;
using perinma.Views.Calendar;

namespace tests;

[TestFixture]
public class CalendarNavigationBarViewModelTests
{
    [Test]
    public void SelectedViewIndex_ExecutesMatchingCommand()
    {
        var viewModel = new CalendarNavigationBarViewModel();
        var executed = new List<string>();

        viewModel.ShowMonthViewCommand = new RelayCommand(() => executed.Add("Month"));
        viewModel.ShowWeekViewCommand = new RelayCommand(() => executed.Add("Week"));
        viewModel.ShowFiveDaysViewCommand = new RelayCommand(() => executed.Add("WorkWeek"));
        viewModel.ShowDayViewCommand = new RelayCommand(() => executed.Add("Day"));
        viewModel.ShowAgendaViewCommand = new RelayCommand(() => executed.Add("Agenda"));

        viewModel.SelectedViewIndex = (int)CalendarNavigationBarViewModel.CalendarNavigationViewMode.Day;

        Assert.That(executed, Is.EqualTo(new[] { "Day" }));
    }

    [Test]
    public void SetSelectedViewMode_UpdatesSelection()
    {
        var viewModel = new CalendarNavigationBarViewModel();

        viewModel.SetSelectedViewMode(CalendarNavigationBarViewModel.CalendarNavigationViewMode.Month);

        Assert.That(viewModel.SelectedViewIndex,
            Is.EqualTo((int)CalendarNavigationBarViewModel.CalendarNavigationViewMode.Month));
    }
}
