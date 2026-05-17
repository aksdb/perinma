using System;
using perinma.Models;
using perinma.Views.Contacts;

namespace tests;

[TestFixture]
public class ContactEditViewModelTests
{
    private static ContactAddressBookOption CreateAddressBookOption()
    {
        return new ContactAddressBookOption(new AddressBook
        {
            Account = new Account
            {
                Id = Guid.NewGuid(),
                Name = "Personal",
                Type = AccountType.CardDav,
                SortOrder = 0
            },
            Id = Guid.NewGuid(),
            ExternalId = "https://carddav.example.com/addressbooks/default",
            Name = "Default",
            Enabled = true
        });
    }

    [Test]
    public void Save_NoValues_SetsValidationError()
    {
        var viewModel = new ContactEditViewModel([CreateAddressBookOption()]);

        viewModel.SaveCommand.Execute(null);

        Assert.That(viewModel.ValidationError, Is.EqualTo("Enter at least a name, email, or phone number."));
    }

    [Test]
    public void Save_WithValues_EmitsNormalizedResult()
    {
        var option = CreateAddressBookOption();
        var viewModel = new ContactEditViewModel([option]);
        ContactEditResult? result = null;
        viewModel.CloseRequested += value => result = value;

        viewModel.DisplayName = "  Alice Example  ";
        viewModel.PrimaryEmail = " alice@example.com ";
        viewModel.SaveCommand.Execute(null);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.AddressBook.Id, Is.EqualTo(option.AddressBookId));
            Assert.That(result.DisplayName, Is.EqualTo("Alice Example"));
            Assert.That(result.PrimaryEmail, Is.EqualTo("alice@example.com"));
        });
    }
}
