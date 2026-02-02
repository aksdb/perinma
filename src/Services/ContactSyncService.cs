using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using perinma.Storage;
using perinma.Storage.Models;
using perinma.Models;
using perinma.Messaging;

namespace perinma.Services;

public class ContactSyncService
{
    private readonly SqliteStorage _storage;
    private readonly IReadOnlyDictionary<AccountType, IContactProvider> _providers;

    public ContactSyncService(
        SqliteStorage storage,
        IReadOnlyDictionary<AccountType, IContactProvider> providers)
    {
        _storage = storage;
        _providers = providers;
    }

    /// <summary>
    /// Gets the contact providers dictionary.
    /// </summary>
    public IReadOnlyDictionary<AccountType, IContactProvider> Providers => _providers;

    /// <summary>
    /// Syncs contacts from all accounts that support contacts.
    /// </summary>
    public async Task<ContactSyncServiceResult> SyncAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        var result = new ContactSyncServiceResult();
        WeakReferenceMessenger.Default.Send(new ContactSyncStartedMessage());

        try
        {
            var accounts = (await _storage.GetAllAccountsAsync()).ToImmutableList();

            // Filter to accounts that have contact providers
            var contactAccounts = accounts
                .Where(a => _providers.ContainsKey(a.AccountTypeEnum))
                .ToList();

            Console.WriteLine($"Found {contactAccounts.Count} accounts to sync contacts for");

            for (int i = 0; i < contactAccounts.Count; i++)
            {
                var account = contactAccounts[i];
                try
                {
                    WeakReferenceMessenger.Default.Send(new SyncAccountProgressMessage
                    {
                        AccountName = account.Name,
                        AccountIndex = i,
                        TotalAccounts = contactAccounts.Count
                    });
                    await SyncAccountAsync(account, cancellationToken);
                    result.SyncedAccounts++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error syncing contacts for account {account.Name}: {ex}");
                    result.FailedAccounts++;
                    result.Errors.Add($"{account.Name}: {ex.Message}");
                }
            }

            result.Success = result.FailedAccounts == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during contact sync: {ex.Message}");
            result.Success = false;
            result.Errors.Add(ex.Message);
        }
        finally
        {
            WeakReferenceMessenger.Default.Send(new ContactSyncEndedMessage());
        }

        return result;
    }

    /// <summary>
    /// Syncs a single account using the appropriate provider.
    /// </summary>
    private async Task SyncAccountAsync(
        AccountDbo account,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing contacts for account: {account.Name} (Type: {account.Type})");

        if (!_providers.TryGetValue(account.AccountTypeEnum, out var provider))
        {
            throw new InvalidOperationException($"No contact provider registered for account type: {account.Type}");
        }

        try
        {
            // Sync contact groups first (for Google)
            await SyncContactGroupsAsync(provider, account, cancellationToken);

            // Sync address books
            await SyncAddressBooksAsync(provider, account, cancellationToken);

            // Sync contacts for each enabled address book
            var addressBooks = await _storage.GetAddressBooksByAccountAsync(account.AccountId);
            var enabledAddressBooks = addressBooks.Where(ab => ab.Enabled == 1).ToList();

            for (int i = 0; i < enabledAddressBooks.Count; i++)
            {
                var addressBook = enabledAddressBooks[i];
                try
                {
                    WeakReferenceMessenger.Default.Send(new SyncAddressBookProgressMessage
                    {
                        AddressBookName = addressBook.Name,
                        AddressBookIndex = i,
                        TotalAddressBooks = enabledAddressBooks.Count
                    });
                    await SyncAddressBookContactsAsync(provider, addressBook, account.AccountId, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error syncing contacts for address book {addressBook.Name}: {ex.Message}");
                }
            }
        }
        catch (ReAuthenticationRequiredException ex)
        {
            Console.WriteLine($"Account {account.Name} requires re-authentication: {ex.Message}");
            WeakReferenceMessenger.Default.Send(new ReAuthenticationRequiredMessage(ex.AccountId, ex.ProviderType));
        }
    }

    /// <summary>
    /// Syncs address books for an account using the provider.
    /// </summary>
    private async Task SyncAddressBooksAsync(
        IContactProvider provider,
        AccountDbo account,
        CancellationToken cancellationToken)
    {
        string? syncToken = await _storage.GetAccountData(account, "addressBookSyncToken");
        bool isFullSync = string.IsNullOrEmpty(syncToken);

        AddressBookSyncResult result;
        try
        {
            result = await provider.GetAddressBooksAsync(account.AccountId, syncToken, cancellationToken);
            Console.WriteLine(
                $"Found {result.AddressBooks.Count} address book {(isFullSync ? "items" : "changes")} for account {account.Name}");
        }
        catch (Exception ex) when (ex.Message.Contains("410") || ex.Message.Contains("invalid") ||
                                   ex.Message.Contains("Sync token"))
        {
            Console.WriteLine($"Sync token invalid, performing full sync: {ex.Message}");
            isFullSync = true;
            result = await provider.GetAddressBooksAsync(account.AccountId, null, cancellationToken);
            Console.WriteLine($"Found {result.AddressBooks.Count} address books in full sync for account {account.Name}");
        }

        var currentSyncTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var addressBook in result.AddressBooks)
        {
            if (addressBook.Deleted)
            {
                Console.WriteLine($"Address book {addressBook.Name} was deleted, will clean up");
                continue;
            }

            var existingAddressBook =
                await _storage.GetAddressBookByExternalIdAsync(account.AccountId, addressBook.ExternalId);
            int enabled = existingAddressBook?.Enabled ?? 1; // Default to enabled for new address books

            var addressBookDbo = new AddressBookDbo
            {
                AccountId = account.AccountId,
                AddressBookId = string.Empty,
                ExternalId = addressBook.ExternalId,
                Name = addressBook.Name,
                Enabled = enabled,
                LastSync = currentSyncTime
            };

            await _storage.CreateOrUpdateAddressBookAsync(addressBookDbo);

            foreach (var dataPair in addressBook.Data)
            {
                switch (dataPair.Value)
                {
                    case DataAttribute.Text text:
                        await _storage.SetAddressBookDataAsync(addressBookDbo.AddressBookId, dataPair.Key, text.value);
                        break;
                    case DataAttribute.JsonText jsonText:
                        // For now, store JSON as text - could add SetAddressBookDataJsonAsync if needed
                        await _storage.SetAddressBookDataAsync(addressBookDbo.AddressBookId, dataPair.Key, jsonText.value);
                        break;
                }
            }
        }

        if (isFullSync)
        {
            var deletedCount = await _storage.DeleteAddressBooksNotSyncedAsync(account.AccountId, currentSyncTime);
            if (deletedCount > 0)
            {
                Console.WriteLine($"Deleted {deletedCount} address book(s) that were removed remotely");
            }
        }

        if (!string.IsNullOrEmpty(result.SyncToken))
        {
            await _storage.SetAccountData(account, "addressBookSyncToken", result.SyncToken);
            Console.WriteLine($"Stored new address book sync token for next sync");
        }

        Console.WriteLine($"Synced {result.AddressBooks.Count} address books for account {account.Name}");
    }

    /// <summary>
    /// Syncs contacts for an address book using the provider.
    /// </summary>
    private async Task SyncAddressBookContactsAsync(
        IContactProvider provider,
        AddressBookDbo addressBook,
        string accountId,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing contacts for address book: {addressBook.Name}");

        string? syncToken = await _storage.GetAddressBookDataAsync(addressBook.AddressBookId, "contactSyncToken");
        bool isFullSync = string.IsNullOrEmpty(syncToken);

        ContactSyncResult result;
        try
        {
            result = await provider.GetContactsAsync(accountId, addressBook.ExternalId ?? string.Empty, syncToken,
                cancellationToken);
            Console.WriteLine(
                $"Found {result.Contacts.Count} contact {(isFullSync ? "items" : "changes")} for address book {addressBook.Name}");
        }
        catch (Exception ex) when (ex.Message.Contains("410") || ex.Message.Contains("invalid") ||
                                   ex.Message.Contains("Sync token"))
        {
            Console.WriteLine($"Contact sync token invalid, performing full sync: {ex.Message}");
            isFullSync = true;
            result = await provider.GetContactsAsync(accountId, addressBook.ExternalId ?? string.Empty, null,
                cancellationToken);
            Console.WriteLine($"Found {result.Contacts.Count} contacts in full sync for address book {addressBook.Name}");
        }

        var currentSyncTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var contact in result.Contacts)
        {
            if (contact.Deleted)
            {
                Console.WriteLine($"Contact {contact.DisplayName} was deleted, will clean up");
                continue;
            }

            // Download photo and convert to blob:// URL
            var photoBlobUrl = await DownloadPhotoAsBlobAsync(contact.PhotoUrl, cancellationToken);

            var contactDbo = new ContactDbo
            {
                AddressBookId = addressBook.AddressBookId,
                ContactId = string.Empty,
                ExternalId = contact.ExternalId,
                DisplayName = contact.DisplayName,
                GivenName = contact.GivenName,
                FamilyName = contact.FamilyName,
                PrimaryEmail = contact.PrimaryEmail,
                PrimaryPhone = contact.PrimaryPhone,
                PhotoUrl = photoBlobUrl,
                ChangedAt = currentSyncTime
            };

            var contactId = await _storage.CreateOrUpdateContactAsync(contactDbo);

            if (!string.IsNullOrEmpty(contact.RawData))
            {
                await _storage.SetContactDataAsync(contactId, "rawData", contact.RawData);
            }

            // Handle group memberships
            if (contact.GroupExternalIds != null && contact.GroupExternalIds.Count > 0)
            {
                var groupIds = new List<string>();
                foreach (var groupExternalId in contact.GroupExternalIds)
                {
                    var groupId = await _storage.GetContactGroupIdByExternalIdAsync(accountId, groupExternalId);
                    if (groupId != null)
                    {
                        groupIds.Add(groupId);
                    }
                }
                await _storage.SetContactGroupMembershipAsync(contactId, groupIds);
            }
        }

        if (isFullSync)
        {
            var deletedCount = await _storage.DeleteContactsNotSyncedAsync(addressBook.AddressBookId, currentSyncTime);
            if (deletedCount > 0)
            {
                Console.WriteLine($"Deleted {deletedCount} contact(s) that were removed remotely");
            }
        }

        if (!string.IsNullOrEmpty(result.SyncToken))
        {
            await _storage.SetAddressBookDataAsync(addressBook.AddressBookId, "contactSyncToken", result.SyncToken);
            Console.WriteLine($"Stored new contact sync token for next sync");
        }

        Console.WriteLine($"Synced {result.Contacts.Count} contacts for address book {addressBook.Name}");
        WeakReferenceMessenger.Default.Send(new SyncContactsProgressMessage
        {
            AddressBookName = addressBook.Name,
            ContactCount = result.Contacts.Count
        });
    }

    /// <summary>
    /// Syncs contact groups for an account using the provider.
    /// </summary>
    private async Task SyncContactGroupsAsync(
        IContactProvider provider,
        AccountDbo account,
        CancellationToken cancellationToken)
    {
        string? syncToken = await _storage.GetAccountData(account, "contactGroupSyncToken");

        ContactGroupSyncResult result;
        try
        {
            result = await provider.GetContactGroupsAsync(account.AccountId, syncToken, cancellationToken);
            Console.WriteLine($"Found {result.Groups.Count} contact groups for account {account.Name}");
        }
        catch (Exception ex) when (ex.Message.Contains("410") || ex.Message.Contains("invalid"))
        {
            Console.WriteLine($"Contact group sync token invalid, performing full sync: {ex.Message}");
            result = await provider.GetContactGroupsAsync(account.AccountId, null, cancellationToken);
        }

        foreach (var group in result.Groups)
        {
            if (group.Deleted)
            {
                continue;
            }

            var groupDbo = new ContactGroupDbo
            {
                AccountId = account.AccountId,
                GroupId = string.Empty,
                ExternalId = group.ExternalId,
                Name = group.Name,
                SystemGroup = group.SystemGroup ? 1 : 0
            };

            await _storage.CreateOrUpdateContactGroupAsync(groupDbo);
        }

        if (!string.IsNullOrEmpty(result.SyncToken))
        {
            await _storage.SetAccountData(account, "contactGroupSyncToken", result.SyncToken);
        }

        Console.WriteLine($"Synced {result.Groups.Count} contact groups for account {account.Name}");
    }

    /// <summary>
    /// Gets the contact provider for a specific account type.
    /// </summary>
    public IContactProvider? GetProviderForAccountType(AccountType accountType)
    {
        return _providers.GetValueOrDefault(accountType);
    }

    /// <summary>
    /// Downloads a photo URL and converts it to a blob:// URL with base64 data.
    /// </summary>
    private static async Task<string?> DownloadPhotoAsBlobAsync(string? photoUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(photoUrl) || !photoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return photoUrl; // Already a blob:// URL or invalid
        }

        try
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(photoUrl, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to download photo: {response.StatusCode}");
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var base64 = Convert.ToBase64String(bytes);
            return $"blob://{base64}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading photo: {ex.Message}");
            return null;
        }
    }
}

public class ContactSyncServiceResult
{
    public bool Success { get; set; }
    public int SyncedAccounts { get; set; }
    public int FailedAccounts { get; set; }
    public List<string> Errors { get; set; } = [];
}
