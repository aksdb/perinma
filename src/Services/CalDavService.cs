using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using perinma.Storage.Models;

namespace perinma.Services;

public class CalDavService : ICalDavService
{
    private const string DavNamespace = "DAV:";
    private const string CalDavNamespace = "urn:ietf:params:xml:ns:caldav";
    private const string CalendarServerNamespace = "http://calendarserver.org/ns/";

    public async Task<ICalDavService.CalendarSyncResult> GetCalendarsAsync(
        CalDavCredentials credentials,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(credentials);

        // Step 1: Discover calendar-home-set if we don't have it
        var calendarHomeUrl = await DiscoverCalendarHomeSetAsync(client, credentials.ServerUrl, cancellationToken);

        if (string.IsNullOrEmpty(calendarHomeUrl))
        {
            // Fallback: assume server URL is the calendar home
            calendarHomeUrl = credentials.ServerUrl;
        }

        // Step 2: If syncToken is provided, use sync-collection
        if (!string.IsNullOrEmpty(syncToken))
        {
            try
            {
                var syncResponse = await client.SyncCollectionAsync(calendarHomeUrl, syncToken, cancellationToken);
                var syncedCalendars = await ParseCalendarsFromSyncResponseAsync(client, syncResponse, cancellationToken);

                return new ICalDavService.CalendarSyncResult
                {
                    Calendars = syncedCalendars,
                    SyncToken = syncResponse.SyncToken
                };
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("410"))
            {
                // Sync token expired, rethrow so caller can handle
                throw;
            }
        }

        // Step 3: Full sync - list all calendars via PROPFIND
        var calendars = await ListCalendarsAsync(client, calendarHomeUrl, cancellationToken);

        return new ICalDavService.CalendarSyncResult
        {
            Calendars = calendars,
            SyncToken = null // Full sync doesn't return sync token in this implementation
        };
    }

    public async Task<ICalDavService.EventSyncResult> GetEventsAsync(
        CalDavCredentials credentials,
        string calendarUrl,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(credentials);

        // Use sync-collection REPORT for incremental sync
        var syncResponse = await client.SyncCollectionAsync(calendarUrl, syncToken, cancellationToken);

        var events = new List<CalDavEvent>();

        foreach (var item in syncResponse.Items)
        {
            if (item.IsDeleted)
            {
                // Extract UID from URL for deleted events
                var uid = ExtractUidFromUrl(item.Href);
                if (!string.IsNullOrEmpty(uid))
                {
                    events.Add(new CalDavEvent
                    {
                        Uid = uid,
                        Url = item.Href,
                        Deleted = true
                    });
                }
                continue;
            }

            if (string.IsNullOrEmpty(item.CalendarData))
                continue;

            try
            {
                var calendar = client.ParseICalendar(item.CalendarData);

                foreach (var evt in calendar.Events)
                {
                    events.Add(new CalDavEvent
                    {
                        Uid = evt.Uid,
                        Url = item.Href,
                        Summary = evt.Summary,
                        StartTime = evt.Start?.AsUtc,
                        EndTime = evt.End?.AsUtc,
                        Status = evt.Status,
                        ETag = item.ETag,
                        RawICalendar = item.CalendarData,
                        Deleted = false
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing iCalendar for {item.Href}: {ex.Message}");
                // Skip malformed events
                continue;
            }
        }

        return new ICalDavService.EventSyncResult
        {
            Events = events,
            SyncToken = syncResponse.SyncToken
        };
    }

    public async Task<bool> TestConnectionAsync(
        CalDavCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(credentials);
        return await client.TestConnectionAsync(credentials.ServerUrl, cancellationToken);
    }

    private CalDavClient CreateClient(CalDavCredentials credentials)
    {
        var client = new CalDavClient();
        client.SetBasicAuth(credentials.Username, credentials.Password);
        return client;
    }

    private async Task<string?> DiscoverCalendarHomeSetAsync(
        CalDavClient client,
        string serverUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var xd = XNamespace.Get(DavNamespace);
            var xc = XNamespace.Get(CalDavNamespace);

            var properties = new[]
            {
                new XElement(xc + "calendar-home-set")
            };

            var response = await client.PropfindAsync(serverUrl, 0, properties, cancellationToken);

            var calendarHomeSet = response.Items.FirstOrDefault()?.CalendarHomeSet;

            if (!string.IsNullOrEmpty(calendarHomeSet))
            {
                // Make absolute URL if relative
                if (!Uri.IsWellFormedUriString(calendarHomeSet, UriKind.Absolute))
                {
                    var baseUri = new Uri(serverUrl);
                    var absoluteUri = new Uri(baseUri, calendarHomeSet);
                    return absoluteUri.ToString();
                }

                return calendarHomeSet;
            }

            return null;
        }
        catch
        {
            // If discovery fails, return null and use server URL as fallback
            return null;
        }
    }

    private async Task<List<CalDavCalendar>> ListCalendarsAsync(
        CalDavClient client,
        string calendarHomeUrl,
        CancellationToken cancellationToken)
    {
        var xd = XNamespace.Get(DavNamespace);
        var xc = XNamespace.Get(CalDavNamespace);
        var xcs = XNamespace.Get(CalendarServerNamespace);

        var properties = new[]
        {
            new XElement(xd + "displayname"),
            new XElement(xd + "resourcetype"),
            new XElement(xcs + "calendar-color"),
            new XElement(xcs + "getctag"),
            new XElement(xd + "sync-token")
        };

        var response = await client.PropfindAsync(calendarHomeUrl, 1, properties, cancellationToken);

        var calendars = new List<CalDavCalendar>();

        foreach (var item in response.Items)
        {
            // Skip non-calendar resources and the calendar home itself
            if (!item.IsCalendar || item.Href == calendarHomeUrl)
                continue;

            // Make absolute URL if relative
            var absoluteUrl = item.Href;
            if (!Uri.IsWellFormedUriString(absoluteUrl, UriKind.Absolute))
            {
                var baseUri = new Uri(calendarHomeUrl);
                absoluteUrl = new Uri(baseUri, item.Href).ToString();
            }

            calendars.Add(new CalDavCalendar
            {
                Url = absoluteUrl,
                DisplayName = item.DisplayName ?? "Unnamed Calendar",
                Color = item.Color,
                CTag = item.CTag,
                Deleted = false
            });
        }

        return calendars;
    }

    private async Task<List<CalDavCalendar>> ParseCalendarsFromSyncResponseAsync(
        CalDavClient client,
        SyncCollectionResponse syncResponse,
        CancellationToken cancellationToken)
    {
        var calendars = new List<CalDavCalendar>();

        foreach (var item in syncResponse.Items)
        {
            if (item.IsDeleted)
            {
                calendars.Add(new CalDavCalendar
                {
                    Url = item.Href,
                    DisplayName = string.Empty,
                    Deleted = true
                });
                continue;
            }

            // For updated/new calendars, we need to fetch their properties
            // This is a simplified implementation - in production you might want to batch these
            try
            {
                var xd = XNamespace.Get(DavNamespace);
                var xcs = XNamespace.Get(CalendarServerNamespace);

                var properties = new[]
                {
                    new XElement(xd + "displayname"),
                    new XElement(xcs + "calendar-color"),
                    new XElement(xcs + "getctag")
                };

                var propResponse = await client.PropfindAsync(item.Href, 0, properties, cancellationToken);
                var props = propResponse.Items.FirstOrDefault();

                if (props != null)
                {
                    calendars.Add(new CalDavCalendar
                    {
                        Url = item.Href,
                        DisplayName = props.DisplayName ?? "Unnamed Calendar",
                        Color = props.Color,
                        CTag = props.CTag,
                        Deleted = false
                    });
                }
            }
            catch
            {
                // Skip calendars we can't fetch properties for
                continue;
            }
        }

        return calendars;
    }

    private string ExtractUidFromUrl(string url)
    {
        // Extract the last part of the URL (typically the UID)
        // Example: /calendars/user/work/event-123.ics -> event-123.ics
        var uri = new Uri(url, UriKind.RelativeOrAbsolute);
        var segments = uri.IsAbsoluteUri ? uri.Segments : url.Split('/');
        var lastSegment = segments.LastOrDefault()?.TrimEnd('/');

        // Remove .ics extension if present
        if (lastSegment?.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) == true)
        {
            lastSegment = lastSegment[..^4];
        }

        return lastSegment ?? url;
    }
}
