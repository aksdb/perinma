using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Storage;

namespace perinma.Views.Contacts;

public partial class ContactsViewModel : ViewModelBase
{
    private readonly SqliteStorage _storage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalContactCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedContact))]
    private ContactItemViewModel? _selectedContact;

    public bool HasSelectedContact => SelectedContact != null;

    public ObservableCollection<AddressBookAccountGroupViewModel> AccountGroups { get; } = [];
    public ObservableCollection<ContactItemViewModel> FilteredContacts { get; } = [];

    public ContactsViewModel(SqliteStorage storage)
    {
        _storage = storage;
        _ = LoadAddressBooksAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = LoadContactsAsync();
    }

    [RelayCommand]
    public async Task LoadAddressBooksAsync()
    {
        AccountGroups.Clear();

        try
        {
            var allAddressBooks = await _storage.GetAllAddressBooksAsync();
            var addressBooksList = allAddressBooks.ToList();

            // Group by account
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

            // Load contacts after address books
            await LoadContactsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading address books: {ex.Message}");
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

            // Filter by search text if provided
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
            {
                FilteredContacts.Add(new ContactItemViewModel(contact));
            }

            TotalContactCount = FilteredContacts.Count;

            // Load photos in background with limited concurrency
            _ = LoadPhotosAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading contacts: {ex.Message}");
        }
    }

    private async Task LoadPhotosAsync()
    {
        // Load photos with limited concurrency to avoid overwhelming the network
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

            // Refresh contacts to reflect the change
            await LoadContactsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating address book enabled state: {ex.Message}");
            addressBook.Enabled = !enabled;
        }
    }
}
