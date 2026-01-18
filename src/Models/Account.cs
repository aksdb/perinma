using System;

namespace perinma.Models;

public enum AccountType
{
    Google,
    CalDav
}

public class Account
{
    public required Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
}
