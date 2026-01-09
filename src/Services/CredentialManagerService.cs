using System.Text.Json;
using CredentialStore;
using perinma.Storage.Models;

namespace perinma.Services;

/// <summary>
/// Manages credentials using platform-specific secure storage (Windows Credential Manager, macOS Keychain, Linux Secret Service).
/// </summary>
public class CredentialManagerService
{
    private readonly ICredentialStore? _store;
    private const string Namespace = "perinma";

    public CredentialManagerService() : this(testMode: false)
    {
    }

    protected CredentialManagerService(bool testMode)
    {
        if (!testMode)
        {
            _store = CredentialStore.CredentialStore.Create(Namespace);
        }
    }

    /// <summary>
    /// Stores Google credentials securely in the platform keyring.
    /// </summary>
    /// <param name="accountId">Unique identifier for the account</param>
    /// <param name="credentials">Google credentials to store</param>
    public virtual void StoreGoogleCredentials(string accountId, GoogleCredentials credentials)
    {
        if (_store == null) return;
        var service = GetServiceName(accountId);
        var json = JsonSerializer.Serialize(credentials);
        _store.AddOrUpdate(service, accountId, json);
    }

    /// <summary>
    /// Retrieves Google credentials from the platform keyring.
    /// </summary>
    /// <param name="accountId">Unique identifier for the account</param>
    /// <returns>Google credentials if found, null otherwise</returns>
    public virtual GoogleCredentials? GetGoogleCredentials(string accountId)
    {
        if (_store == null) return null;
        var service = GetServiceName(accountId);
        var credential = _store.Get(service, accountId);

        if (credential == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GoogleCredentials>(credential.Password);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Stores CalDAV credentials securely in the platform keyring.
    /// </summary>
    /// <param name="accountId">Unique identifier for the account</param>
    /// <param name="credentials">CalDAV credentials to store</param>
    public virtual void StoreCalDavCredentials(string accountId, CalDavCredentials credentials)
    {
        if (_store == null) return;
        var service = GetServiceName(accountId);
        var json = JsonSerializer.Serialize(credentials);
        _store.AddOrUpdate(service, accountId, json);
    }

    /// <summary>
    /// Retrieves CalDAV credentials from the platform keyring.
    /// </summary>
    /// <param name="accountId">Unique identifier for the account</param>
    /// <returns>CalDAV credentials if found, null otherwise</returns>
    public virtual CalDavCredentials? GetCalDavCredentials(string accountId)
    {
        if (_store == null) return null;
        var service = GetServiceName(accountId);
        var credential = _store.Get(service, accountId);

        if (credential == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CalDavCredentials>(credential.Password);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes credentials from the platform keyring.
    /// </summary>
    /// <param name="accountId">Unique identifier for the account</param>
    /// <returns>True if credentials were deleted, false if they didn't exist</returns>
    public virtual bool DeleteCredentials(string accountId)
    {
        if (_store == null) return false;
        var service = GetServiceName(accountId);
        return _store.Remove(service, accountId);
    }

    /// <summary>
    /// Checks if credentials exist for an account.
    /// </summary>
    /// <param name="accountId">Unique identifier for the account</param>
    /// <returns>True if credentials exist, false otherwise</returns>
    public virtual bool HasCredentials(string accountId)
    {
        if (_store == null) return false;
        var service = GetServiceName(accountId);
        var credential = _store.Get(service, accountId);
        return credential != null;
    }

    private static string GetServiceName(string accountId)
    {
        // Use a URL-like format for the service name to match GCM conventions
        return $"https://{Namespace}.app/account/{accountId}";
    }
}
