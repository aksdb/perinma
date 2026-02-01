using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using perinma.Storage.Models;

namespace perinma.Services.CardDAV;

public class CardDavService : ICardDavService
{
    private const string DavNamespace = "DAV:";
    private const string CardDavNamespace = "urn:ietf:params:xml:ns:carddav";

    public async Task<ICardDavService.AddressBookSyncResult> GetAddressBooksAsync(
        CardDavCredentials credentials,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(credentials);

        // Step 1: Discover addressbook-home-set
        var addressBookHomeUrl = await DiscoverAddressBookHomeSetAsync(client, credentials.ServerUrl, cancellationToken);

        if (string.IsNullOrEmpty(addressBookHomeUrl))
        {
            // Fallback: assume server URL is the address book home
            addressBookHomeUrl = credentials.ServerUrl;
        }

        // Step 2: List all address books via PROPFIND
        var addressBooks = await ListAddressBooksAsync(client, addressBookHomeUrl, cancellationToken);

        return new ICardDavService.AddressBookSyncResult
        {
            AddressBooks = addressBooks,
            SyncToken = null
        };
    }

    public async Task<ICardDavService.ContactSyncResult> GetContactsAsync(
        CardDavCredentials credentials,
        string addressBookUrl,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(credentials);

        // Use sync-collection REPORT for incremental sync
        var syncResponse = await SyncCollectionAsync(client, addressBookUrl, syncToken, cancellationToken);

        var contacts = new List<CardDavContact>();

        foreach (var item in syncResponse.Items)
        {
            if (item.IsDeleted)
            {
                var uid = ExtractUidFromUrl(item.Href);
                if (!string.IsNullOrEmpty(uid))
                {
                    contacts.Add(new CardDavContact
                    {
                        Uid = uid,
                        Url = item.Href,
                        Deleted = true
                    });
                }
                continue;
            }

            if (string.IsNullOrEmpty(item.AddressData))
                continue;

            try
            {
                var contact = ParseVCard(item.Href, item.AddressData, item.ETag);
                if (contact != null)
                {
                    contacts.Add(contact);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing vCard for {item.Href}: {ex.Message}");
                continue;
            }
        }

        return new ICardDavService.ContactSyncResult
        {
            Contacts = contacts,
            SyncToken = syncResponse.SyncToken
        };
    }

    public async Task<bool> TestConnectionAsync(
        CardDavCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(credentials);

            var request = new HttpRequestMessage(HttpMethod.Options, credentials.ServerUrl);
            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return false;

            // Check if server supports CardDAV
            if (response.Headers.TryGetValues("DAV", out var davHeaders))
            {
                var davCapabilities = string.Join(" ", davHeaders);
                return davCapabilities.Contains("addressbook", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient CreateClient(CardDavCredentials credentials)
    {
        var client = new HttpClient();
        var authBytes = Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("perinma/1.0");
        return client;
    }

    private async Task<string?> DiscoverAddressBookHomeSetAsync(
        HttpClient client,
        string serverUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var xd = XNamespace.Get(DavNamespace);
            var xc = XNamespace.Get(CardDavNamespace);

            var propfindXml = new XElement(xd + "propfind",
                new XAttribute(XNamespace.Xmlns + "d", DavNamespace),
                new XAttribute(XNamespace.Xmlns + "c", CardDavNamespace),
                new XElement(xd + "prop",
                    new XElement(xc + "addressbook-home-set")
                )
            );

            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), serverUrl)
            {
                Content = new StringContent(propfindXml.ToString(), Encoding.UTF8, "application/xml")
            };
            request.Headers.Add("Depth", "0");

            var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseXml = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = XDocument.Parse(responseXml);

            var addressBookHomeSet = doc.Descendants(xc + "addressbook-home-set")
                .Elements(xd + "href")
                .FirstOrDefault()?.Value;

            if (!string.IsNullOrEmpty(addressBookHomeSet))
            {
                if (!Uri.IsWellFormedUriString(addressBookHomeSet, UriKind.Absolute))
                {
                    var baseUri = new Uri(serverUrl);
                    var absoluteUri = new Uri(baseUri, addressBookHomeSet);
                    return absoluteUri.ToString();
                }

                return addressBookHomeSet;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<CardDavAddressBook>> ListAddressBooksAsync(
        HttpClient client,
        string addressBookHomeUrl,
        CancellationToken cancellationToken)
    {
        var xd = XNamespace.Get(DavNamespace);
        var xc = XNamespace.Get(CardDavNamespace);

        var propfindXml = new XElement(xd + "propfind",
            new XAttribute(XNamespace.Xmlns + "d", DavNamespace),
            new XAttribute(XNamespace.Xmlns + "c", CardDavNamespace),
            new XElement(xd + "prop",
                new XElement(xd + "displayname"),
                new XElement(xd + "resourcetype"),
                new XElement(xd + "sync-token")
            )
        );

        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), addressBookHomeUrl)
        {
            Content = new StringContent(propfindXml.ToString(), Encoding.UTF8, "application/xml")
        };
        request.Headers.Add("Depth", "1");

        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseXml = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = XDocument.Parse(responseXml);

        var addressBooks = new List<CardDavAddressBook>();

        foreach (var responseElement in doc.Descendants(xd + "response"))
        {
            var href = responseElement.Element(xd + "href")?.Value;
            if (string.IsNullOrEmpty(href))
                continue;

            var propstat = responseElement.Element(xd + "propstat");
            if (propstat == null)
                continue;

            var prop = propstat.Element(xd + "prop");
            if (prop == null)
                continue;

            var status = propstat.Element(xd + "status")?.Value;
            var isSuccess = status?.Contains("200 OK") ?? false;

            if (!isSuccess)
                continue;

            var resourceType = prop.Element(xd + "resourcetype");
            var isAddressBook = resourceType?.Element(xc + "addressbook") != null;

            if (!isAddressBook)
                continue;

            var displayName = prop.Element(xd + "displayname")?.Value;

            // Make absolute URL if relative
            var absoluteUrl = href;
            if (!Uri.IsWellFormedUriString(absoluteUrl, UriKind.Absolute))
            {
                var baseUri = new Uri(addressBookHomeUrl);
                absoluteUrl = new Uri(baseUri, href).ToString();
            }

            addressBooks.Add(new CardDavAddressBook
            {
                Url = absoluteUrl,
                DisplayName = displayName ?? "Unnamed Address Book",
                Deleted = false
            });
        }

        return addressBooks;
    }

    private async Task<SyncCollectionResponse> SyncCollectionAsync(
        HttpClient client,
        string addressBookUrl,
        string? syncToken,
        CancellationToken cancellationToken)
    {
        var xd = XNamespace.Get(DavNamespace);
        var xc = XNamespace.Get(CardDavNamespace);

        var syncTokenElement = !string.IsNullOrEmpty(syncToken)
            ? new XElement(xd + "sync-token", syncToken)
            : new XElement(xd + "sync-token");

        var reportXml = new XElement(xd + "sync-collection",
            new XAttribute(XNamespace.Xmlns + "d", DavNamespace),
            new XAttribute(XNamespace.Xmlns + "c", CardDavNamespace),
            syncTokenElement,
            new XElement(xd + "sync-level", "1"),
            new XElement(xd + "prop",
                new XElement(xd + "getetag"),
                new XElement(xc + "address-data")
            )
        );

        var request = new HttpRequestMessage(new HttpMethod("REPORT"), addressBookUrl)
        {
            Content = new StringContent(reportXml.ToString(), Encoding.UTF8, "application/xml")
        };
        request.Headers.Add("Depth", "1");

        var response = await client.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode == 410)
        {
            throw new InvalidOperationException("Sync token is invalid or expired (410)");
        }

        response.EnsureSuccessStatusCode();

        var responseXml = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseSyncCollectionResponse(responseXml);
    }

    private SyncCollectionResponse ParseSyncCollectionResponse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var xd = XNamespace.Get(DavNamespace);
        var xc = XNamespace.Get(CardDavNamespace);

        var items = new List<SyncCollectionItem>();
        var newSyncToken = doc.Descendants(xd + "sync-token").FirstOrDefault()?.Value;

        foreach (var responseElement in doc.Descendants(xd + "response"))
        {
            var href = responseElement.Element(xd + "href")?.Value;
            if (string.IsNullOrEmpty(href))
                continue;

            var status = responseElement.Element(xd + "status")?.Value;
            var isDeleted = status?.Contains("404") ?? false;

            string? etag = null;
            string? addressData = null;

            if (!isDeleted)
            {
                var propstat = responseElement.Element(xd + "propstat");
                if (propstat != null)
                {
                    var prop = propstat.Element(xd + "prop");
                    if (prop != null)
                    {
                        etag = prop.Element(xd + "getetag")?.Value;
                        addressData = prop.Element(xc + "address-data")?.Value;
                    }
                }
            }

            items.Add(new SyncCollectionItem
            {
                Href = href,
                ETag = etag,
                AddressData = addressData,
                IsDeleted = isDeleted
            });
        }

        return new SyncCollectionResponse
        {
            Items = items,
            SyncToken = newSyncToken
        };
    }

    private CardDavContact? ParseVCard(string url, string vCardData, string? etag)
    {
        // Simple vCard parser - extract key fields
        // vCard format: BEGIN:VCARD ... END:VCARD with properties like FN:, N:, EMAIL:, TEL:, PHOTO:

        var uid = ExtractVCardProperty(vCardData, "UID");
        if (string.IsNullOrEmpty(uid))
        {
            uid = ExtractUidFromUrl(url);
        }

        if (string.IsNullOrEmpty(uid))
        {
            return null;
        }

        var displayName = ExtractVCardProperty(vCardData, "FN");
        var structuredName = ExtractVCardProperty(vCardData, "N");
        string? givenName = null;
        string? familyName = null;

        if (!string.IsNullOrEmpty(structuredName))
        {
            // N format: FamilyName;GivenName;MiddleName;Prefix;Suffix
            var nameParts = structuredName.Split(';');
            if (nameParts.Length >= 1)
                familyName = nameParts[0];
            if (nameParts.Length >= 2)
                givenName = nameParts[1];
        }

        var primaryEmail = ExtractVCardProperty(vCardData, "EMAIL");
        var primaryPhone = ExtractVCardProperty(vCardData, "TEL");
        var photoUrl = ExtractVCardProperty(vCardData, "PHOTO");

        return new CardDavContact
        {
            Uid = uid,
            Url = url,
            DisplayName = displayName,
            GivenName = givenName,
            FamilyName = familyName,
            PrimaryEmail = primaryEmail,
            PrimaryPhone = primaryPhone,
            PhotoUrl = photoUrl,
            ETag = etag,
            RawVCard = vCardData,
            Deleted = false
        };
    }

    private static string? ExtractVCardProperty(string vCardData, string propertyName)
    {
        // Match property with optional parameters: PROPERTY;param=value:value
        // or simple: PROPERTY:value
        var pattern = $@"^{Regex.Escape(propertyName)}(?:;[^:]*)?:(.*)$";
        var match = Regex.Match(vCardData, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);

        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return null;
    }

    private static string ExtractUidFromUrl(string url)
    {
        var uri = new Uri(url, UriKind.RelativeOrAbsolute);
        var segments = uri.IsAbsoluteUri ? uri.Segments : url.Split('/');
        var lastSegment = segments.LastOrDefault()?.TrimEnd('/');

        // Remove .vcf extension if present
        if (lastSegment?.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase) == true)
        {
            lastSegment = lastSegment[..^4];
        }

        return lastSegment ?? url;
    }

    private class SyncCollectionResponse
    {
        public required List<SyncCollectionItem> Items { get; init; }
        public string? SyncToken { get; init; }
    }

    private class SyncCollectionItem
    {
        public required string Href { get; init; }
        public string? ETag { get; init; }
        public string? AddressData { get; init; }
        public bool IsDeleted { get; init; }
    }
}
