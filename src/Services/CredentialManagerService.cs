using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CredentialStore;
using perinma.Storage.Models;

namespace perinma.Services;

public class CredentialManagerService(ICredentialStore store)
{
    public void StoreGoogleCredentials(string accountId, GoogleCredentials credentials) =>
        StoreCredentials(accountId, credentials, CredentialsContext.Default.GoogleCredentials);

    public GoogleCredentials? GetGoogleCredentials(string accountId) =>
        GetCredentials(accountId, CredentialsContext.Default.GoogleCredentials);

    public void StoreCalDavCredentials(string accountId, CalDavCredentials credentials) =>
        StoreCredentials(accountId, credentials, CredentialsContext.Default.CalDavCredentials);

    public CalDavCredentials? GetCalDavCredentials(string accountId) =>
        GetCredentials(accountId, CredentialsContext.Default.CalDavCredentials);

    public void StoreCardDavCredentials(string accountId, CardDavCredentials credentials) =>
        StoreCredentials(accountId, credentials, CredentialsContext.Default.CardDavCredentials);

    public CardDavCredentials? GetCardDavCredentials(string accountId) =>
        GetCredentials(accountId, CredentialsContext.Default.CardDavCredentials);

    public void StoreJmapCredentials(string accountId, JmapCredentials credentials) =>
        StoreCredentials(accountId, credentials, CredentialsContext.Default.JmapCredentials);

    public JmapCredentials? GetJmapCredentials(string accountId) =>
        GetCredentials(accountId, CredentialsContext.Default.JmapCredentials);

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

    private void StoreCredentials<T>(string accountId, T credentials, JsonTypeInfo<T> typeInfo)
    {
        var service = GetServiceName(accountId);
        var json = JsonSerializer.Serialize(credentials, typeInfo);
        store.AddOrUpdate(service, accountId, json);
    }

    private T? GetCredentials<T>(string accountId, JsonTypeInfo<T> typeInfo) where T : class
    {
        var service = GetServiceName(accountId);
        var credential = store.Get(service, accountId);
        if (credential == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(credential.Password, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetServiceName(string accountId)
    {
        return $"account:{accountId}";
    }
}