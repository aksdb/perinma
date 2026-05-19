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

        AssertAtomControl(dialog, "AddressBookComboBox");
        AssertAtomControl(dialog, "GivenNameTextBox");
        AssertAtomControl(dialog, "FamilyNameTextBox");
        AssertAtomControl(dialog, "DisplayNameTextBox");
        AssertAtomControl(dialog, "PrimaryEmailTextBox");
        AssertAtomControl(dialog, "PrimaryPhoneTextBox");
        AssertAtomControl(dialog, "CancelButton");
        AssertAtomControl(dialog, "SaveButton");
    }

    private static void AssertAtomControl(ContactEditDialog dialog, string name)
    {
        var control = dialog.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().FullName, Does.StartWith("AtomUI."), $"Control '{name}' should use an AtomUI control type.");
    }
}
