using System;

namespace perinma.Models;

public enum AccountType
{
    Google,
    CalDav,
    CardDav
}

public class Account
{
    public required Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public int SortOrder { get; set; }
}
