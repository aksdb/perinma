using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Apis.Json;
using perinma.Models;
using perinma.Services;
using perinma.Services.CardDAV;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Views.MessageBox;

namespace perinma.Views.Contacts;

public partial class ContactsViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;
    private readonly ContactSyncService _contactSyncService;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalContactCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedContact))]
    private ContactItemViewModel? _selectedContact;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
    private ContactGroupViewModel? _selectedGroup;

    private HashSet<string> _selectedGroupContactIds = [];

    public bool HasSelectedContact => SelectedContact != null;
    public bool HasSelectedGroup => SelectedGroup != null;

    public ObservableCollection<AddressBookAccountGroupViewModel> AccountGroups { get; } = [];
    public ObservableCollection<ContactGroupViewModel> ContactGroups { get; } = [];
    public ObservableCollection<ContactItemViewModel> FilteredContacts { get; } = [];

    public ContactsViewModel(SqliteStorage storage, ContactSyncService contactSyncService)
    {
        _storage = storage;
        _contactSyncService = contactSyncService;
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        await LoadAddressBooksAsync();
        await LoadContactGroupsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = LoadContactsAsync();
    }

    partial void OnSelectedGroupChanged(ContactGroupViewModel? value)
    {
        _ = OnGroupSelectionChangedAsync(value);
    }

    private async Task OnGroupSelectionChangedAsync(ContactGroupViewModel? group)
    {
        if (group == null)
        {
            _selectedGroupContactIds = [];
        }
        else
        {
            var contactIds = await _storage.GetContactIdsByGroupAsync(group.GroupId.ToString());
            _selectedGroupContactIds = contactIds.ToHashSet();
        }

        await LoadContactsAsync();
    }

    [RelayCommand]
    public async Task LoadAddressBooksAsync()
    {
        AccountGroups.Clear();

        try
        {
            var allAddressBooks = await _storage.GetAllAddressBooksAsync();
            var addressBooksList = allAddressBooks.ToList();

            var groupedByAccount = addressBooksList
                .GroupBy(ab => new { ab.AccountId, ab.AccountName, ab.AccountTypeEnum, ab.AccountSortOrder })
                .OrderBy(g => g.Key.AccountSortOrder)
                .ThenBy(g => g.Key.AccountName);

            foreach (var accountGroup in groupedByAccount)
            {
                var group = new AddressBookAccountGroupViewModel
                {
                    AccountId = Guid.Parse(accountGroup.Key.AccountId),
                    AccountName = accountGroup.Key.AccountName,
                    AccountType = accountGroup.Key.AccountTypeEnum
                };

                foreach (var addressBook in accountGroup.OrderBy(ab => ab.Name))
                {
                    var addressBookVm = new AddressBookViewModel(addressBook);
                    addressBookVm.EnabledChanged += OnAddressBookEnabledChanged;
                    group.AddressBooks.Add(addressBookVm);
                }

                AccountGroups.Add(group);
            }

            await LoadContactsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading address books: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task LoadContactGroupsAsync()
    {
        ContactGroups.Clear();

        try
        {
            var allGroups = await _storage.GetAllContactGroupsAsync();

            foreach (var group in allGroups)
            {
                if (group.IsSystemGroup && group.MemberCount == 0)
                    continue;

                ContactGroups.Add(new ContactGroupViewModel(group));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading contact groups: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task LoadContactsAsync()
    {
        FilteredContacts.Clear();
        TotalContactCount = 0;

        try
        {
            var allContacts = await _storage.GetAllContactsAsync();
            var contactsList = allContacts.ToList();

            if (SelectedGroup != null && _selectedGroupContactIds.Count > 0)
            {
                contactsList = contactsList
                    .Where(c => _selectedGroupContactIds.Contains(c.ContactId))
                    .ToList();
            }
            else if (SelectedGroup != null && _selectedGroupContactIds.Count == 0)
            {
                TotalContactCount = 0;
                return;
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var searchLower = SearchText.ToLowerInvariant();
                contactsList = contactsList
                    .Where(c =>
                        (c.DisplayName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (c.PrimaryEmail?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (c.PrimaryPhone?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (c.GivenName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (c.FamilyName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
            }

            foreach (var contact in contactsList.OrderBy(c => c.DisplayName))
                FilteredContacts.Add(new ContactItemViewModel(contact));

            TotalContactCount = FilteredContacts.Count;
            _ = LoadPhotosAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading contacts: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CreateContactAsync()
    {
        var owner = App.MainWindow;
        if (owner == null)
            return;

        try
        {
            var addressBookOptions = await GetEditableAddressBookOptionsAsync();
            if (addressBookOptions.Count == 0)
            {
                await MessageBoxWindow.ShowAsync(
                    owner,
                    "No Address Book",
                    "Enable an address book before creating contacts.",
                    MessageBoxType.Warning,
                    MessageBoxButtons.Ok);
                return;
            }

            var result = await ContactEditDialog.ShowAsync(owner, new ContactEditViewModel(addressBookOptions));
            if (result == null)
                return;

            var provider = _contactSyncService.GetProviderForAccountType(result.AddressBook.Account.Type);
            if (provider == null)
                throw new InvalidOperationException($"No contact provider found for {result.AddressBook.Account.Type}");

            var contact = ApplyEditResult(new Contact
            {
                Reference = new ContactReference
                {
                    AddressBook = result.AddressBook,
                    Id = Guid.NewGuid(),
                    ExternalId = null
                }
            }, result);

            var savedContact = await provider.CreateContactAsync(result.AddressBook, contact);
            var savedContactId = await PersistContactAsync(savedContact);
            await RefreshAfterSaveAsync(savedContactId);
        }
        catch (Exception ex)
        {
            await MessageBoxWindow.ShowAsync(
                owner,
                "Create Contact Failed",
                ex.Message,
                MessageBoxType.Error,
                MessageBoxButtons.Ok);
        }
    }

    [RelayCommand]
    private async Task EditSelectedContactAsync()
    {
        if (SelectedContact == null)
            return;

        var owner = App.MainWindow;
        if (owner == null)
            return;

        try
        {
            var contact = await LoadEditableContactAsync(SelectedContact.ContactId);
            if (contact == null)
                throw new InvalidOperationException("Contact not found.");

            if (contact.Extensions.Get(ContactExtensions.IsReadOnly))
            {
                await MessageBoxWindow.ShowAsync(
                    owner,
                    "Read-only Contact",
                    "This contact cannot be edited by the provider.",
                    MessageBoxType.Warning,
                    MessageBoxButtons.Ok);
                return;
            }

            var result = await ContactEditDialog.ShowAsync(
                owner,
                new ContactEditViewModel(
                    [new ContactAddressBookOption(contact.Reference.AddressBook)],
                    contact,
                    canChangeAddressBook: false));
            if (result == null)
                return;

            var provider = _contactSyncService.GetProviderForAccountType(contact.Reference.AddressBook.Account.Type);
            if (provider == null)
                throw new InvalidOperationException($"No contact provider found for {contact.Reference.AddressBook.Account.Type}");

            ApplyEditResult(contact, result);
            var savedContact = await provider.UpdateContactAsync(contact);
            var savedContactId = await PersistContactAsync(savedContact);
            await RefreshAfterSaveAsync(savedContactId);
        }
        catch (Exception ex)
        {
            await MessageBoxWindow.ShowAsync(
                owner,
                "Edit Contact Failed",
                ex.Message,
                MessageBoxType.Error,
                MessageBoxButtons.Ok);
        }
    }

    private async Task<Contact?> LoadEditableContactAsync(Guid contactId)
    {
        var contact = await _storage.GetHydratedContactByIdAsync(contactId.ToString());
        if (contact == null)
            return null;

        var provider = _contactSyncService.GetProviderForAccountType(contact.Reference.AddressBook.Account.Type)
            ?? throw new InvalidOperationException($"No contact provider found for {contact.Reference.AddressBook.Account.Type}");
        provider.EnrichContact(contact, key => _storage.GetContactDataAsync(contact.Reference.Id.ToString(), key).GetAwaiter().GetResult());
        return contact;
    }

    private async Task<List<ContactAddressBookOption>> GetEditableAddressBookOptionsAsync()
    {
        var allAddressBooks = await _storage.GetAllAddressBooksAsync();
        return allAddressBooks
            .Where(ab => ab.IsEnabled)
            .OrderBy(ab => ab.AccountSortOrder)
            .ThenBy(ab => ab.AccountName)
            .ThenBy(ab => ab.Name)
            .Select(ab => new ContactAddressBookOption(new AddressBook
            {
                Account = new Account
                {
                    Id = Guid.Parse(ab.AccountId),
                    Name = ab.AccountName,
                    Type = ab.AccountTypeEnum,
                    SortOrder = ab.AccountSortOrder
                },
                Id = Guid.Parse(ab.AddressBookId),
                ExternalId = ab.ExternalId,
                Name = ab.Name,
                Enabled = ab.IsEnabled,
                LastSync = ab.LastSync == null
                    ? null
                    : DateTimeOffset.FromUnixTimeSeconds(ab.LastSync.Value).UtcDateTime
            }))
            .ToList();
    }

    private async Task<string> PersistContactAsync(Contact contact)
    {
        var contactId = await _storage.CreateOrUpdateContactAsync(new Storage.Models.ContactDbo
        {
            AddressBookId = contact.Reference.AddressBook.Id.ToString(),
            ContactId = contact.Reference.Id.ToString(),
            ExternalId = contact.Reference.ExternalId,
            DisplayName = contact.DisplayName,
            GivenName = contact.GivenName,
            FamilyName = contact.FamilyName,
            PrimaryEmail = contact.PrimaryEmail,
            PrimaryPhone = contact.PrimaryPhone,
            PhotoUrl = contact.PhotoUrl,
            ChangedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        switch (contact.Reference.AddressBook.Account.Type)
        {
            case AccountType.Google:
                var googlePerson = GoogleContactProvider.GetGooglePerson(contact)
                    ?? throw new InvalidOperationException("Google contact metadata is missing after save.");
                await _storage.SetContactDataJsonAsync(contactId, "rawData", NewtonsoftJsonSerializer.Instance.Serialize(googlePerson));
                break;
            case AccountType.CardDav:
                var cardDavContact = CardDavContactProvider.GetCardDavContact(contact)
                    ?? throw new InvalidOperationException("CardDAV contact metadata is missing after save.");
                if (!string.IsNullOrWhiteSpace(cardDavContact.RawVCard))
                    await _storage.SetContactDataAsync(contactId, "rawData", cardDavContact.RawVCard);
                break;
        }

        await PersistProviderFieldAsync(contactId, contact.Reference.AddressBook.Account.Type, contact);
        return contactId;
    }

    private async Task RefreshAfterSaveAsync(string contactId)
    {
        await LoadAddressBooksAsync();

        var selected = FilteredContacts.FirstOrDefault(contact =>
            contact.ContactId == Guid.Parse(contactId));
        if (selected != null)
            SelectedContact = selected;
    }

    private async Task PersistProviderFieldAsync(string contactId, AccountType accountType, Contact contact)
    {
        var resource = contact.Extensions.Get(ContactExtensions.ProviderResource);
        var etag = contact.Extensions.Get(ContactExtensions.ProviderETag);

        switch (accountType)
        {
            case AccountType.Google:
                if (!string.IsNullOrWhiteSpace(resource))
                    await _storage.SetContactDataAsync(contactId, "providerResource", resource);
                if (!string.IsNullOrWhiteSpace(etag))
                    await _storage.SetContactDataAsync(contactId, "providerETag", etag);
                break;
            case AccountType.CardDav:
                if (!string.IsNullOrWhiteSpace(resource))
                    await _storage.SetContactDataAsync(contactId, "resourceUrl", resource);
                if (!string.IsNullOrWhiteSpace(etag))
                    await _storage.SetContactDataAsync(contactId, "etag", etag);
                break;
        }
    }

    private static Contact ApplyEditResult(Contact contact, ContactEditResult result)
    {
        contact.DisplayName = result.DisplayName;
        contact.GivenName = result.GivenName;
        contact.FamilyName = result.FamilyName;
        contact.PrimaryEmail = result.PrimaryEmail;
        contact.PrimaryPhone = result.PrimaryPhone;
        return contact;
    }

    private async Task LoadPhotosAsync()
    {
        await Parallel.ForEachAsync(FilteredContacts,
            new ParallelOptions { MaxDegreeOfParallelism = 5 },
            async (contact, cancellationToken) =>
            {
                await contact.LoadPhotoAsync(cancellationToken);
            });
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void ClearGroupFilter()
    {
        SelectedGroup = null;
    }

    [RelayCommand]
    private void SelectContact(ContactItemViewModel? contact)
    {
        SelectedContact = contact;
    }

    private async void OnAddressBookEnabledChanged(object? sender, bool enabled)
    {
        if (sender is not AddressBookViewModel addressBook)
            return;

        try
        {
            var success = await _storage.UpdateAddressBookEnabledAsync(
                addressBook.AddressBookId.ToString(),
                enabled
            );

            if (!success)
            {
                Console.WriteLine($"Failed to update address book enabled state: {addressBook.AddressBookId}");
                addressBook.Enabled = !enabled;
                return;
            }

            await LoadContactsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating address book enabled state: {ex.Message}");
            addressBook.Enabled = !enabled;
        }
    }
}
