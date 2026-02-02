using System.Collections.Generic;
using System.Linq;

namespace perinma.Views.Contacts;

/// <summary>
/// Represents an email address with optional label and primary indicator
/// </summary>
public sealed record ContactEmailField(string Value, string? Label, bool IsPrimary)
{
    /// <summary>
    /// Gets a display-friendly label (e.g., "Work", "Home", or "Email")
    /// </summary>
    public string DisplayLabel => FormatLabel(Label) ?? "Email";

    private static string? FormatLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        // Google uses lowercase labels like "work", "home", "other"
        return char.ToUpperInvariant(label[0]) + label[1..].ToLowerInvariant();
    }
}

/// <summary>
/// Represents a phone number with optional label and primary indicator
/// </summary>
public sealed record ContactPhoneField(string Value, string? Label, bool IsPrimary)
{
    /// <summary>
    /// Gets a display-friendly label (e.g., "Mobile", "Work", or "Phone")
    /// </summary>
    public string DisplayLabel => FormatLabel(Label) ?? "Phone";

    private static string? FormatLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        return char.ToUpperInvariant(label[0]) + label[1..].ToLowerInvariant();
    }
}

/// <summary>
/// Represents a postal address with optional label
/// </summary>
public sealed record ContactAddressField(
    string? FormattedValue,
    string? StreetAddress,
    string? City,
    string? Region,
    string? PostalCode,
    string? Country,
    string? Label,
    bool IsPrimary)
{
    /// <summary>
    /// Gets a display-friendly label (e.g., "Work", "Home", or "Address")
    /// </summary>
    public string DisplayLabel => FormatLabel(Label) ?? "Address";

    /// <summary>
    /// Gets the formatted address for display, preferring FormattedValue if available
    /// </summary>
    public string DisplayValue
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FormattedValue))
                return FormattedValue;

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(StreetAddress))
                parts.Add(StreetAddress);

            var cityLine = string.Join(", ",
                new[] { City, Region, PostalCode }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

            if (!string.IsNullOrEmpty(cityLine))
                parts.Add(cityLine);

            if (!string.IsNullOrWhiteSpace(Country))
                parts.Add(Country);

            return string.Join("\n", parts);
        }
    }

    private static string? FormatLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        return char.ToUpperInvariant(label[0]) + label[1..].ToLowerInvariant();
    }
}
