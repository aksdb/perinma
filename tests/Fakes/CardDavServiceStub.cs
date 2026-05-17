using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using perinma.Services.CardDAV;
using perinma.Storage.Models;

namespace tests.Fakes;

public class CardDavServiceStub : ICardDavService
{
    private readonly List<CardDavAddressBook> _addressBooks = new();
    private readonly Dictionary<string, List<CardDavContact>> _contactsByAddressBook = new();

    public void SetAddressBooks(params CardDavAddressBook[] addressBooks)
    {
        _addressBooks.Clear();
        _addressBooks.AddRange(addressBooks);
    }

    public void SetContacts(string addressBookUrl, params CardDavContact[] contacts)
    {
        if (!_contactsByAddressBook.ContainsKey(addressBookUrl))
            _contactsByAddressBook[addressBookUrl] = new List<CardDavContact>();

        _contactsByAddressBook[addressBookUrl].Clear();
        _contactsByAddressBook[addressBookUrl].AddRange(contacts);
    }

    public Task<ICardDavService.AddressBookSyncResult> GetAddressBooksAsync(
        CardDavCredentials credentials,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ICardDavService.AddressBookSyncResult
        {
            AddressBooks = _addressBooks,
            SyncToken = null
        });
    }

    public Task<ICardDavService.ContactSyncResult> GetContactsAsync(
        CardDavCredentials credentials,
        string addressBookUrl,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var contacts = _contactsByAddressBook.TryGetValue(addressBookUrl, out var values)
            ? values
            : [];

        return Task.FromResult(new ICardDavService.ContactSyncResult
        {
            Contacts = contacts,
            SyncToken = null
        });
    }

    public Task<bool> TestConnectionAsync(
        CardDavCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
