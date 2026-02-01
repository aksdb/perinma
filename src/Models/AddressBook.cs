using System;

namespace perinma.Models;

public class AddressBook
{
    public required Account Account { get; set; }
    public required Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public DateTime? LastSync { get; set; }
}
