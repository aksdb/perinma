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
    public ModelExtensions Extensions { get; } = new();
}

public static class AddressBookExtensions
{
    /// <summary>
    /// True when the user cannot create, update, or delete contacts in this address book.
    /// Absent (default false) = writable — the safe fallback.
    /// </summary>
    public static ModelExtension<bool> IsReadOnly = new();
}
