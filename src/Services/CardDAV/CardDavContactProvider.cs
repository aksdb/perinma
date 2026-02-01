using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace perinma.Services.CardDAV;

/// <summary>
/// CardDAV implementation of IContactProvider.
/// </summary>
public class CardDavContactProvider : IContactProvider
{
    private readonly ICardDavService _cardDavService;
    private readonly CredentialManagerService _credentialManager;

    public CardDavContactProvider(
        ICardDavService cardDavService,
        CredentialManagerService credentialManager)
    {
        _cardDavService = cardDavService;
        _credentialManager = credentialManager;
    }

    /// <inheritdoc/>
    public CredentialManagerService CredentialManager => _credentialManager;

    /// <inheritdoc/>
    public async Task<AddressBookSyncResult> GetAddressBooksAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var credentials = _credentialManager.GetCardDavCredentials(accountId);
        if (credentials == null)
        {
            throw new InvalidOperationException($"No CardDAV credentials found for account {accountId}");
        }

        var result = await _cardDavService.GetAddressBooksAsync(credentials, syncToken, cancellationToken);

        var addressBooks = result.AddressBooks.Select(ab => new ProviderAddressBook
        {
            ExternalId = ab.Url,
            Name = ab.DisplayName,
            Deleted = ab.Deleted
        }).ToList();

        return new AddressBookSyncResult
        {
            AddressBooks = addressBooks,
            SyncToken = result.SyncToken
        };
    }

    /// <inheritdoc/>
    public async Task<ContactSyncResult> GetContactsAsync(
        string accountId,
        string addressBookExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var credentials = _credentialManager.GetCardDavCredentials(accountId);
        if (credentials == null)
        {
            throw new InvalidOperationException($"No CardDAV credentials found for account {accountId}");
        }

        var result = await _cardDavService.GetContactsAsync(credentials, addressBookExternalId, syncToken, cancellationToken);

        var contacts = result.Contacts.Select(c => new ProviderContact
        {
            ExternalId = c.Uid,
            DisplayName = c.DisplayName,
            GivenName = c.GivenName,
            FamilyName = c.FamilyName,
            PrimaryEmail = c.PrimaryEmail,
            PrimaryPhone = c.PrimaryPhone,
            PhotoUrl = c.PhotoUrl,
            Deleted = c.Deleted,
            RawData = c.RawVCard,
            GroupExternalIds = null // CardDAV groups are handled via CATEGORIES in vCard
        }).ToList();

        return new ContactSyncResult
        {
            Contacts = contacts,
            SyncToken = result.SyncToken
        };
    }

    /// <inheritdoc/>
    public Task<ContactGroupSyncResult> GetContactGroupsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        // CardDAV doesn't have a separate concept of contact groups like Google
        // Groups are typically handled via CATEGORIES property in vCards
        // For now, return empty list
        return Task.FromResult(new ContactGroupSyncResult
        {
            Groups = new List<ProviderContactGroup>(),
            SyncToken = null
        });
    }

    /// <inheritdoc/>
    public async Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credentials = _credentialManager.GetCardDavCredentials(accountId);
            if (credentials == null)
            {
                return false;
            }

            return await _cardDavService.TestConnectionAsync(credentials, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CardDAV connection test failed: {ex.Message}");
            return false;
        }
    }
}
