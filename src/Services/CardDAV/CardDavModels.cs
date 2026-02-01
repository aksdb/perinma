namespace perinma.Services.CardDAV;

public class CardDavAddressBook
{
    public required string Url { get; init; }
    public required string DisplayName { get; init; }
    public string? CTag { get; init; }
    public bool Deleted { get; init; }
}

public class CardDavContact
{
    public required string Uid { get; init; }
    public required string Url { get; init; }
    public string? DisplayName { get; init; }
    public string? GivenName { get; init; }
    public string? FamilyName { get; init; }
    public string? PrimaryEmail { get; init; }
    public string? PrimaryPhone { get; init; }
    public string? PhotoUrl { get; init; }
    public string? ETag { get; init; }
    public string? RawVCard { get; init; }
    public bool Deleted { get; init; }
}
