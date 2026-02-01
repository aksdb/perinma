using System;
using perinma.Models;

namespace perinma.Storage.Models;

public class AccountDbo
{
    public required string AccountId { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public int SortOrder { get; set; }

    public AccountType AccountTypeEnum => 
        Enum.TryParse<AccountType>(Type, ignoreCase: true, out var result) 
            ? result 
            : throw new ArgumentException("Unknown account type.");
}

public class CalendarDbo
{
    public required string AccountId { get; set; }
    public required string CalendarId { get; set; }
    public string? ExternalId { get; set; }
    public required string Name { get; set; }
    public string? Color { get; set; }
    public int Enabled { get; set; }
    public long? LastSync { get; set; }
}

public class CalendarEventDbo
{
    public string CalendarId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public long? StartTime { get; set; }
    public long? EndTime { get; set; }
    public string? Title { get; set; }
    public long? ChangedAt { get; set; }
}

public class CalendarEventQueryResult
{
    public required string EventId { get; init; }
    public string? ExternalId { get; init; }
    public long? StartTime { get; init; }
    public long? EndTime { get; init; }
    public string? Title { get; init; }
    public long? ChangedAt { get; init; }
    public string? RawData { get; init; }
    public required string CalendarId { get; init; }
    public string? CalendarExternalId { get; init; }
    public required string CalendarName { get; init; }
    public string? CalendarColor { get; init; }
    public int CalendarEnabled { get; init; }
    public long? CalendarLastSync { get; init; }
    public required string AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountType { get; init; }

    public AccountType AccountTypeEnum => Enum.TryParse<AccountType>(AccountType, ignoreCase: true, out var result) ? result : perinma.Models.AccountType.Google;
}

public class AddressBookDbo
{
    public required string AccountId { get; set; }
    public required string AddressBookId { get; set; }
    public string? ExternalId { get; set; }
    public required string Name { get; set; }
    public int Enabled { get; set; }
    public long? LastSync { get; set; }
}

public class ContactDbo
{
    public string AddressBookId { get; set; } = string.Empty;
    public string ContactId { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? DisplayName { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? PrimaryEmail { get; set; }
    public string? PrimaryPhone { get; set; }
    public string? PhotoUrl { get; set; }
    public long? ChangedAt { get; set; }
}

public class ContactGroupDbo
{
    public required string AccountId { get; set; }
    public required string GroupId { get; set; }
    public string? ExternalId { get; set; }
    public required string Name { get; set; }
    public int SystemGroup { get; set; }
}

public class ContactQueryResult
{
    public required string ContactId { get; init; }
    public string? ExternalId { get; init; }
    public string? DisplayName { get; init; }
    public string? GivenName { get; init; }
    public string? FamilyName { get; init; }
    public string? PrimaryEmail { get; init; }
    public string? PrimaryPhone { get; init; }
    public string? PhotoUrl { get; init; }
    public long? ChangedAt { get; init; }
    public string? RawData { get; init; }
    public required string AddressBookId { get; init; }
    public string? AddressBookExternalId { get; init; }
    public required string AddressBookName { get; init; }
    public int AddressBookEnabled { get; init; }
    public long? AddressBookLastSync { get; init; }
    public required string AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountType { get; init; }

    public AccountType AccountTypeEnum => Enum.TryParse<AccountType>(AccountType, ignoreCase: true, out var result) ? result : perinma.Models.AccountType.Google;
}
