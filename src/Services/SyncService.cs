using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Json;
using perinma.Storage;
using perinma.Storage.Models;
using perinma.Utils;

namespace perinma.Services;

public class SyncService
{
    private readonly SqliteStorage _storage;
    private readonly CredentialManagerService _credentialManager;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly ICalDavService _calDavService;

    public SyncService(
        SqliteStorage storage,
        CredentialManagerService credentialManager,
        IGoogleCalendarService googleCalendarService,
        ICalDavService calDavService)
    {
        _storage = storage;
        _credentialManager = credentialManager;
        _googleCalendarService = googleCalendarService;
        _calDavService = calDavService;
    }

    /// <summary>
    /// Forces a complete resync of an account by clearing all local data and sync tokens,
    /// then performing a full sync from the remote server.
    /// </summary>
    public async Task<SyncResult> ForceResyncAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var result = new SyncResult();

        try
        {
            var account = await _storage.GetAccountByIdAsync(accountId);
            if (account == null)
            {
                result.Success = false;
                result.Errors.Add($"Account with id {accountId} not found");
                return result;
            }

            Console.WriteLine($"Force resync requested for account: {account.Name}");

            // Clear all sync data (calendars, events, sync tokens)
            await _storage.ClearAccountSyncDataAsync(accountId);
            Console.WriteLine($"Cleared all sync data for account: {account.Name}");

            // Perform a fresh sync
            try
            {
                if (account.Type.Equals("Google", StringComparison.OrdinalIgnoreCase))
                {
                    await SyncGoogleAccountAsync(account, cancellationToken);
                }
                else if (account.Type.Equals("CalDAV", StringComparison.OrdinalIgnoreCase))
                {
                    await SyncCalDavAccountAsync(account, cancellationToken);
                }
                else
                {
                    result.Errors.Add($"Unknown account type: {account.Type}");
                    result.FailedAccounts++;
                    result.Success = false;
                    return result;
                }

                result.SyncedAccounts++;
                result.Success = true;
                Console.WriteLine($"Force resync completed for account: {account.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during force resync for account {account.Name}: {ex.Message}");
                result.FailedAccounts++;
                result.Errors.Add($"{account.Name}: {ex.Message}");
                result.Success = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during force resync: {ex.Message}");
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Syncs calendars from all accounts (Google and CalDAV)
    /// </summary>
    public async Task<SyncResult> SyncAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        var result = new SyncResult();

        try
        {
            // Get all accounts
            var accounts = (await _storage.GetAllAccountsAsync()).ToImmutableList();
            var googleAccounts = accounts.Where(a => a.Type.Equals("Google", StringComparison.OrdinalIgnoreCase)).ToList();
            var caldavAccounts = accounts.Where(a => a.Type.Equals("CalDAV", StringComparison.OrdinalIgnoreCase)).ToList();

            Console.WriteLine($"Found {googleAccounts.Count} Google accounts and {caldavAccounts.Count} CalDAV accounts to sync");

            // Sync Google accounts
            foreach (var account in googleAccounts)
            {
                try
                {
                    await SyncGoogleAccountAsync(account, cancellationToken);
                    result.SyncedAccounts++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error syncing Google account {account.Name}: {ex.Message}");
                    result.FailedAccounts++;
                    result.Errors.Add($"{account.Name}: {ex.Message}");
                }
            }

            // Sync CalDAV accounts
            foreach (var account in caldavAccounts)
            {
                try
                {
                    await SyncCalDavAccountAsync(account, cancellationToken);
                    result.SyncedAccounts++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error syncing CalDAV account {account.Name}: {ex.Message}");
                    result.FailedAccounts++;
                    result.Errors.Add($"{account.Name}: {ex.Message}");
                }
            }

            result.Success = result.FailedAccounts == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during sync: {ex.Message}");
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Syncs calendars from a single Google account using incremental sync when possible
    /// </summary>
    private async Task SyncGoogleAccountAsync(AccountDbo account, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing account: {account.Name}");

        // Get credentials from credential manager
        var credentials = _credentialManager.GetGoogleCredentials(account.AccountId);
        if (credentials == null)
        {
            throw new InvalidOperationException($"No credentials found for account {account.Name}");
        }

        // Load sync token from account data for incremental sync
        string? syncToken = await _storage.GetAccountData(account, "calendarSyncToken");
        bool isFullSync = string.IsNullOrEmpty(syncToken);

        // Create Google Calendar service
        var service = await _googleCalendarService.CreateServiceAsync(credentials, cancellationToken);

        // Update credentials in case tokens were refreshed
        _credentialManager.StoreGoogleCredentials(account.AccountId, credentials);

        // Fetch calendars with optional sync token for incremental sync
        GoogleCalendarService.CalendarSyncResult result;
        try
        {
            result = await _googleCalendarService.GetCalendarsAsync(service, syncToken, cancellationToken);
            Console.WriteLine($"Found {result.Calendars.Count} calendar {(isFullSync ? "items" : "changes")} for account {account.Name}");
        }
        catch (Exception ex) when (ex.Message.Contains("410") || ex.Message.Contains("invalid") || ex.Message.Contains("Sync token"))
        {
            // Sync token is invalid or expired, fall back to full sync
            Console.WriteLine($"Sync token invalid, performing full sync: {ex.Message}");
            isFullSync = true;
            syncToken = null;
            result = await _googleCalendarService.GetCalendarsAsync(service, null, cancellationToken);
            Console.WriteLine($"Found {result.Calendars.Count} calendars in full sync for account {account.Name}");
        }

        // Track the current sync timestamp for cleanup
        var currentSyncTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Save calendars to database
        foreach (var calendar in result.Calendars)
        {
            // Check if calendar was deleted (Google returns deleted calendars with deleted=true)
            if (calendar.Deleted == true)
            {
                Console.WriteLine($"Calendar {calendar.Summary} was deleted, will clean up");
                // In incremental sync, we get explicit delete notifications
                // We'll handle deletion in the cleanup phase for full sync
                continue;
            }

            var calendarDbo = new CalendarDbo
            {
                AccountId = account.AccountId,
                CalendarId = string.Empty, // Will be set by CreateOrUpdateCalendarAsync
                ExternalId = calendar.Id,
                Name = calendar.Summary ?? "Unnamed Calendar",
                Color = calendar.BackgroundColor,
                Enabled = calendar.Selected == true ? 1 : 0,
                LastSync = currentSyncTime,
            };

            await _storage.CreateOrUpdateCalendarAsync(calendarDbo);
        }

        // If this was a full sync, clean up calendars that weren't updated
        // (they were deleted on the remote side)
        if (isFullSync)
        {
            var deletedCount = await _storage.DeleteCalendarsNotSyncedAsync(account.AccountId, currentSyncTime);
            if (deletedCount > 0)
            {
                Console.WriteLine($"Deleted {deletedCount} calendar(s) that were removed remotely");
            }
        }

        // Store the new sync token for next incremental sync
        if (!string.IsNullOrEmpty(result.SyncToken))
        {
            await _storage.SetAccountData(account, "calendarSyncToken", result.SyncToken);
            Console.WriteLine($"Stored new sync token for next sync");
        }

        Console.WriteLine($"Synced {result.Calendars.Count} calendars for account {account.Name}");

        // Sync events for each enabled calendar
        var calendars = await _storage.GetCalendarsByAccountAsync(account.AccountId);
        foreach (var calendar in calendars.Where(c => c.Enabled == 1))
        {
            try
            {
                await SyncGoogleCalendarEventsAsync(service, calendar, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error syncing events for calendar {calendar.Name}: {ex.Message}");
                // Continue with other calendars
            }
        }
    }

    /// <summary>
    /// Syncs events for a single google calendar using incremental sync when possible
    /// </summary>
    private async Task SyncGoogleCalendarEventsAsync(Google.Apis.Calendar.v3.CalendarService service, CalendarDbo calendar, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing events for calendar: {calendar.Name}");

        // Load sync token from calendar data for incremental sync
        string? syncToken = await _storage.GetCalendarData(calendar, "eventSyncToken");
        bool isFullSync = string.IsNullOrEmpty(syncToken);

        // Fetch events with optional sync token for incremental sync
        GoogleCalendarService.EventSyncResult result;
        try
        {
            result = await _googleCalendarService.GetEventsAsync(service, calendar.ExternalId ?? string.Empty, syncToken, cancellationToken);
            Console.WriteLine($"Found {result.Events.Count} event {(isFullSync ? "items" : "changes")} for calendar {calendar.Name}");
        }
        catch (Exception ex) when (ex.Message.Contains("410") || ex.Message.Contains("invalid") || ex.Message.Contains("Sync token"))
        {
            // Sync token is invalid or expired, fall back to full sync
            Console.WriteLine($"Event sync token invalid, performing full sync: {ex.Message}");
            isFullSync = true;
            syncToken = null;
            result = await _googleCalendarService.GetEventsAsync(service, calendar.ExternalId ?? string.Empty, null, cancellationToken);
            Console.WriteLine($"Found {result.Events.Count} events in full sync for calendar {calendar.Name}");
        }

        // Track the current sync timestamp for cleanup
        var currentSyncTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Save events to database
        foreach (var evt in result.Events)
        {
            // Check if event was deleted or cancelled
            if (evt.Status == "cancelled")
            {
                Console.WriteLine($"Event {evt.Summary} was cancelled, will clean up");
                // In incremental sync, we get explicit delete notifications
                // We'll handle deletion in the cleanup phase for full sync
                continue;
            }

            // Skip events without start/end times (e.g., all-day events without proper dates)
            if (evt.Start == null || evt.End == null)
            {
                continue;
            }

            // Parse start and end times
            long? startTime = null;
            long? endTime = null;
            DateTime? startDateTime = null;
            DateTime? endDateTime = null;

            if (evt.Start.DateTimeRaw != null && DateTime.TryParse(evt.Start.DateTimeRaw, out var parsedStartDateTime))
            {
                startDateTime = parsedStartDateTime;
                startTime = new DateTimeOffset(parsedStartDateTime).ToUnixTimeSeconds();
            }
            else if (evt.Start.Date != null && DateTime.TryParse(evt.Start.Date, out var startDate))
            {
                startDateTime = startDate;
                startTime = new DateTimeOffset(startDate).ToUnixTimeSeconds();
            }

            if (evt.End.DateTimeRaw != null && DateTime.TryParse(evt.End.DateTimeRaw, out var parsedEndDateTime))
            {
                endDateTime = parsedEndDateTime;
                endTime = new DateTimeOffset(parsedEndDateTime).ToUnixTimeSeconds();
            }
            else if (evt.End.Date != null && DateTime.TryParse(evt.End.Date, out var endDate))
            {
                endDateTime = endDate;
                endTime = new DateTimeOffset(endDate).ToUnixTimeSeconds();
            }

            // For recurring events, calculate the recurrence end time and store it in EndTime
            if (evt.Recurrence is { Count: > 0 } && startDateTime.HasValue && endDateTime.HasValue)
            {
                var recurrenceEndTime = RecurrenceParser.GetRecurrenceEndTime(
                    evt.Recurrence,
                    startDateTime.Value,
                    endDateTime.Value);

                if (recurrenceEndTime.HasValue)
                {
                    endTime = new DateTimeOffset(recurrenceEndTime.Value).ToUnixTimeSeconds();
                }
            }

            var eventDbo = new CalendarEventDbo
            {
                CalendarId = calendar.CalendarId,
                EventId = string.Empty, // Will be set by CreateOrUpdateEventAsync
                ExternalId = evt.Id,
                StartTime = startTime,
                EndTime = endTime,
                Title = evt.Summary ?? "Untitled Event",
                ChangedAt = currentSyncTime,
            };

            var eventId = await _storage.CreateOrUpdateEventAsync(eventDbo);

            // Store raw Google event data for later use
            var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(evt);
            await _storage.SetEventDataJson(eventId, "rawData", rawEventJson);
        }

        // If this was a full sync, clean up events that weren't updated
        // (they were deleted on the remote side)
        if (isFullSync)
        {
            var deletedCount = await _storage.DeleteEventsNotSyncedAsync(calendar.CalendarId, currentSyncTime);
            if (deletedCount > 0)
            {
                Console.WriteLine($"Deleted {deletedCount} event(s) that were removed remotely");
            }
        }

        // Store the new sync token for next incremental sync
        if (!string.IsNullOrEmpty(result.SyncToken))
        {
            await _storage.SetCalendarData(calendar, "eventSyncToken", result.SyncToken);
            Console.WriteLine($"Stored new event sync token for next sync");
        }

        Console.WriteLine($"Synced {result.Events.Count} events for calendar {calendar.Name}");
    }

    /// <summary>
    /// Syncs calendars from a single CalDAV account using incremental sync when possible
    /// </summary>
    private async Task SyncCalDavAccountAsync(AccountDbo account, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing CalDAV account: {account.Name}");

        // Get credentials from credential manager
        var credentials = _credentialManager.GetCalDavCredentials(account.AccountId);
        if (credentials == null)
        {
            throw new InvalidOperationException($"No credentials found for account {account.Name}");
        }

        // Load sync token from account data for incremental sync
        string? syncToken = await _storage.GetAccountData(account, "calendarSyncToken");
        bool isFullSync = string.IsNullOrEmpty(syncToken);

        // Fetch calendars with optional sync token for incremental sync
        ICalDavService.CalendarSyncResult result;
        try
        {
            result = await _calDavService.GetCalendarsAsync(credentials, syncToken, cancellationToken);
            Console.WriteLine($"Found {result.Calendars.Count} calendar {(isFullSync ? "items" : "changes")} for account {account.Name}");
        }
        catch (Exception ex) when (ex.Message.Contains("410") || ex.Message.Contains("invalid"))
        {
            // Sync token is invalid or expired, fall back to full sync
            Console.WriteLine($"Sync token invalid, performing full sync: {ex.Message}");
            isFullSync = true;
            syncToken = null;
            result = await _calDavService.GetCalendarsAsync(credentials, null, cancellationToken);
            Console.WriteLine($"Found {result.Calendars.Count} calendars in full sync for account {account.Name}");
        }

        // Track the current sync timestamp for cleanup
        var currentSyncTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Save calendars to database
        foreach (var calendar in result.Calendars)
        {
            // Check if calendar was deleted
            if (calendar.Deleted)
            {
                Console.WriteLine($"Calendar {calendar.DisplayName} was deleted, will clean up");
                continue;
            }

            var calendarDbo = new CalendarDbo
            {
                AccountId = account.AccountId,
                CalendarId = string.Empty, // Will be set by CreateOrUpdateCalendarAsync
                ExternalId = calendar.Url,
                Name = calendar.DisplayName,
                Color = calendar.Color,
                Enabled = 1, // Default enabled for CalDAV calendars
                LastSync = currentSyncTime,
            };

            await _storage.CreateOrUpdateCalendarAsync(calendarDbo);
        }

        // If this was a full sync, clean up calendars that weren't updated
        if (isFullSync)
        {
            var deletedCount = await _storage.DeleteCalendarsNotSyncedAsync(account.AccountId, currentSyncTime);
            if (deletedCount > 0)
            {
                Console.WriteLine($"Deleted {deletedCount} calendar(s) that were removed remotely");
            }
        }

        // Store the new sync token for next incremental sync
        if (!string.IsNullOrEmpty(result.SyncToken))
        {
            await _storage.SetAccountData(account, "calendarSyncToken", result.SyncToken);
            Console.WriteLine($"Stored new sync token for next sync");
        }

        Console.WriteLine($"Synced {result.Calendars.Count} calendars for account {account.Name}");

        // Sync events for each enabled calendar
        var calendars = await _storage.GetCalendarsByAccountAsync(account.AccountId);
        foreach (var calendar in calendars.Where(c => c.Enabled == 1))
        {
            try
            {
                await SyncCalDavEventsAsync(credentials, calendar, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error syncing events for calendar {calendar.Name}: {ex.Message}");
                // Continue with other calendars
            }
        }
    }

    /// <summary>
    /// Syncs events for a single CalDAV calendar using incremental sync when possible
    /// </summary>
    private async Task SyncCalDavEventsAsync(CalDavCredentials credentials, CalendarDbo calendar, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing events for CalDAV calendar: {calendar.Name}");

        // Load sync token from calendar data for incremental sync
        string? syncToken = await _storage.GetCalendarData(calendar, "eventSyncToken");
        bool isFullSync = string.IsNullOrEmpty(syncToken);

        // Fetch events with optional sync token for incremental sync
        ICalDavService.EventSyncResult result;
        try
        {
            result = await _calDavService.GetEventsAsync(credentials, calendar.ExternalId ?? string.Empty, syncToken, cancellationToken);
            Console.WriteLine($"Found {result.Events.Count} event {(isFullSync ? "items" : "changes")} for calendar {calendar.Name}");
        }
        catch (Exception ex) when (ex.Message.Contains("410") || ex.Message.Contains("invalid"))
        {
            // Sync token is invalid or expired, fall back to full sync
            Console.WriteLine($"Event sync token invalid, performing full sync: {ex.Message}");
            isFullSync = true;
            syncToken = null;
            result = await _calDavService.GetEventsAsync(credentials, calendar.ExternalId ?? string.Empty, null, cancellationToken);
            Console.WriteLine($"Found {result.Events.Count} events in full sync for calendar {calendar.Name}");
        }

        // Track the current sync timestamp for cleanup
        var currentSyncTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Save events to database
        foreach (var evt in result.Events)
        {
            // Check if event was deleted or cancelled
            if (evt.Status == "CANCELLED" || evt.Deleted)
            {
                Console.WriteLine($"Event {evt.Summary} was cancelled/deleted, will clean up");
                continue;
            }

            // Calculate end time, considering recurrence rules if present
            long? endTime = evt.EndTime.HasValue ? new DateTimeOffset(evt.EndTime.Value).ToUnixTimeSeconds() : null;

            // Parse recurrence end time from raw iCalendar data
            if (!string.IsNullOrEmpty(evt.RawICalendar))
            {
                var recurrenceEndTime = ParseCalDavRecurrenceEndTime(evt.RawICalendar);
                if (recurrenceEndTime.HasValue)
                {
                    endTime = new DateTimeOffset(recurrenceEndTime.Value).ToUnixTimeSeconds();
                }
            }

            var eventDbo = new CalendarEventDbo
            {
                CalendarId = calendar.CalendarId,
                EventId = string.Empty, // Will be set by CreateOrUpdateEventAsync
                ExternalId = evt.Uid,
                StartTime = evt.StartTime.HasValue ? new DateTimeOffset(evt.StartTime.Value).ToUnixTimeSeconds() : null,
                EndTime = endTime,
                Title = evt.Summary ?? "Untitled Event",
                ChangedAt = currentSyncTime,
            };

            var eventId = await _storage.CreateOrUpdateEventAsync(eventDbo);

            // Store raw iCalendar data for later use
            if (!string.IsNullOrEmpty(evt.RawICalendar))
            {
                await _storage.SetEventData(eventId, "rawData", evt.RawICalendar);
            }
        }

        // If this was a full sync, clean up events that weren't updated
        if (isFullSync)
        {
            var deletedCount = await _storage.DeleteEventsNotSyncedAsync(calendar.CalendarId, currentSyncTime);
            if (deletedCount > 0)
            {
                Console.WriteLine($"Deleted {deletedCount} event(s) that were removed remotely");
            }
        }

        // Store the new sync token for next incremental sync
        if (!string.IsNullOrEmpty(result.SyncToken))
        {
            await _storage.SetCalendarData(calendar, "eventSyncToken", result.SyncToken);
            Console.WriteLine($"Stored new event sync token for next sync");
        }

        Console.WriteLine($"Synced {result.Events.Count} events for calendar {calendar.Name}");
    }

    /// <summary>
    /// Parses recurrence end time from raw iCalendar data.
    /// </summary>
    private static DateTime? ParseCalDavRecurrenceEndTime(string rawICalendar)
    {
        try
        {
            var calendar = Ical.Net.Calendar.Load(rawICalendar);
            var calendarEvent = calendar?.Events.FirstOrDefault();

            if (calendarEvent != null)
            {
                return RecurrenceParser.CalculateRecurrenceEndTime(calendarEvent);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing CalDAV recurrence: {ex.Message}");
        }

        return null;
    }
}

public class SyncResult
{
    public bool Success { get; set; }
    public int SyncedAccounts { get; set; }
    public int FailedAccounts { get; set; }
    public int TotalCalendars { get; set; }
    public System.Collections.Generic.List<string> Errors { get; set; } = new();
}
