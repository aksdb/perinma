namespace CredentialStore;

public class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, MemoryCredential> _credentials = new();
    private readonly object _lock = new();

    public IList<string> GetAccounts(string service)
    {
        lock (_lock)
        {
            return _credentials.Values
                .Where(c => service == null || StringComparer.OrdinalIgnoreCase.Equals(c.Service, service))
                .Select(c => c.Account)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public ICredential Get(string service, string account)
    {
        lock (_lock)
        {
            return _credentials.Values
                .FirstOrDefault(c =>
                    (service == null || StringComparer.OrdinalIgnoreCase.Equals(c.Service, service)) &&
                    (account == null || StringComparer.OrdinalIgnoreCase.Equals(c.Account, account)));
        }
    }

    public void AddOrUpdate(string service, string account, string secret)
    {
        lock (_lock)
        {
            string key = CreateKey(service, account);
            _credentials[key] = new MemoryCredential(service, account, secret);
        }
    }

    public bool Remove(string service, string account)
    {
        lock (_lock)
        {
            var credentialToRemove = _credentials.Values
                .FirstOrDefault(c =>
                    (service == null || StringComparer.OrdinalIgnoreCase.Equals(c.Service, service)) &&
                    (account == null || StringComparer.OrdinalIgnoreCase.Equals(c.Account, account)));

            if (credentialToRemove != null)
            {
                string key = CreateKey(credentialToRemove.Service, credentialToRemove.Account);
                return _credentials.Remove(key);
            }

            return false;
        }
    }

    private static string CreateKey(string service, string account)
    {
        return $"{service?.ToLowerInvariant() ?? string.Empty}:{account?.ToLowerInvariant() ?? string.Empty}";
    }

    private class MemoryCredential : ICredential
    {
        public MemoryCredential(string service, string account, string password)
        {
            Service = service;
            Account = account;
            Password = password;
        }

        public string Service { get; }
        public string Account { get; }
        public string Password { get; }
    }
}
