using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Ical.Net;
using perinma.Storage.Models;

namespace perinma.Services;

public class CalDavClient
{
    private readonly HttpClient _httpClient;
    private const string DavNamespace = "DAV:";
    private const string CalDavNamespace = "urn:ietf:params:xml:ns:caldav";
    private const string CalendarServerNamespace = "http://calendarserver.org/ns/";
    // Apple iCal namespace - used by SOGo, Apple Calendar, and many other CalDAV servers for calendar-color
    private const string AppleIcalNamespace = "http://apple.com/ns/ical/";

    public CalDavClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public void SetBasicAuth(string username, string password)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<bool> TestConnectionAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Options, serverUrl);
            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return false;

            // Check if server supports CalDAV
            if (response.Headers.TryGetValues("DAV", out var davHeaders))
            {
                var davCapabilities = string.Join(" ", davHeaders);
                return davCapabilities.Contains("calendar-access", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<PropfindResponse> PropfindAsync(
        string url,
        int depth,
        IEnumerable<XElement> properties,
        CancellationToken cancellationToken = default)
    {
        var xd = XNamespace.Get(DavNamespace);
        var xc = XNamespace.Get(CalDavNamespace);
        var xcs = XNamespace.Get(CalendarServerNamespace);

        var propfindXml = new XElement(xd + "propfind",
            new XAttribute(XNamespace.Xmlns + "d", DavNamespace),
            new XAttribute(XNamespace.Xmlns + "c", CalDavNamespace),
            new XAttribute(XNamespace.Xmlns + "cs", CalendarServerNamespace),
            new XAttribute(XNamespace.Xmlns + "a", AppleIcalNamespace),
            new XElement(xd + "prop", properties)
        );

        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url)
        {
            Content = new StringContent(propfindXml.ToString(), Encoding.UTF8, "application/xml")
        };
        request.Headers.Add("Depth", depth.ToString());

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseXml = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParsePropfindResponse(responseXml);
    }

    public async Task<SyncCollectionResponse> SyncCollectionAsync(
        string calendarUrl,
        string? syncToken,
        CancellationToken cancellationToken = default)
    {
        var xd = XNamespace.Get(DavNamespace);
        var xc = XNamespace.Get(CalDavNamespace);

        var syncTokenElement = !string.IsNullOrEmpty(syncToken)
            ? new XElement(xd + "sync-token", syncToken)
            : new XElement(xd + "sync-token");

        var reportXml = new XElement(xd + "sync-collection",
            new XAttribute(XNamespace.Xmlns + "d", DavNamespace),
            new XAttribute(XNamespace.Xmlns + "c", CalDavNamespace),
            syncTokenElement,
            new XElement(xd + "sync-level", "1"),
            new XElement(xd + "prop",
                new XElement(xd + "getetag"),
                new XElement(xc + "calendar-data")
            )
        );

        var request = new HttpRequestMessage(new HttpMethod("REPORT"), calendarUrl)
        {
            Content = new StringContent(reportXml.ToString(), Encoding.UTF8, "application/xml")
        };
        request.Headers.Add("Depth", "1");

        var response = await _httpClient.SendAsync(request, cancellationToken);

        // Handle 410 Gone (invalid sync token)
        if ((int)response.StatusCode == 410)
        {
            throw new InvalidOperationException("Sync token is invalid or expired (410)");
        }

        response.EnsureSuccessStatusCode();

        var responseXml = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseSyncCollectionResponse(responseXml);
    }

    public async Task<string> GetCalendarObjectAsync(string objectUrl, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(objectUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public Calendar ParseICalendar(string icalData)
    {
        return Calendar.Load(icalData);
    }

    private PropfindResponse ParsePropfindResponse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var xd = XNamespace.Get(DavNamespace);
        var xc = XNamespace.Get(CalDavNamespace);
        var xcs = XNamespace.Get(CalendarServerNamespace);
        var xa = XNamespace.Get(AppleIcalNamespace);

        var responses = new List<PropfindItem>();

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

            var displayName = prop.Element(xd + "displayname")?.Value;
            var resourceType = prop.Element(xd + "resourcetype");
            var isCalendar = resourceType?.Element(xc + "calendar") != null;
            // Check both CalendarServer and Apple iCal namespaces for calendar-color
            // SOGo and many other servers use the Apple iCal namespace
            var color = prop.Element(xa + "calendar-color")?.Value
                        ?? prop.Element(xcs + "calendar-color")?.Value;
            var ctag = prop.Element(xcs + "getctag")?.Value;
            var syncToken = prop.Element(xd + "sync-token")?.Value;
            var calendarHomeSet = prop.Element(xc + "calendar-home-set")?.Element(xd + "href")?.Value;

            responses.Add(new PropfindItem
            {
                Href = href,
                DisplayName = displayName,
                IsCalendar = isCalendar,
                Color = color,
                CTag = ctag,
                SyncToken = syncToken,
                CalendarHomeSet = calendarHomeSet
            });
        }

        return new PropfindResponse { Items = responses };
    }

    private SyncCollectionResponse ParseSyncCollectionResponse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var xd = XNamespace.Get(DavNamespace);
        var xc = XNamespace.Get(CalDavNamespace);

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
            string? calendarData = null;

            if (!isDeleted)
            {
                var propstat = responseElement.Element(xd + "propstat");
                if (propstat != null)
                {
                    var prop = propstat.Element(xd + "prop");
                    if (prop != null)
                    {
                        etag = prop.Element(xd + "getetag")?.Value;
                        calendarData = prop.Element(xc + "calendar-data")?.Value;
                    }
                }
            }

            items.Add(new SyncCollectionItem
            {
                Href = href,
                ETag = etag,
                CalendarData = calendarData,
                IsDeleted = isDeleted
            });
        }

        return new SyncCollectionResponse
        {
            Items = items,
            SyncToken = newSyncToken
        };
    }
}

public class PropfindResponse
{
    public required List<PropfindItem> Items { get; init; }
}

public class PropfindItem
{
    public required string Href { get; init; }
    public string? DisplayName { get; init; }
    public bool IsCalendar { get; init; }
    public string? Color { get; init; }
    public string? CTag { get; init; }
    public string? SyncToken { get; init; }
    public string? CalendarHomeSet { get; init; }
}

public class SyncCollectionResponse
{
    public required List<SyncCollectionItem> Items { get; init; }
    public string? SyncToken { get; init; }
}

public class SyncCollectionItem
{
    public required string Href { get; init; }
    public string? ETag { get; init; }
    public string? CalendarData { get; init; }
    public bool IsDeleted { get; init; }
}
