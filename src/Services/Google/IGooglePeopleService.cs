using System.Threading;
using System.Threading.Tasks;
using Google.Apis.PeopleService.v1;
using perinma.Storage.Models;

namespace perinma.Services.Google;

/// <summary>
/// Interface for Google People API operations
/// </summary>
public interface IGooglePeopleService
{
    /// <summary>
    /// Creates a PeopleServiceService from GoogleCredentials
    /// </summary>
    Task<PeopleServiceService> CreateServiceAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken = default,
        string? accountId = null);

    /// <summary>
    /// Fetches all contacts for the authenticated user, optionally using incremental sync
    /// </summary>
    Task<GooglePeopleService.ContactSyncResult> GetContactsAsync(
        PeopleServiceService service,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches contact groups for the authenticated user
    /// </summary>
    Task<GooglePeopleService.ContactGroupSyncResult> GetContactGroupsAsync(
        PeopleServiceService service,
        string? syncToken = null,
        CancellationToken cancellationToken = default);
}
