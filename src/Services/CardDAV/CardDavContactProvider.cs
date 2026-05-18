using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using perinma.Models;

namespace perinma.Services.CardDAV;

/// <summary>
/// CardDAV implementation of IContactProvider.
/// </summary>
public class CardDavContactProvider : IContactProvider
{
    private const string ContactResourceKey = "resourceUrl";
    private const string ContactEtagKey = "etag";
    private readonly ICardDavService _cardDavService;
    private readonly CredentialManagerService _credentialManager;
    
    private static readonly ModelExtension<CardDavContact> CardDavContactExtension = new();

    public CardDavContactProvider(
        ICardDavService cardDavService,
        CredentialManagerService credentialManager)
    {
        _cardDavService = cardDavService;
        _credentialManager = credentialManager;
    }

    public CredentialManagerService CredentialManager => _credentialManager;

    public async Task<AddressBookSyncResult> GetAddressBooksAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var credentials = _credentialManager.GetCardDavCredentials(accountId);
        if (credentials == null)
            throw new InvalidOperationException($"No CardDAV credentials found for account {accountId}");

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

    public async Task<ContactSyncResult> GetContactsAsync(
        string accountId,
        string addressBookExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var credentials = _credentialManager.GetCardDavCredentials(accountId);
        if (credentials == null)
            throw new InvalidOperationException($"No CardDAV credentials found for account {accountId}");

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
            Data = BuildProviderData(c),
            GroupExternalIds = null
        }).ToList();

        return new ContactSyncResult
        {
            Contacts = contacts,
            SyncToken = result.SyncToken
        };
    }

    public Task<ContactGroupSyncResult> GetContactGroupsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ContactGroupSyncResult
        {
            Groups = new List<ProviderContactGroup>(),
            SyncToken = null
        });
    }

    public void EnrichContact(Contact contact, Func<string, string?> getData)
    {
        TryEnrichContact(contact, getData("rawData"), getData(ContactResourceKey), getData(ContactEtagKey));
    }

    public async Task<Contact> CreateContactAsync(
        AddressBook addressBook,
        Contact contact,
        CancellationToken cancellationToken = default)
    {
        var accountId = addressBook.Account.Id.ToString();
        var credentials = _credentialManager.GetCardDavCredentials(accountId);
        if (credentials == null)
            throw new InvalidOperationException($"No CardDAV credentials found for account {accountId}");
        if (string.IsNullOrWhiteSpace(addressBook.ExternalId))
            throw new InvalidOperationException("Address book URL is required to create a CardDAV contact");

        var created = await _cardDavService.CreateContactAsync(credentials, addressBook.ExternalId, BuildCardDavContact(contact),
            cancellationToken);
        return MapToContact(addressBook, created, contact.Reference.Id);
    }

    public async Task<Contact> UpdateContactAsync(
        Contact contact,
        CancellationToken cancellationToken = default)
    {
        var accountId = contact.Reference.AddressBook.Account.Id.ToString();
        var credentials = _credentialManager.GetCardDavCredentials(accountId);
        if (credentials == null)
            throw new InvalidOperationException($"No CardDAV credentials found for account {accountId}");

        var updated = await _cardDavService.UpdateContactAsync(credentials, BuildCardDavContact(contact), cancellationToken);
        return MapToContact(contact.Reference.AddressBook, updated, contact.Reference.Id);
    }

    public IList<object> GetSupportedExtensions() =>
    [
        ContactExtensions.ProviderResource,
        ContactExtensions.ProviderETag
    ];

    public async Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credentials = _credentialManager.GetCardDavCredentials(accountId);
            if (credentials == null)
                return false;

            return await _cardDavService.TestConnectionAsync(credentials, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CardDAV connection test failed: {ex.Message}");
            return false;
        }
    }

    public static bool TryEnrichContact(Contact contact, string? rawVCard, string? resourceUrl = null, string? etag = null)
    {
        if (string.IsNullOrWhiteSpace(rawVCard))
            return false;

        var normalizedResourceUrl = NormalizeResourceUrl(contact.Reference.AddressBook.ExternalId,
            resourceUrl ?? contact.Extensions.Get(ContactExtensions.ProviderResource));
        var parsedContact = new CardDavContact
        {
            Uid = contact.Reference.ExternalId ?? Guid.NewGuid().ToString("N"),
            Url = normalizedResourceUrl ?? string.Empty,
            DisplayName = contact.DisplayName,
            GivenName = contact.GivenName,
            FamilyName = contact.FamilyName,
            PrimaryEmail = contact.PrimaryEmail,
            PrimaryPhone = contact.PrimaryPhone,
            PhotoUrl = contact.PhotoUrl,
            ETag = etag,
            RawVCard = rawVCard,
            Deleted = false
        };

        contact.Extensions.Set(CardDavContactExtension, parsedContact);
        if (!string.IsNullOrWhiteSpace(parsedContact.Url))
            contact.Extensions.Set(ContactExtensions.ProviderResource, parsedContact.Url);
        if (!string.IsNullOrWhiteSpace(parsedContact.ETag))
            contact.Extensions.Set(ContactExtensions.ProviderETag, parsedContact.ETag);
        return true;
    }

    public static CardDavContact? GetCardDavContact(Contact contact) => contact.Extensions.Get(CardDavContactExtension);

    private static Contact MapToContact(AddressBook addressBook, CardDavContact cardDavContact, Guid contactId)
    {
        var hydratedContact = new Contact
        {
            Reference = new ContactReference
            {
                AddressBook = addressBook,
                Id = contactId,
                ExternalId = cardDavContact.Uid
            },
            DisplayName = cardDavContact.DisplayName,
            GivenName = cardDavContact.GivenName,
            FamilyName = cardDavContact.FamilyName,
            PrimaryEmail = cardDavContact.PrimaryEmail,
            PrimaryPhone = cardDavContact.PrimaryPhone,
            PhotoUrl = cardDavContact.PhotoUrl,
        };

        TryEnrichContact(hydratedContact, cardDavContact.RawVCard, cardDavContact.Url, cardDavContact.ETag);
        return hydratedContact;
    }

    private static CardDavContact BuildCardDavContact(Contact contact)
    {
        var resourceUrl = NormalizeResourceUrl(
            contact.Reference.AddressBook.ExternalId,
            contact.Extensions.Get(ContactExtensions.ProviderResource));

        if (GetCardDavContact(contact) is { } existingContact)
        {
            return new CardDavContact
            {
                Uid = existingContact.Uid,
                Url = resourceUrl ?? existingContact.Url,
                DisplayName = contact.DisplayName,
                GivenName = contact.GivenName,
                FamilyName = contact.FamilyName,
                PrimaryEmail = contact.PrimaryEmail,
                PrimaryPhone = contact.PrimaryPhone,
                PhotoUrl = contact.PhotoUrl,
                ETag = contact.Extensions.Get(ContactExtensions.ProviderETag) ?? existingContact.ETag,
                RawVCard = existingContact.RawVCard,
                Deleted = existingContact.Deleted
            };
        }

        return new CardDavContact
        {
            Uid = contact.Reference.ExternalId ?? Guid.NewGuid().ToString("N"),
            Url = resourceUrl ?? string.Empty,
            DisplayName = contact.DisplayName,
            GivenName = contact.GivenName,
            FamilyName = contact.FamilyName,
            PrimaryEmail = contact.PrimaryEmail,
            PrimaryPhone = contact.PrimaryPhone,
            PhotoUrl = contact.PhotoUrl,
            ETag = contact.Extensions.Get(ContactExtensions.ProviderETag),
            RawVCard = null,
            Deleted = false
        };
    }

    private static string? NormalizeResourceUrl(string? addressBookUrl, string? resourceUrl)
    {
        if (string.IsNullOrWhiteSpace(resourceUrl))
            return resourceUrl;
        if (Uri.IsWellFormedUriString(resourceUrl, UriKind.Absolute))
            return resourceUrl;
        if (string.IsNullOrWhiteSpace(addressBookUrl))
            return resourceUrl;

        var baseUrl = addressBookUrl.EndsWith('/') ? addressBookUrl : addressBookUrl + "/";
        return new Uri(new Uri(baseUrl), resourceUrl).ToString();
    }
    private static Dictionary<string, DataAttribute> BuildProviderData(CardDavContact contact)
    {
        var data = new Dictionary<string, DataAttribute>();
        if (!string.IsNullOrWhiteSpace(contact.Url))
            data[ContactResourceKey] = new DataAttribute.Text(contact.Url);
        if (!string.IsNullOrWhiteSpace(contact.ETag))
            data[ContactEtagKey] = new DataAttribute.Text(contact.ETag);
        return data;
    }
}
