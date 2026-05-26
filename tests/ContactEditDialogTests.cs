using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using perinma.Views.Contacts;

namespace tests;

[TestFixture]
public class ContactEditDialogTests
{
    [AvaloniaTest]
    public void ContactEditDialog_UsesAtomInputControls()
    {
        var viewModel = new ContactEditViewModel([ContactEditViewModelTests.CreateAddressBookOptionForTests()]);
        var dialog = new ContactEditDialog
        {
            DataContext = viewModel
        };

        AssertAtomControl(dialog, "AddressBookComboBox", "ComboBox");
        AssertAtomControl(dialog, "GivenNameInput", "LineEdit");
        AssertAtomControl(dialog, "FamilyNameInput", "LineEdit");
        AssertAtomControl(dialog, "DisplayNameInput", "LineEdit");
        AssertAtomControl(dialog, "PrimaryEmailInput", "LineEdit");
        AssertAtomControl(dialog, "PrimaryPhoneInput", "LineEdit");
        AssertAtomControl(dialog, "CancelButton", "Button");
        AssertAtomControl(dialog, "SaveButton", "Button");
    }

    [AvaloniaTest]
    public void ContactEditDialog_AddressBookDropdownOpensInsideAtomWindow()
    {
        var viewModel = new ContactEditViewModel([ContactEditViewModelTests.CreateAddressBookOptionForTests()]);
        var dialog = new ContactEditDialog
        {
            DataContext = viewModel
        };

        dialog.Show();

        try
        {
            var comboBox = dialog.FindControl<AtomUI.Desktop.Controls.ComboBox>("AddressBookComboBox");
            Assert.That(comboBox, Is.Not.Null);

            Assert.DoesNotThrow(() => comboBox!.IsDropDownOpen = true);
            Assert.That(comboBox.IsDropDownOpen, Is.True);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static void AssertAtomControl(ContactEditDialog dialog, string name, string typeName)
    {
        var control = dialog.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().Name, Is.EqualTo(typeName), $"Control '{name}' should use AtomUI {typeName}.");
    }
}
