using System;

namespace perinma.Models;

public class Contact
{
    public required AddressBook AddressBook { get; set; }
    public required Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public string? DisplayName { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? PrimaryEmail { get; set; }
    public string? PrimaryPhone { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime? ChangedAt { get; set; }
}
