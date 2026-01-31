using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using perinma.Storage.Models;

namespace perinma.Services.CalDAV;

public class CalDavService : ICalDavService
{
    private const string DavNamespace = "DAV:";
    private const string CalDavNamespace = "urn:ietf:params:xml:ns:caldav";
    private const string CalendarServerNamespace = "http://calendarserver.org/ns/";
    // Apple iCal namespace - used by SOGo, Apple Calendar, and many other CalDAV servers for calendar-color
    private const string AppleIcalNamespace = "http://apple.com/ns/ical/";

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

        // Step 4: Try to discover shared calendars from common paths
        var sharedCalendars = await DiscoverSharedCalendarsAsync(client, calendarHomeUrl, credentials, cancellationToken);
        calendars.AddRange(sharedCalendars);

        // Step 5: Fetch ACLs for calendars (gracefully, as some servers don't support it)
        await FetchAclsForCalendarsAsync(client, calendars, cancellationToken);

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
                if (calendar == null)
                    continue;

                foreach (var evt in calendar.Events)
                {
                    if (evt.Uid == null)
                    {
                        Console.WriteLine("Skipping CalDAV event without UID");
                        continue;
                    }
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

    public async Task<string> RespondToEventAsync(
        CalDavCredentials credentials,
        string eventUrl,
        string rawICalendar,
        string responseStatus,
        string userEmail,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(credentials);

        // Parse the iCalendar data
        var calendar = Ical.Net.Calendar.Load(rawICalendar);
        var iCalEvent = calendar?.Events.FirstOrDefault();

        if (iCalEvent == null)
        {
            throw new InvalidOperationException("Could not parse event from iCalendar data");
        }

        if (iCalEvent.Attendees == null || iCalEvent.Attendees.Count == 0)
        {
            throw new InvalidOperationException("Event has no attendees");
        }

        // Find the user's attendee entry by email
        var userAttendee = iCalEvent.Attendees.FirstOrDefault(a =>
        {
            var email = a.Value?.ToString();
            if (string.IsNullOrEmpty(email))
                return false;

            // Remove mailto: prefix if present
            if (email.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                email = email[7..];

            return email.Equals(userEmail, StringComparison.OrdinalIgnoreCase);
        });

        if (userAttendee == null)
        {
            throw new InvalidOperationException($"User {userEmail} is not an attendee of this event");
        }

        // Update the participation status
        userAttendee.ParticipationStatus = responseStatus;

        // Serialize the updated calendar
        var serializer = new Ical.Net.Serialization.CalendarSerializer();
        var updatedICalendar = serializer.SerializeToString(calendar) 
            ?? throw new InvalidOperationException("Failed to serialize updated calendar");

        // PUT the updated event back to the server
        await client.PutCalendarObjectAsync(eventUrl, updatedICalendar, null, cancellationToken);

        return updatedICalendar;
    }

    private CalDavClient CreateClient(CalDavCredentials credentials)
    {
        var client = new CalDavClient();
        client.SetBasicAuth(credentials.Username, credentials.Password);
        // Set User-Agent to iCal/Calendar.app to get SOGo to return shared calendars
        // SOGo has special handling for iCal that includes subscribed calendars in responses
        client.SetUserAgent("iCal/8.0 (1936) Mac OS X/10.14");
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
        var xa = XNamespace.Get(AppleIcalNamespace);

        var properties = new[]
        {
            new XElement(xd + "displayname"),
            new XElement(xd + "resourcetype"),
            // Request calendar-color from both namespaces for maximum compatibility
            // SOGo and Apple Calendar use the Apple iCal namespace
            new XElement(xa + "calendar-color"),
            new XElement(xcs + "calendar-color"),
            new XElement(xcs + "getctag"),
            new XElement(xd + "sync-token"),
            new XElement(xd + "owner")
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
                Deleted = false,
                Owner = item.Owner,
                AclXml = item.AclXml,
                CurrentUserPrivilegeSetXml = item.CurrentUserPrivilegeSetXml
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
                var xa = XNamespace.Get(AppleIcalNamespace);

                var properties = new[]
                {
                    new XElement(xd + "displayname"),
                    // Request calendar-color from both namespaces for maximum compatibility
                    new XElement(xa + "calendar-color"),
                    new XElement(xcs + "calendar-color"),
                    new XElement(xcs + "getctag"),
                    new XElement(xd + "owner")
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
                        Deleted = false,
                        Owner = props.Owner,
                        AclXml = props.AclXml,
                        CurrentUserPrivilegeSetXml = props.CurrentUserPrivilegeSetXml
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

    private async Task<List<CalDavCalendar>> DiscoverSharedCalendarsAsync(
        CalDavClient client,
        string calendarHomeUrl,
        CalDavCredentials credentials,
        CancellationToken cancellationToken)
    {
        var sharedCalendars = new List<CalDavCalendar>();

        // Common paths to try for shared calendars
        var sharedPaths = new List<string>
        {
            // SOGo: Shared calendars might be in different user paths
            // We can't guess these without API access, but iCal User-Agent helps
            // Generic shared folder
            $"{TrimTrailingSlash(calendarHomeUrl)}/shared/",
            // Generic others folder
            $"{TrimTrailingSlash(calendarHomeUrl)}/others/",
            // For servers that use "public" for shared
            $"{TrimTrailingSlash(calendarHomeUrl)}/public/",
        };

        var xd = XNamespace.Get(DavNamespace);

        foreach (var path in sharedPaths)
        {
            try
            {
                // Try a shallow PROPFIND to see if the path exists
                var properties = new[]
                {
                    new XElement(xd + "resourcetype")
                };

                var response = await client.PropfindAsync(path, 0, properties, cancellationToken);

                if (response.Items.Count > 0)
                {
                    var item = response.Items[0];
                    var isCollection = item.Href != null; // If we got a response, path exists

                    if (isCollection)
                    {
                        // Path exists, try to list calendars in it
                        var calendarsInPath = await ListCalendarsAsync(client, path, cancellationToken);
                        sharedCalendars.AddRange(calendarsInPath);
                    }
                }
            }
            catch
            {
                // Path doesn't exist or isn't accessible, skip it
                continue;
            }
        }

        return sharedCalendars;
    }

    private static string TrimTrailingSlash(string url)
    {
        return url.EndsWith('/') ? url.TrimEnd('/') : url;
    }

    private async Task FetchAclsForCalendarsAsync(
        CalDavClient client,
        List<CalDavCalendar> calendars,
        CancellationToken cancellationToken)
    {
        var xd = XNamespace.Get(DavNamespace);

        var aclProperties = new[]
        {
            new XElement(xd + "acl"),
            new XElement(xd + "current-user-privilege-set")
        };

        foreach (var calendar in calendars)
        {
            try
            {
                // Fetch ACL properties for this calendar
                var response = await client.PropfindAsync(calendar.Url, 0, aclProperties, cancellationToken);

                var item = response.Items.FirstOrDefault();
                if (item != null)
                {
                    calendar.AclXml = item.AclXml;
                    calendar.CurrentUserPrivilegeSetXml = item.CurrentUserPrivilegeSetXml;
                }
            }
            catch (HttpRequestException ex)
            {
                // If server doesn't support ACL properties (e.g., SOGo returns 501),
                // log and continue without ACL data
                var statusCode = ex.StatusCode.HasValue ? ((int)ex.StatusCode).ToString() : "unknown";
                Console.WriteLine($"Failed to fetch ACL for calendar {calendar.DisplayName} ({calendar.Url}): {ex.Message} (Status: {statusCode})");
            }
            catch (Exception ex)
            {
                // Other errors (network, parsing, etc.)
                Console.WriteLine($"Error fetching ACL for calendar {calendar.DisplayName} ({calendar.Url}): {ex.Message}");
            }
        }
    }
}
