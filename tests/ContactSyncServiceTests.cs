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

    [SetUp]
    public void SetUp()
    {
        _database = new DatabaseService(inMemory: true);
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        _storage = new SqliteStorage(_database, _credentialManager);
        _cardDavService = new CardDavServiceStub();
        var provider = new CardDavContactProvider(_cardDavService, _credentialManager);
        _contactSyncService = new ContactSyncService(_storage, new System.Collections.Generic.Dictionary<AccountType, IContactProvider>
        {
            [AccountType.CardDav] = provider
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
    }
}
