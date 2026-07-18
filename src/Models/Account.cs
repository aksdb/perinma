using System;

namespace perinma.Models;

public enum AccountType
{
    Google,
    CalDav,
    CardDav,
    Jmap
}

[Flags]
public enum AccountCapability
{
    None = 0,
    Calendar = 1,
    Contacts = 1 << 1,
    Mail = 1 << 2
}

public class Account
{
    public required Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public AccountCapability Capabilities { get; set; }
    public int SortOrder { get; set; }

    public bool SupportsCalendar => Capabilities.HasFlag(AccountCapability.Calendar);
    public bool SupportsContacts => Capabilities.HasFlag(AccountCapability.Contacts);
    public bool SupportsMail => Capabilities.HasFlag(AccountCapability.Mail);
}
