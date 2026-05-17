using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Json;
using Google.Apis.PeopleService.v1.Data;
using perinma.Models;

namespace perinma.Services.Google;

/// <summary>
/// Google People API implementation of IContactProvider.
/// </summary>
public class GoogleContactProvider(
    IGooglePeopleService googlePeopleService,
    CredentialManagerService credentialManager)
    : IContactProvider
{
    private const string DefaultAddressBookExternalId = "people/me";
    private const string DefaultAddressBookName = "Contacts";
    private const string ContactResourceKey = "providerResource";
    private const string ContactEtagKey = "providerETag";
    private const string UpdatePersonFields = "names,emailAddresses,phoneNumbers";
    private static readonly ModelExtension<Person> GooglePersonExtension = new();

    public CredentialManagerService CredentialManager => credentialManager;

    public async Task<AddressBookSyncResult> GetAddressBooksAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");

        await googlePeopleService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

        return new AddressBookSyncResult
        {
            AddressBooks =
            [
                new ProviderAddressBook
                {
                    ExternalId = DefaultAddressBookExternalId,
                    Name = DefaultAddressBookName,
                    Deleted = false
                }
            ],
            SyncToken = null
        };
    }

    public async Task<ContactSyncResult> GetContactsAsync(
        string accountId,
        string addressBookExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");

        var service = await googlePeopleService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);
        var result = await googlePeopleService.GetContactsAsync(service, syncToken, cancellationToken);

        var contacts = result.Contacts
            .Select(ConvertPerson)
            .Where(providerContact => providerContact != null)
            .Cast<ProviderContact>()
            .ToList();

        return new ContactSyncResult
        {
            Contacts = contacts,
            SyncToken = result.SyncToken
        };
    }

    public async Task<ContactGroupSyncResult> GetContactGroupsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");

        var service = await googlePeopleService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);
        var result = await googlePeopleService.GetContactGroupsAsync(service, syncToken, cancellationToken);

        var groups = result.Groups.Select(g => new ProviderContactGroup
        {
            ExternalId = g.ResourceName ?? string.Empty,
            Name = g.Name ?? "Unnamed Group",
            SystemGroup = g.GroupType == "SYSTEM_CONTACT_GROUP",
            Deleted = false
        }).ToList();

        return new ContactGroupSyncResult
        {
            Groups = groups,
            SyncToken = result.SyncToken
        };
    }

    public void EnrichContact(Contact contact, Func<string, string?> getData)
    {
        TryEnrichContact(contact, getData("rawData"));
    }

    public async Task<Contact> CreateContactAsync(
        AddressBook addressBook,
        Contact contact,
        CancellationToken cancellationToken = default)
    {
        var accountId = addressBook.Account.Id.ToString();
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");

        var service = await googlePeopleService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);
        var created = await googlePeopleService.CreateContactAsync(service, BuildPerson(contact), cancellationToken);
        return MapToContact(addressBook, created, contact.Reference.Id);
    }

    public async Task<Contact> UpdateContactAsync(
        Contact contact,
        CancellationToken cancellationToken = default)
    {
        var accountId = contact.Reference.AddressBook.Account.Id.ToString();
        var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");

        var existingPerson = GetGooglePerson(contact)
            ?? throw new InvalidOperationException("Google contact metadata is missing");
        var contactSource = GetContactSource(existingPerson)
            ?? throw new InvalidOperationException("Google contact source metadata is missing");

        var service = await googlePeopleService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);
        var updated = await googlePeopleService.UpdateContactAsync(
            service,
            BuildPerson(contact, existingPerson, contactSource),
            UpdatePersonFields,
            cancellationToken);

        return MapToContact(contact.Reference.AddressBook, updated, contact.Reference.Id);
    }

    public IList<object> GetSupportedExtensions() =>
    [
        ContactExtensions.ProviderResource,
        ContactExtensions.ProviderETag,
        ContactExtensions.IsReadOnly
    ];

    public async Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var googleCredentials = credentialManager.GetGoogleCredentials(accountId);
            if (googleCredentials == null)
                return false;

            var service = await googlePeopleService.CreateServiceAsync(googleCredentials, cancellationToken);
            await googlePeopleService.GetContactsAsync(service, null, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Google People API connection test failed: {ex.Message}");
            return false;
        }
    }

    public static bool TryEnrichContact(Contact contact, string? rawData)
    {
        if (string.IsNullOrWhiteSpace(rawData))
            return false;

        var person = NewtonsoftJsonSerializer.Instance.Deserialize<Person>(rawData);
        if (person == null)
            return false;

        contact.Extensions.Set(GooglePersonExtension, person);

        var contactSource = GetContactSource(person);
        if (!string.IsNullOrWhiteSpace(contactSource?.Etag))
            contact.Extensions.Set(ContactExtensions.ProviderETag, contactSource.Etag);

        if (!string.IsNullOrWhiteSpace(person.ResourceName))
            contact.Extensions.Set(ContactExtensions.ProviderResource, person.ResourceName);

        if (contactSource == null)
            contact.Extensions.Set(ContactExtensions.IsReadOnly, true);

        return true;
    }

    public static Person? GetGooglePerson(Contact contact) => contact.Extensions.Get(GooglePersonExtension);

    private static Contact MapToContact(AddressBook addressBook, Person person, Guid contactId)
    {
        var primaryName = person.Names?.FirstOrDefault(name => name.Metadata?.Primary == true)
                          ?? person.Names?.FirstOrDefault();
        var primaryEmail = person.EmailAddresses?.FirstOrDefault(email => email.Metadata?.Primary == true)
                           ?? person.EmailAddresses?.FirstOrDefault();
        var primaryPhone = person.PhoneNumbers?.FirstOrDefault(phone => phone.Metadata?.Primary == true)
                           ?? person.PhoneNumbers?.FirstOrDefault();
        var photo = person.Photos?.FirstOrDefault(p => p.Metadata?.Primary == true)
                    ?? person.Photos?.FirstOrDefault();

        var hydratedContact = new Contact
        {
            Reference = new ContactReference
            {
                AddressBook = addressBook,
                Id = contactId,
                ExternalId = person.ResourceName
            },
            DisplayName = primaryName?.DisplayName,
            GivenName = primaryName?.GivenName,
            FamilyName = primaryName?.FamilyName,
            PrimaryEmail = primaryEmail?.Value,
            PrimaryPhone = primaryPhone?.Value,
            PhotoUrl = photo?.Url,
        };

        TryEnrichContact(hydratedContact, NewtonsoftJsonSerializer.Instance.Serialize(person));
        return hydratedContact;
    }

    private static Person BuildPerson(Contact contact, Person? existingPerson = null, Source? contactSource = null)
    {
        var displayName = string.IsNullOrWhiteSpace(contact.DisplayName)
            ? string.Join(' ', new[] { contact.GivenName, contact.FamilyName }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim()
            : contact.DisplayName;

        var person = new Person
        {
            ResourceName = existingPerson?.ResourceName ?? contact.Reference.ExternalId ?? contact.Extensions.Get(ContactExtensions.ProviderResource),
            Names =
            [
                new Name
                {
                    DisplayName = displayName,
                    GivenName = contact.GivenName,
                    FamilyName = contact.FamilyName
                }
            ],
            EmailAddresses = string.IsNullOrWhiteSpace(contact.PrimaryEmail)
                ? null
                : [new EmailAddress { Value = contact.PrimaryEmail }],
            PhoneNumbers = string.IsNullOrWhiteSpace(contact.PrimaryPhone)
                ? null
                : [new PhoneNumber { Value = contact.PrimaryPhone }],
        };

        if (contactSource != null)
        {
            person.Metadata = new PersonMetadata
            {
                Sources =
                [
                    new Source
                    {
                        Type = contactSource.Type,
                        Id = contactSource.Id,
                        Etag = contactSource.Etag,
                        UpdateTime = contactSource.UpdateTime,
                        ProfileMetadata = contactSource.ProfileMetadata,
                        SourcePrimary = contactSource.SourcePrimary
                    }
                ]
            };
        }

        return person;
    }

    private static ProviderContact? ConvertPerson(Person person)
    {
        if (string.IsNullOrEmpty(person.ResourceName))
            return null;

        var primaryName = person.Names?.FirstOrDefault(n => n.Metadata?.Primary == true)
                          ?? person.Names?.FirstOrDefault();
        var primaryEmail = person.EmailAddresses?.FirstOrDefault(e => e.Metadata?.Primary == true)
                           ?? person.EmailAddresses?.FirstOrDefault();
        var primaryPhone = person.PhoneNumbers?.FirstOrDefault(p => p.Metadata?.Primary == true)
                           ?? person.PhoneNumbers?.FirstOrDefault();
        var photo = person.Photos?.FirstOrDefault(p => p.Metadata?.Primary == true)
                    ?? person.Photos?.FirstOrDefault();
        var groupIds = person.Memberships?
            .Where(m => m.ContactGroupMembership != null)
            .Select(m => m.ContactGroupMembership!.ContactGroupResourceName)
            .Where(id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .ToList();

        var hasNoData = primaryName == null
                        && primaryEmail == null
                        && primaryPhone == null
                        && (person.Memberships == null || person.Memberships.Count == 0);
        var isDeleted = person.Metadata?.Deleted == true || hasNoData;
        var contactSource = GetContactSource(person);

        var data = new Dictionary<string, DataAttribute>();
        if (!string.IsNullOrWhiteSpace(person.ResourceName))
            data[ContactResourceKey] = new DataAttribute.Text(person.ResourceName);
        if (!string.IsNullOrWhiteSpace(contactSource?.Etag))
            data[ContactEtagKey] = new DataAttribute.Text(contactSource.Etag);

        return new ProviderContact
        {
            ExternalId = person.ResourceName,
            DisplayName = primaryName?.DisplayName,
            GivenName = primaryName?.GivenName,
            FamilyName = primaryName?.FamilyName,
            PrimaryEmail = primaryEmail?.Value,
            PrimaryPhone = primaryPhone?.Value,
            PhotoUrl = photo?.Url,
            Deleted = isDeleted,
            RawData = NewtonsoftJsonSerializer.Instance.Serialize(person),
            Data = data,
            GroupExternalIds = groupIds
        };
    }

    private static Source? GetContactSource(Person person) =>
        person.Metadata?.Sources?.FirstOrDefault(source => string.Equals(source.Type, "CONTACT", StringComparison.OrdinalIgnoreCase));
}