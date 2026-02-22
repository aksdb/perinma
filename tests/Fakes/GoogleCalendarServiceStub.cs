using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CredentialStore;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage.Models;

namespace tests.Fakes;

/// <summary>
/// Simple stub for IGoogleCalendarService that returns predefined raw data.
/// Used for testing real providers without making actual API calls.
/// </summary>
public class GoogleCalendarServiceStub : IGoogleCalendarService
{
    private readonly List<string> _rawCalendarData = new();
    private readonly Dictionary<string, List<string>> _rawEventDataByCalendar = new();
    private string? _syncToken;
    private readonly Dictionary<string, string?> _eventSyncTokens = new();
    private bool _invalidSyncTokenBehavior = false;
    private bool _firstCallMade = false;

    /// <summary>
    /// Sets the raw JSON calendar data to return.
    /// </summary>
    public void SetRawCalendars(params string[] rawCalendarJsonData)
    {
        _rawCalendarData.Clear();
        _rawCalendarData.AddRange(rawCalendarJsonData);
    }

    /// <summary>
    /// Sets the raw JSON event data to return for a specific calendar.
    /// </summary>
    public void SetRawEvents(string calendarId, params string[] rawEventJsonData)
    {
        if (!_rawEventDataByCalendar.ContainsKey(calendarId))
        {
            _rawEventDataByCalendar[calendarId] = new List<string>();
        }
        _rawEventDataByCalendar[calendarId].Clear();
        _rawEventDataByCalendar[calendarId].AddRange(rawEventJsonData);
    }

    public void SetSyncToken(string syncToken)
    {
        _syncToken = syncToken;
    }

    /// <summary>
    /// Sets whether to throw an exception on the first GetCalendarsAsync call to simulate invalid sync token.
    /// </summary>
    public void SetInvalidSyncTokenBehavior(bool enabled)
    {
        _invalidSyncTokenBehavior = enabled;
        _firstCallMade = false;
    }

    public Task<CalendarService> CreateServiceAsync(GoogleCredentials credentials, CancellationToken cancellationToken = default, string? accountId = null)
    {
        // Return null - we don't use the actual service in tests
        return Task.FromResult<CalendarService>(null!);
    }

    public Task<GoogleCalendarService.CalendarSyncResult> GetCalendarsAsync(
        CalendarService service,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        // Simulate invalid sync token on first call if behavior is enabled
        if (_invalidSyncTokenBehavior && !_firstCallMade && !string.IsNullOrEmpty(syncToken))
        {
            _firstCallMade = true;
            throw new Exception("Sync token is invalid or expired (410)");
        }

        // Deserialize raw JSON to CalendarListEntry objects
        var calendars = new List<CalendarListEntry>();
        foreach (var rawJson in _rawCalendarData)
        {
            try
            {
                var cal = NewtonsoftJsonSerializer.Instance.Deserialize<CalendarListEntry>(rawJson);
                if (cal != null)
                {
                    calendars.Add(cal);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing calendar JSON: {ex.Message}");
            }
        }

        var result = new GoogleCalendarService.CalendarSyncResult
        {
            Calendars = calendars,
            SyncToken = _syncToken ?? Guid.NewGuid().ToString()
        };

        return Task.FromResult(result);
    }

    public Task<GoogleCalendarService.EventSyncResult> GetEventsAsync(
        CalendarService service,
        string calendarId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        var rawEvents = _rawEventDataByCalendar.ContainsKey(calendarId)
            ? _rawEventDataByCalendar[calendarId]
            : new List<string>();

        var events = new List<Event>();
        foreach (var rawJson in rawEvents)
        {
            try
            {
                var evt = NewtonsoftJsonSerializer.Instance.Deserialize<Event>(rawJson);
                if (evt != null)
                {
                    events.Add(evt);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing event JSON: {ex.Message}");
            }
        }

        var newSyncToken = Guid.NewGuid().ToString();
        _eventSyncTokens[calendarId] = newSyncToken;

        var result = new GoogleCalendarService.EventSyncResult
        {
            Events = events,
            SyncToken = newSyncToken
        };

        return Task.FromResult(result);
    }

    public Task UpdateCalendarSelectedAsync(
        CalendarService service,
        string calendarId,
        bool selected,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ExchangeAuthorizationCodeAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken,
        string? redirectUri = null)
    {
        credentials.AccessToken = "stub_access_token";
        credentials.RefreshToken = "stub_refresh_token";
        credentials.ExpiresAt = DateTime.UtcNow.AddHours(1);
        credentials.TokenType = "Bearer";
        credentials.AuthorizationCode = null;

        return Task.CompletedTask;
    }

    public Task RespondToEventAsync(
        CalendarService service,
        string calendarId,
        string eventId,
        string responseStatus,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<string> CreateEventAsync(
        CalendarService service,
        string calendarId,
        Event @event,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult("stub_event_id");
    }

    public Task UpdateEventAsync(
        CalendarService service,
        string calendarId,
        string eventId,
        Event @event,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task DeleteEventAsync(
        CalendarService service,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
