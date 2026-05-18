using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using perinma.Storage.Models;

namespace perinma.Services.CardDAV;

public interface ICardDavService
{
    Task<AddressBookSyncResult> GetAddressBooksAsync(
        CardDavCredentials credentials,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    Task<ContactSyncResult> GetContactsAsync(
        CardDavCredentials credentials,
        string addressBookUrl,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    Task<CardDavContact> CreateContactAsync(
        CardDavCredentials credentials,
        string addressBookUrl,
        CardDavContact contact,
        CancellationToken cancellationToken = default);

    Task<CardDavContact> UpdateContactAsync(
        CardDavCredentials credentials,
        CardDavContact contact,
        CancellationToken cancellationToken = default);

    Task<bool> TestConnectionAsync(
        CardDavCredentials credentials,
        CancellationToken cancellationToken = default);

    public class AddressBookSyncResult
    {
        public required IList<CardDavAddressBook> AddressBooks { get; init; }
        public string? SyncToken { get; init; }
    }

    public class ContactSyncResult
    {
        public required IList<CardDavContact> Contacts { get; init; }
        public string? SyncToken { get; init; }
    }
}
