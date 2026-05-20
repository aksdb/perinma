using System.Reflection;
using perinma.Views.Calendar;
using perinma.Views.Main;

namespace tests;

[TestFixture]
public class MainWindowViewModeTests
{
    private static readonly MethodInfo ResolveNavigationViewModeMethod = typeof(MainWindowViewModel)
        .GetMethod("ResolveNavigationViewMode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveNavigationViewMode method not found.");

    [TestCase(MainWindowViewModel.CalendarView.Week, 7, 5,
        CalendarNavigationBarViewModel.CalendarNavigationViewMode.Week)]
    [TestCase(MainWindowViewModel.CalendarView.Week, 5, 5,
        CalendarNavigationBarViewModel.CalendarNavigationViewMode.WorkWeek)]
    [TestCase(MainWindowViewModel.CalendarView.Week, 1, 5,
        CalendarNavigationBarViewModel.CalendarNavigationViewMode.Day)]
    [TestCase(MainWindowViewModel.CalendarView.Month, 7, 5,
        CalendarNavigationBarViewModel.CalendarNavigationViewMode.Month)]
    [TestCase(MainWindowViewModel.CalendarView.Agenda, 7, 5,
        CalendarNavigationBarViewModel.CalendarNavigationViewMode.Agenda)]
    public void ResolveNavigationViewMode_MapsCalendarStateCorrectly(
        MainWindowViewModel.CalendarView calendarView,
        int dayColumns,
        int workWeekDayCount,
        CalendarNavigationBarViewModel.CalendarNavigationViewMode expected)
    {
        var actual = (CalendarNavigationBarViewModel.CalendarNavigationViewMode)ResolveNavigationViewModeMethod.Invoke(
            null,
            new object[] { calendarView, dayColumns, workWeekDayCount })!;

        Assert.That(actual, Is.EqualTo(expected));
    }
}
