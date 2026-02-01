using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Json;
using Google.Apis.PeopleService.v1.Data;

namespace perinma.Services.Google;

/// <summary>
/// Google People API implementation of IContactProvider.
/// </summary>
public class GoogleContactProvider : IContactProvider
{
    private readonly IGooglePeopleService _googlePeopleService;
    private readonly CredentialManagerService _credentialManager;

    // Google uses a single virtual "address book" for all contacts
    private const string DefaultAddressBookExternalId = "people/me";
    private const string DefaultAddressBookName = "Contacts";

    public GoogleContactProvider(
        IGooglePeopleService googlePeopleService,
        CredentialManagerService credentialManager)
    {
        _googlePeopleService = googlePeopleService;
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
        // Google doesn't have multiple address books - return a single virtual one
        // We still verify credentials work by testing the connection
        var googleCredentials = _credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        // Create service to verify credentials are valid
        await _googlePeopleService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);

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
            SyncToken = null // No sync token for address books in Google
        };
    }

    /// <inheritdoc/>
    public async Task<ContactSyncResult> GetContactsAsync(
        string accountId,
        string addressBookExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = _credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        var service = await _googlePeopleService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);
        var result = await _googlePeopleService.GetContactsAsync(service, syncToken, cancellationToken);

        var contacts = new List<ProviderContact>();

        foreach (var person in result.Contacts)
        {
            var providerContact = ConvertPerson(person);
            if (providerContact != null)
            {
                contacts.Add(providerContact);
            }
        }

        return new ContactSyncResult
        {
            Contacts = contacts,
            SyncToken = result.SyncToken
        };
    }

    /// <inheritdoc/>
    public async Task<ContactGroupSyncResult> GetContactGroupsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var googleCredentials = _credentialManager.GetGoogleCredentials(accountId);
        if (googleCredentials == null)
        {
            throw new InvalidOperationException($"No Google credentials found for account {accountId}");
        }

        var service = await _googlePeopleService.CreateServiceAsync(googleCredentials, cancellationToken, accountId);
        var result = await _googlePeopleService.GetContactGroupsAsync(service, syncToken, cancellationToken);

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

    /// <inheritdoc/>
    public async Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var googleCredentials = _credentialManager.GetGoogleCredentials(accountId);
            if (googleCredentials == null)
            {
                return false;
            }

            var service = await _googlePeopleService.CreateServiceAsync(googleCredentials, cancellationToken);

            // Try to fetch a small number of contacts as a connection test
            await _googlePeopleService.GetContactsAsync(service, null, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Google People API connection test failed: {ex.Message}");
            return false;
        }
    }

    private static ProviderContact? ConvertPerson(Person person)
    {
        if (string.IsNullOrEmpty(person.ResourceName))
        {
            return null;
        }

        // Extract primary name
        var primaryName = person.Names?.FirstOrDefault(n => n.Metadata?.Primary == true)
                          ?? person.Names?.FirstOrDefault();

        // Extract primary email
        var primaryEmail = person.EmailAddresses?.FirstOrDefault(e => e.Metadata?.Primary == true)
                           ?? person.EmailAddresses?.FirstOrDefault();

        // Extract primary phone
        var primaryPhone = person.PhoneNumbers?.FirstOrDefault(p => p.Metadata?.Primary == true)
                           ?? person.PhoneNumbers?.FirstOrDefault();

        // Extract photo URL
        var photo = person.Photos?.FirstOrDefault(p => p.Metadata?.Primary == true)
                    ?? person.Photos?.FirstOrDefault();

        // Extract group memberships
        var groupIds = person.Memberships?
            .Where(m => m.ContactGroupMembership != null)
            .Select(m => m.ContactGroupMembership!.ContactGroupResourceName)
            .Where(id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .ToList();

        // Check if contact is deleted (for incremental sync)
        var isDeleted = person.Metadata?.Deleted == true;

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
            GroupExternalIds = groupIds
        };
    }
}
