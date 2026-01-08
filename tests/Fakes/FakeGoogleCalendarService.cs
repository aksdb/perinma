using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using perinma.Services;
using perinma.Storage.Models;

namespace perinma.Tests.Fakes;

public class FakeGoogleCalendarService : IGoogleCalendarService
{
    private readonly List<CalendarListEntry> _calendars = new();
    private string? _syncToken;
    private bool _shouldThrowInvalidSyncToken;
    
    public void SetCalendars(params CalendarListEntry[] calendars)
    {
        _calendars.Clear();
        _calendars.AddRange(calendars);
    }
    
    public void SetInvalidSyncTokenBehavior(bool shouldThrow)
    {
        _shouldThrowInvalidSyncToken = shouldThrow;
    }

    public Task<CalendarService> CreateServiceAsync(GoogleCredentials credentials, CancellationToken cancellationToken = default)
    {
        // Return a null CalendarService - we won't actually use it in tests
        // In a real scenario, you might want to create a proper mock CalendarService
        return Task.FromResult<CalendarService>(null!);
    }

    public Task<GoogleCalendarService.CalendarSyncResult> GetCalendarsAsync(
        CalendarService service,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        // Simulate invalid sync token error
        if (_shouldThrowInvalidSyncToken && !string.IsNullOrEmpty(syncToken))
        {
            throw new InvalidOperationException("Sync token is invalid or expired (410)");
        }

        // Generate a new sync token for next request
        _syncToken = Guid.NewGuid().ToString();

        var result = new GoogleCalendarService.CalendarSyncResult
        {
            Calendars = _calendars,
            SyncToken = _syncToken
        };

        return Task.FromResult(result);
    }

    public Task ExchangeAuthorizationCodeAsync(
        GoogleCredentials credentials,
        CancellationToken cancellationToken,
        string? redirectUri = null)
    {
        // Simulate successful token exchange
        credentials.AccessToken = "fake_access_token";
        credentials.RefreshToken = "fake_refresh_token";
        credentials.ExpiresAt = DateTime.UtcNow.AddHours(1);
        credentials.TokenType = "Bearer";
        credentials.AuthorizationCode = null;

        return Task.CompletedTask;
    }
    
    public static CalendarListEntry CreateCalendar(string id, string summary, bool selected = true, string? color = null)
    {
        return new CalendarListEntry
        {
            Id = id,
            Summary = summary,
            Selected = selected,
            BackgroundColor = color ?? "#9fc6e7",
            Deleted = false
        };
    }

    public static CalendarListEntry CreateDeletedCalendar(string id)
    {
        return new CalendarListEntry
        {
            Id = id,
            Summary = "Deleted Calendar",
            Deleted = true
        };
    }
}
