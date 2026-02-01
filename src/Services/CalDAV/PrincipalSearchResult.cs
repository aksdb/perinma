using System;
using System.Collections.Generic;

namespace perinma.Services.CalDAV;

/// <summary>
/// Represents a result from a principal search (RFC 3744 principal-property-search).
/// </summary>
public record PrincipalSearchResult(
    string Href,
    string? DisplayName,
    string? Email,
    bool IsGroup)
{
    /// <summary>
    /// Gets whether this principal has an email address.
    /// </summary>
    public bool HasEmail => !string.IsNullOrEmpty(Email);

    /// <summary>
    /// Parses principal search results from a principal-property-search REPORT response.
    /// </summary>
    public static PrincipalSearchResult[] Parse(string xml)
    {
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var d = System.Xml.Linq.XNamespace.Get("DAV:");

        var results = new List<PrincipalSearchResult>();

        foreach (var response in doc.Descendants(d + "response"))
        {
            var href = response.Element(d + "href")?.Value;
            if (string.IsNullOrEmpty(href))
                continue;

            var propstat = response.Element(d + "propstat")?.Element(d + "prop");
            if (propstat == null)
                continue;

            var displayName = propstat.Element(d + "displayname")?.Value;
            var email = propstat.Element(d + "email-address")?.Value;

            // Detect if this is a group by checking if the URL contains "groups"
            // This is a common convention in CalDAV servers like SOGo, Radicale, Baikal
            var isGroup = href.Contains("/groups/", StringComparison.OrdinalIgnoreCase);

            results.Add(new PrincipalSearchResult(href, displayName, email, isGroup));
        }

        return [.. results];
    }
}
