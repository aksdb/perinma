using System;
using System.Linq;
using System.Threading.Tasks;
using CredentialStore;
using NUnit.Framework;
using perinma.Models;
using perinma.Services;
using perinma.Services.CardDAV;
using perinma.Storage;
using perinma.Storage.Models;
using tests.Fakes;

namespace tests;

public class ContactSyncServiceTests
{
    private DatabaseService _database = null!;
    private CredentialManagerService _credentialManager = null!;
    private SqliteStorage _storage = null!;
    private CardDavServiceStub _cardDavService = null!;
    private ContactSyncService _contactSyncService = null!;
    private CardDavContactProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _database = new DatabaseService(inMemory: true);
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        _storage = new SqliteStorage(_database, _credentialManager);
        _cardDavService = new CardDavServiceStub();
        _provider = new CardDavContactProvider(_cardDavService, _credentialManager);
        _contactSyncService = new ContactSyncService(_storage, new System.Collections.Generic.Dictionary<AccountType, IContactProvider>
        {
            [AccountType.CardDav] = _provider
        });
    }

    [TearDown]
    public void TearDown()
    {
        _storage.Dispose();
        _database.Dispose();
    }

    [Test]
    public async Task ForceResyncAccountAsync_CardDavAccount_SyncsAddressBooksAndContacts()
    {
        const string accountId = "carddav-account";
        const string addressBookUrl = "https://carddav.example.com/addressbooks/default";

        await _storage.CreateAccountAsync(new AccountDbo
        {
            AccountId = accountId,
            Name = "SOGO Contacts",
            Type = AccountType.CardDav.ToString()
        });

        _credentialManager.StoreCardDavCredentials(accountId, new CardDavCredentials
        {
            Type = AccountType.CardDav.ToString(),
            ServerUrl = "https://carddav.example.com",
            Username = "user@example.com",
            Password = "secret"
        });

        _cardDavService.SetAddressBooks(new CardDavAddressBook
        {
            Url = addressBookUrl,
            DisplayName = "Default"
        });
        _cardDavService.SetContacts(addressBookUrl, new CardDavContact
        {
            Uid = "contact-1",
            Url = $"{addressBookUrl}/contact-1.vcf",
            DisplayName = "Alice Example",
            GivenName = "Alice",
            FamilyName = "Example",
            PrimaryEmail = "alice@example.com",
            ETag = "\"etag-1\"",
            RawVCard = "BEGIN:VCARD\nFN:Alice Example\nEND:VCARD"
        });

        var result = await _contactSyncService.ForceResyncAccountAsync(accountId);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Errors, Is.Empty);

        var addressBook = (await _storage.GetAddressBooksByAccountAsync(accountId)).Single();
        Assert.That(addressBook.ExternalId, Is.EqualTo(addressBookUrl));

        var contacts = (await _storage.GetContactsByAddressBookAsync(addressBook.AddressBookId)).ToList();
        Assert.That(contacts, Has.Count.EqualTo(1));
        Assert.That(contacts[0].ExternalId, Is.EqualTo("contact-1"));
        Assert.That(contacts[0].DisplayName, Is.EqualTo("Alice Example"));

        var contactDataRaw = await _storage.GetContactDataAsync(contacts[0].ContactId, "rawData");
        var resourceUrl = await _storage.GetContactDataAsync(contacts[0].ContactId, "resourceUrl");
        var etag = await _storage.GetContactDataAsync(contacts[0].ContactId, "etag");

        Assert.That(contactDataRaw, Does.Contain("FN:Alice Example"));
        Assert.That(resourceUrl, Is.EqualTo($"{addressBookUrl}/contact-1.vcf"));
        Assert.That(etag, Is.EqualTo("\"etag-1\""));
    }

    [Test]
    public async Task UpdateContactAsync_WithRelativeStoredResourceUrl_NormalizesToAbsoluteUrl()
    {
        var accountId = Guid.NewGuid().ToString();
        const string addressBookUrl = "https://carddav.example.com/addressbooks/default";

        await _storage.CreateAccountAsync(new AccountDbo
        {
            AccountId = accountId,
            Name = "SOGO Contacts",
            Type = AccountType.CardDav.ToString()
        });

        _credentialManager.StoreCardDavCredentials(accountId, new CardDavCredentials
        {
            Type = AccountType.CardDav.ToString(),
            ServerUrl = "https://carddav.example.com",
            Username = "user@example.com",
            Password = "secret"
        });

        _cardDavService.RequireAbsoluteUpdateUrl = true;

        var addressBook = new AddressBookDbo
        {
            AccountId = accountId,
            AddressBookId = string.Empty,
            ExternalId = addressBookUrl,
            Name = "Default",
            Enabled = 1,
            LastSync = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await _storage.CreateOrUpdateAddressBookAsync(addressBook);

        var contactId = await _storage.CreateOrUpdateContactAsync(new ContactDbo
        {
            AddressBookId = addressBook.AddressBookId,
            ContactId = Guid.NewGuid().ToString(),
            ExternalId = "contact-1",
            DisplayName = "Alice Example",
            GivenName = "Alice",
            FamilyName = "Example",
            PrimaryEmail = "alice@example.com",
            ChangedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        await _storage.SetContactDataAsync(contactId, "rawData", "BEGIN:VCARD\nUID:contact-1\nFN:Alice Example\nEND:VCARD");
        await _storage.SetContactDataAsync(contactId, "resourceUrl", "contact-1.vcf");
        await _storage.SetContactDataAsync(contactId, "etag", "\"etag-1\"");

        var hydratedContact = await _storage.GetHydratedContactByIdAsync(contactId);
        Assert.That(hydratedContact, Is.Not.Null);

        _provider.EnrichContact(hydratedContact!, key => _storage.GetContactDataAsync(contactId, key).GetAwaiter().GetResult());
        hydratedContact.DisplayName = "Alice Example Updated";

        var updatedContact = await _provider.UpdateContactAsync(hydratedContact);

        Assert.Multiple(() =>
        {
            Assert.That(_cardDavService.LastUpdatedContact, Is.Not.Null);
            Assert.That(_cardDavService.LastUpdatedContact!.Url, Is.EqualTo($"{addressBookUrl}/contact-1.vcf"));
            Assert.That(updatedContact.Extensions.Get(ContactExtensions.ProviderResource), Is.EqualTo($"{addressBookUrl}/contact-1.vcf"));
        });
    }
}
