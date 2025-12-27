using System;

namespace perinma.Models;

public class Account
{
    public required Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
