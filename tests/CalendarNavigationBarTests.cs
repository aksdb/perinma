using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using perinma.Views.Calendar;

namespace tests;

[TestFixture]
public class CalendarNavigationBarTests
{
    [AvaloniaTest]
    public void CalendarNavigationBar_UsesAtomUiControls()
    {
        var control = new CalendarNavigationBar
        {
            DataContext = new CalendarNavigationBarViewModel()
        };

        AssertAtomControl(control, "ViewModeGroup", "Segmented");
        AssertAtomControl(control, "MonthViewButton", "SegmentedItem");
        AssertAtomControl(control, "WeekViewButton", "SegmentedItem");
        AssertAtomControl(control, "WorkWeekViewButton", "SegmentedItem");
        AssertAtomControl(control, "DayViewButton", "SegmentedItem");
        AssertAtomControl(control, "AgendaViewButton", "SegmentedItem");
        AssertAtomControl(control, "PreviousButton", "Button");
        AssertAtomControl(control, "TodayButton", "Button");
        AssertAtomControl(control, "NextButton", "Button");
        AssertAtomControl(control, "CreateEventButton", "Button");
    }

    private static void AssertAtomControl(CalendarNavigationBar control, string name, string typeName)
    {
        var found = control.FindControl<Control>(name);
        Assert.That(found, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(found!.GetType().Name, Is.EqualTo(typeName));
        Assert.That(found.GetType().Namespace, Does.StartWith("AtomUI."));
    }
}
