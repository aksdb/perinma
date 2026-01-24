using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using perinma.Storage;
using perinma.Storage.Models;
using perinma.Models;

namespace perinma.Services;

public class SyncService
{
    private readonly SqliteStorage _storage;
    private readonly CredentialManagerService _credentialManager;
    private readonly ReminderService _reminderService;
    private readonly IReadOnlyDictionary<string, ICalendarProvider> _providers;

    public delegate void SyncProgressHandler(string message, int current, int total);
    public delegate void CalendarSyncProgressHandler(string calendarName, int current, int total);
    public delegate void EventSyncProgressHandler(string calendarName, int eventCount);

    public SyncService(
        SqliteStorage storage,
        CredentialManagerService credentialManager,
        IReadOnlyDictionary<string, ICalendarProvider> providers,
        ReminderService reminderService)
    {
        _storage = storage;
        _credentialManager = credentialManager;
        _providers = providers;
        _reminderService = reminderService;
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
                await SyncAccountAsync(account, cancellationToken, null, null);
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
    /// Syncs calendars from all accounts.
    /// </summary>
    public async Task<SyncResult> SyncAllAccountsAsync(
        CancellationToken cancellationToken = default,
        SyncProgressHandler? onAccountSyncStart = null,
        CalendarSyncProgressHandler? onCalendarSyncStart = null,
        EventSyncProgressHandler? onEventSyncStart = null)
    {
        var result = new SyncResult();

        try
        {
            var accounts = (await _storage.GetAllAccountsAsync()).ToImmutableList();
            Console.WriteLine($"Found {accounts.Count} accounts to sync");

            for (int i = 0; i < accounts.Count; i++)
            {
                var account = accounts[i];
                try
                {
                    onAccountSyncStart?.Invoke(account.Name, i, accounts.Count);
                    await SyncAccountAsync(account, cancellationToken, onCalendarSyncStart, onEventSyncStart);
                    result.SyncedAccounts++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error syncing account {account.Name}: {ex.Message}");
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
    /// Syncs a single account using the appropriate provider.
    /// </summary>
    private async Task SyncAccountAsync(
        AccountDbo account,
        CancellationToken cancellationToken,
        CalendarSyncProgressHandler? onCalendarSyncStart = null,
        EventSyncProgressHandler? onEventSyncStart = null)
    {
        Console.WriteLine($"Syncing account: {account.Name} (Type: {account.Type})");

        // Get the provider for this account type
        if (!_providers.TryGetValue(account.Type, out var provider))
        {
            throw new InvalidOperationException($"No provider registered for account type: {account.Type}");
        }

        // Get credentials from credential manager
        var credentials = GetCredentialsForAccount(account);
        if (credentials == null)
        {
            throw new InvalidOperationException($"No credentials found for account {account.Name}");
        }

        // Sync calendars
        await SyncCalendarsAsync(provider, account, credentials, cancellationToken);

        // Sync events for each enabled calendar
        var calendars = await _storage.GetCalendarsByAccountAsync(account.AccountId);
        var enabledCalendars = calendars.Where(c => c.Enabled == 1).ToList();

        for (int i = 0; i < enabledCalendars.Count; i++)
        {
            var calendar = enabledCalendars[i];
            try
            {
                onCalendarSyncStart?.Invoke(calendar.Name, i, enabledCalendars.Count);
                await SyncCalendarEventsAsync(provider, calendar, credentials, account.Type, cancellationToken, onEventSyncStart);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error syncing events for calendar {calendar.Name}: {ex.Message}");
                // Continue with other calendars
            }
        }
    }

    /// <summary>
    /// Syncs calendars for an account using the provider.
    /// </summary>
    private async Task SyncCalendarsAsync(
        ICalendarProvider provider,
        AccountDbo account,
        AccountCredentials credentials,
        CancellationToken cancellationToken)
    {
        // Load sync token from account data for incremental sync
        string? syncToken = await _storage.GetAccountData(account, "calendarSyncToken");
        bool isFullSync = string.IsNullOrEmpty(syncToken);

        // Fetch calendars with optional sync token
        CalendarSyncResult result;
        try
        {
            result = await provider.GetCalendarsAsync(credentials, syncToken, cancellationToken);
            Console.WriteLine($"Found {result.Calendars.Count} calendar {(isFullSync ? "items" : "changes")} for account {account.Name}");
        }
        catch (Exception ex) when (ex.Message.Contains("410") || ex.Message.Contains("invalid") || ex.Message.Contains("Sync token"))
        {
            // Sync token is invalid or expired, fall back to full sync
            Console.WriteLine($"Sync token invalid, performing full sync: {ex.Message}");
            isFullSync = true;
            result = await provider.GetCalendarsAsync(credentials, null, cancellationToken);
            Console.WriteLine($"Found {result.Calendars.Count} calendars in full sync for account {account.Name}");
        }

        // Update credentials in case tokens were refreshed (for Google)
        UpdateCredentialsIfNeeded(account, credentials);

        // Track the current sync timestamp for cleanup
        var currentSyncTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Save calendars to database
        foreach (var calendar in result.Calendars)
        {
            if (calendar.Deleted)
            {
                Console.WriteLine($"Calendar {calendar.Name} was deleted, will clean up");
                continue;
            }

            var calendarDbo = new CalendarDbo
            {
                AccountId = account.AccountId,
                CalendarId = string.Empty, // Will be set by CreateOrUpdateCalendarAsync
                ExternalId = calendar.ExternalId,
                Name = calendar.Name,
                Color = calendar.Color,
                Enabled = calendar.Selected ? 1 : 0,
                LastSync = currentSyncTime,
            };

            await _storage.CreateOrUpdateCalendarAsync(calendarDbo);

            // Store raw provider data if available
            if (!string.IsNullOrEmpty(calendar.RawData))
            {
                await _storage.SetCalendarDataJson(calendarDbo, "rawData", calendar.RawData);
            }
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
    }

    /// <summary>
    /// Syncs events for a calendar using the provider.
    /// </summary>
    private async Task SyncCalendarEventsAsync(
        ICalendarProvider provider,
        CalendarDbo calendar,
        AccountCredentials credentials,
        string accountType,
        CancellationToken cancellationToken,
        EventSyncProgressHandler? onEventSyncStart = null)
    {
        Console.WriteLine($"Syncing events for calendar: {calendar.Name}");

        // Load sync token from calendar data for incremental sync
        string? syncToken = await _storage.GetCalendarData(calendar, "eventSyncToken");
        bool isFullSync = string.IsNullOrEmpty(syncToken);

        // Fetch events with optional sync token
        EventSyncResult result;
        try
        {
            result = await provider.GetEventsAsync(credentials, calendar.ExternalId ?? string.Empty, syncToken, cancellationToken);
            Console.WriteLine($"Found {result.Events.Count} event {(isFullSync ? "items" : "changes")} for calendar {calendar.Name}");
        }
        catch (Exception ex) when (ex.Message.Contains("410") || ex.Message.Contains("invalid") || ex.Message.Contains("Sync token"))
        {
            // Sync token is invalid or expired, fall back to full sync
            Console.WriteLine($"Event sync token invalid, performing full sync: {ex.Message}");
            isFullSync = true;
            result = await provider.GetEventsAsync(credentials, calendar.ExternalId ?? string.Empty, null, cancellationToken);
            Console.WriteLine($"Found {result.Events.Count} events in full sync for calendar {calendar.Name}");
        }

        // Track the current sync timestamp for cleanup
        var currentSyncTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Determine account type for reminders
        var reminderAccountType = accountType.Equals("Google", StringComparison.OrdinalIgnoreCase)
            ? AccountType.Google
            : AccountType.CalDav;

        // Save events to database
        foreach (var evt in result.Events)
        {
            if (evt.Deleted)
            {
                Console.WriteLine($"Event {evt.Title} was deleted, will clean up");
                continue;
            }

            var eventDbo = new CalendarEventDbo
            {
                CalendarId = calendar.CalendarId,
                EventId = string.Empty, // Will be set by CreateOrUpdateEventAsync
                ExternalId = evt.ExternalId,
                StartTime = evt.StartTime.HasValue ? new DateTimeOffset(evt.StartTime.Value).ToUnixTimeSeconds() : null,
                EndTime = evt.EndTime.HasValue ? new DateTimeOffset(evt.EndTime.Value).ToUnixTimeSeconds() : null,
                Title = evt.Title ?? "Untitled Event",
                ChangedAt = currentSyncTime,
            };

            var eventId = await _storage.CreateOrUpdateEventAsync(eventDbo);

            // Store raw provider data
            if (!string.IsNullOrEmpty(evt.RawData))
            {
                await _storage.SetEventData(eventId, "rawData", evt.RawData);
            }

            // Populate reminders for this event
            await _reminderService.PopulateRemindersForEventAsync(eventId, calendar.CalendarId, reminderAccountType, cancellationToken);

            // Handle override relationship (Google-specific)
            if (!string.IsNullOrEmpty(evt.RecurringEventId))
            {
                var parentEventId = await _storage.GetEventIdByExternalIdAsync(calendar.CalendarId, evt.RecurringEventId);

                if (parentEventId != null)
                {
                    await _storage.CreateEventRelationAsync(parentEventId, eventId);
                }
                else
                {
                    await _storage.AddEventRelationToBacklogAsync(calendar.CalendarId, evt.RecurringEventId, evt.ExternalId);
                }
            }
        }

        // Process backlog - check if any parents now exist
        await _storage.ProcessEventRelationBacklogAsync(calendar.CalendarId);

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
        onEventSyncStart?.Invoke(calendar.Name, result.Events.Count);
    }

    /// <summary>
    /// Gets credentials for an account based on its type.
    /// </summary>
    private AccountCredentials? GetCredentialsForAccount(AccountDbo account)
    {
        if (account.Type.Equals("Google", StringComparison.OrdinalIgnoreCase))
        {
            return _credentialManager.GetGoogleCredentials(account.AccountId);
        }
        else if (account.Type.Equals("CalDAV", StringComparison.OrdinalIgnoreCase))
        {
            return _credentialManager.GetCalDavCredentials(account.AccountId);
        }

        return null;
    }

    /// <summary>
    /// Updates credentials in the credential manager if they were refreshed during sync.
    /// </summary>
    private void UpdateCredentialsIfNeeded(AccountDbo account, AccountCredentials credentials)
    {
        if (credentials is GoogleCredentials googleCredentials)
        {
            _credentialManager.StoreGoogleCredentials(account.AccountId, googleCredentials);
        }
        // CalDAV credentials don't need to be updated after sync
    }
}

public class SyncResult
{
    public bool Success { get; set; }
    public int SyncedAccounts { get; set; }
    public int FailedAccounts { get; set; }
    public int TotalCalendars { get; set; }
    public List<string> Errors { get; set; } = [];
}
