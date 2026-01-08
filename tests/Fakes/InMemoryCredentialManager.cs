using System.Collections.Generic;
using perinma.Services;
using perinma.Storage.Models;

namespace perinma.Tests.Fakes;

/// <summary>
/// In-memory implementation of credential manager for testing
/// Extends CredentialManagerService but stores credentials in memory instead of platform keyring
/// </summary>
public class TestCredentialManager : CredentialManagerService
{
    private readonly Dictionary<string, object> _credentials = new();

    // Constructor that doesn't call base constructor (which would try to initialize GCM)
    public TestCredentialManager() : base(testMode: true)
    {
    }

    public override void StoreGoogleCredentials(string accountId, GoogleCredentials credentials)
    {
        _credentials[accountId] = credentials;
    }

    public override GoogleCredentials? GetGoogleCredentials(string accountId)
    {
        if (_credentials.TryGetValue(accountId, out var credentials) && credentials is GoogleCredentials googleCreds)
        {
            return googleCreds;
        }
        return null;
    }

    public override void StoreCalDavCredentials(string accountId, CalDavCredentials credentials)
    {
        _credentials[accountId] = credentials;
    }

    public override CalDavCredentials? GetCalDavCredentials(string accountId)
    {
        if (_credentials.TryGetValue(accountId, out var credentials) && credentials is CalDavCredentials calDavCreds)
        {
            return calDavCreds;
        }
        return null;
    }

    public override bool DeleteCredentials(string accountId)
    {
        return _credentials.Remove(accountId);
    }

    public override bool HasCredentials(string accountId)
    {
        return _credentials.ContainsKey(accountId);
    }
}
