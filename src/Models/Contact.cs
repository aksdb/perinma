using System;

namespace perinma.Models;

public record ContactReference
{
    public required AddressBook AddressBook { get; init; }
    public required Guid Id { get; init; }
    public string? ExternalId { get; init; }
}

public class Contact
{
    public required ContactReference Reference { get; set; }
    public string? DisplayName { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? PrimaryEmail { get; set; }
    public string? PrimaryPhone { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime? ChangedAt { get; set; }
    public ModelExtensions Extensions { get; init; } = new();
}

public static class ContactExtensions
{
    public static readonly ModelExtension<string> ProviderResource = new();
    public static readonly ModelExtension<string> ProviderETag = new();
    public static readonly ModelExtension<bool> IsReadOnly = new();
}
