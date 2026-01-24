using System;

namespace perinma.Services;

/// <summary>
/// Exception thrown when a provider account requires re-authentication.
/// This typically happens when the refresh token is invalid or expired.
/// </summary>
public class ReAuthenticationRequiredException : Exception
{
    public string ProviderType { get; }
    public string AccountId { get; }

    public ReAuthenticationRequiredException(string providerType, string accountId, string? message = null)
        : base(message ?? $"Account '{accountId}' requires re-authentication for provider '{providerType}'")
    {
        ProviderType = providerType;
        AccountId = accountId;
    }
}
