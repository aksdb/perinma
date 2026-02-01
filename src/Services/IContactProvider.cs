using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace perinma.Services;

/// <summary>
/// Interface for contact providers (Google People API, CardDAV, etc.).
/// Provides a unified abstraction for syncing contacts from different sources.
/// </summary>
public interface IContactProvider
{
    /// <summary>
    /// Gets the credential manager service used by this provider.
    /// </summary>
    CredentialManagerService CredentialManager { get; }

    /// <summary>
    /// Syncs address books for an account, optionally using incremental sync.
    /// </summary>
    /// <param name="accountId">Account ID to sync address books for</param>
    /// <param name="syncToken">Optional sync token for incremental sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing address books and new sync token</returns>
    Task<AddressBookSyncResult> GetAddressBooksAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs contacts for a specific address book, optionally using incremental sync.
    /// </summary>
    /// <param name="accountId">Account ID to sync contacts for</param>
    /// <param name="addressBookExternalId">External ID of the address book to sync</param>
    /// <param name="syncToken">Optional sync token for incremental sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing contacts and new sync token</returns>
    Task<ContactSyncResult> GetContactsAsync(
        string accountId,
        string addressBookExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs contact groups/labels for an account.
    /// </summary>
    /// <param name="accountId">Account ID to sync groups for</param>
    /// <param name="syncToken">Optional sync token for incremental sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing contact groups and new sync token</returns>
    Task<ContactGroupSyncResult> GetContactGroupsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests whether the connection to the provider is working with the given account.
    /// </summary>
    /// <param name="accountId">Account ID to test connection for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connection is successful, false otherwise</returns>
    Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of syncing address books from a provider.
/// </summary>
public class AddressBookSyncResult
{
    /// <summary>
    /// Address books returned from the sync operation.
    /// May include deleted address books with Deleted=true for incremental sync.
    /// </summary>
    public required IList<ProviderAddressBook> AddressBooks { get; init; }

    /// <summary>
    /// Sync token to use for the next incremental sync.
    /// </summary>
    public string? SyncToken { get; init; }
}

/// <summary>
/// Result of syncing contacts from a provider.
/// </summary>
public class ContactSyncResult
{
    /// <summary>
    /// Contacts returned from the sync operation.
    /// May include deleted contacts for incremental sync.
    /// </summary>
    public required IList<ProviderContact> Contacts { get; init; }

    /// <summary>
    /// Sync token to use for the next incremental sync.
    /// </summary>
    public string? SyncToken { get; init; }
}

/// <summary>
/// Result of syncing contact groups from a provider.
/// </summary>
public class ContactGroupSyncResult
{
    /// <summary>
    /// Contact groups returned from the sync operation.
    /// </summary>
    public required IList<ProviderContactGroup> Groups { get; init; }

    /// <summary>
    /// Sync token to use for the next incremental sync.
    /// </summary>
    public string? SyncToken { get; init; }
}

/// <summary>
/// Provider-agnostic address book representation for sync operations.
/// </summary>
public class ProviderAddressBook
{
    /// <summary>
    /// External ID of the address book (provider-specific identifier).
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// Display name of the address book.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether the address book has been deleted (for incremental sync).
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// Provider specific data.
    /// </summary>
    public Dictionary<string, DataAttribute> Data { get; init; } = new();
}

/// <summary>
/// Provider-agnostic contact representation for sync operations.
/// </summary>
public class ProviderContact
{
    /// <summary>
    /// External ID of the contact (provider-specific identifier).
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// Display name of the contact.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Given (first) name.
    /// </summary>
    public string? GivenName { get; init; }

    /// <summary>
    /// Family (last) name.
    /// </summary>
    public string? FamilyName { get; init; }

    /// <summary>
    /// Primary email address.
    /// </summary>
    public string? PrimaryEmail { get; init; }

    /// <summary>
    /// Primary phone number.
    /// </summary>
    public string? PrimaryPhone { get; init; }

    /// <summary>
    /// URL to the contact's photo.
    /// </summary>
    public string? PhotoUrl { get; init; }

    /// <summary>
    /// Whether this contact is deleted (for incremental sync).
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// Raw provider data serialized as string for later use (JSON or vCard).
    /// </summary>
    public string? RawData { get; init; }

    /// <summary>
    /// External IDs of groups this contact belongs to.
    /// </summary>
    public IList<string>? GroupExternalIds { get; init; }
}

/// <summary>
/// Provider-agnostic contact group representation for sync operations.
/// </summary>
public class ProviderContactGroup
{
    /// <summary>
    /// External ID of the group (provider-specific identifier).
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// Display name of the group.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether this is a system group (e.g., "My Contacts" in Google).
    /// </summary>
    public bool SystemGroup { get; init; }

    /// <summary>
    /// Whether this group is deleted (for incremental sync).
    /// </summary>
    public bool Deleted { get; init; }
}
