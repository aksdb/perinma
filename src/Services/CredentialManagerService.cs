using System.Text.Json;
using CredentialStore;
using perinma.Storage.Models;

namespace perinma.Services;

public class CredentialManagerService(ICredentialStore store)
{
    public void StoreGoogleCredentials(string accountId, GoogleCredentials credentials)
    {
        var service = GetServiceName(accountId);
        var json = JsonSerializer.Serialize(credentials, CredentialsContext.Default.GoogleCredentials);
        store.AddOrUpdate(service, accountId, json);
    }

    public GoogleCredentials? GetGoogleCredentials(string accountId)
    {
        var service = GetServiceName(accountId);
        var credential = store.Get(service, accountId);

        if (credential == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(credential.Password, CredentialsContext.Default.GoogleCredentials);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void StoreCalDavCredentials(string accountId, CalDavCredentials credentials)
    {
        var service = GetServiceName(accountId);
        var json = JsonSerializer.Serialize(credentials, CredentialsContext.Default.CalDavCredentials);
        store.AddOrUpdate(service, accountId, json);
    }

    public CalDavCredentials? GetCalDavCredentials(string accountId)
    {
        var service = GetServiceName(accountId);
        var credential = store.Get(service, accountId);

        if (credential == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(credential.Password, CredentialsContext.Default.CalDavCredentials);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool DeleteCredentials(string accountId)
    {
        var service = GetServiceName(accountId);
        return store.Remove(service, accountId);
    }

    public bool HasCredentials(string accountId)
    {
        var service = GetServiceName(accountId);
        var credential = store.Get(service, accountId);
        return credential != null;
    }

    private static string GetServiceName(string accountId)
    {
        // Use a URL-like format for the service name to match GCM conventions
        return $"account:{accountId}";
    }
}