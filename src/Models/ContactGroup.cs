using System;

namespace perinma.Models;

public class ContactGroup
{
    public required Account Account { get; set; }
    public required Guid Id { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool SystemGroup { get; set; }
}
