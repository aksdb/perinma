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

    private static void AssertAtomControl(ContactEditDialog dialog, string name, string typeName)
    {
        var control = dialog.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().Name, Is.EqualTo(typeName), $"Control '{name}' should use AtomUI {typeName}.");
    }
}
