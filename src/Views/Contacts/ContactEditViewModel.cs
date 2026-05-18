using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;

namespace perinma.Views.Contacts;

public sealed record ContactAddressBookOption(AddressBook AddressBook)
{
    public Guid AddressBookId => AddressBook.Id;
    public string DisplayName => $"{AddressBook.Account.Name} / {AddressBook.Name}";
}

public sealed record ContactEditResult(
    AddressBook AddressBook,
    string? DisplayName,
    string? GivenName,
    string? FamilyName,
    string? PrimaryEmail,
    string? PrimaryPhone);

public partial class ContactEditViewModel : ViewModelBase
{
    private readonly bool _canChangeAddressBook;

    [ObservableProperty]
    private ContactAddressBookOption? _selectedAddressBook;

    [ObservableProperty]
    private string? _displayName;

    [ObservableProperty]
    private string? _givenName;

    [ObservableProperty]
    private string? _familyName;

    [ObservableProperty]
    private string? _primaryEmail;

    [ObservableProperty]
    private string? _primaryPhone;

    [ObservableProperty]
    private string? _validationError;

    public ObservableCollection<ContactAddressBookOption> AddressBooks { get; } = [];
    public bool IsEditMode { get; }
    public bool CanChangeAddressBook => _canChangeAddressBook;
    public string Title => IsEditMode ? "Edit Contact" : "New Contact";

    public event Action<ContactEditResult?>? CloseRequested;

    public ContactEditViewModel(
        IEnumerable<ContactAddressBookOption> addressBooks,
        Contact? existingContact = null,
        bool canChangeAddressBook = true)
    {
        _canChangeAddressBook = canChangeAddressBook;
        IsEditMode = existingContact != null;

        foreach (var addressBook in addressBooks)
            AddressBooks.Add(addressBook);

        if (existingContact != null)
        {
            SelectedAddressBook = AddressBooks.FirstOrDefault(option => option.AddressBookId == existingContact.Reference.AddressBook.Id)
                ?? AddressBooks.FirstOrDefault();
            DisplayName = existingContact.DisplayName;
            GivenName = existingContact.GivenName;
            FamilyName = existingContact.FamilyName;
            PrimaryEmail = existingContact.PrimaryEmail;
            PrimaryPhone = existingContact.PrimaryPhone;
        }
        else
        {
            SelectedAddressBook = AddressBooks.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void Save()
    {
        ValidationError = null;

        if (SelectedAddressBook == null)
        {
            ValidationError = "Select an address book.";
            return;
        }

        var hasAnyValue = !string.IsNullOrWhiteSpace(DisplayName)
                          || !string.IsNullOrWhiteSpace(GivenName)
                          || !string.IsNullOrWhiteSpace(FamilyName)
                          || !string.IsNullOrWhiteSpace(PrimaryEmail)
                          || !string.IsNullOrWhiteSpace(PrimaryPhone);
        if (!hasAnyValue)
        {
            ValidationError = "Enter at least a name, email, or phone number.";
            return;
        }

        CloseRequested?.Invoke(new ContactEditResult(
            SelectedAddressBook.AddressBook,
            Normalize(DisplayName),
            Normalize(GivenName),
            Normalize(FamilyName),
            Normalize(PrimaryEmail),
            Normalize(PrimaryPhone)));
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(null);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
