using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using perinma.Views.Calendar;

namespace tests;

[TestFixture]
public class EventEditWindowTests
{
    [AvaloniaTest]
    public void EventEditView_CalendarDropdownOpensInsideAtomWindow()
    {
        var window = new EventEditView();
        window.Show();

        try
        {
            var comboBox = window.FindControl<AtomUI.Desktop.Controls.ComboBox>("CalendarComboBox");
            Assert.That(comboBox, Is.Not.Null);

            Assert.DoesNotThrow(() => comboBox!.IsDropDownOpen = true);
            Assert.That(comboBox.IsDropDownOpen, Is.True);
        }
        finally
        {
            window.Close();
        }
    }
}
