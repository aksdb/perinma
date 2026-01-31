using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using perinma.Storage;
using perinma.Storage.Models;
using perinma.Models;
using perinma.Messaging;

namespace perinma.Services;

public class SyncService
{
    private readonly SqliteStorage _storage;
    private readonly CredentialManagerService _credentialManager;
    private readonly ReminderService _reminderService;
    private readonly IReadOnlyDictionary<AccountType, ICalendarProvider> _providers;

    public SyncService(
        SqliteStorage storage,
        CredentialManagerService credentialManager,
        IReadOnlyDictionary<AccountType, ICalendarProvider> providers,
        ReminderService reminderService)
    {
        _storage = storage;
        _credentialManager = credentialManager;
        _providers = providers;
        _reminderService = reminderService;
    }

    /// <summary>
    /// Gets the calendar providers dictionary.
    /// </summary>
    public IReadOnlyDictionary<AccountType, ICalendarProvider> Providers => _providers;

    /// <summary>
    /// Forces a complete resync of an account by clearing all local data and sync tokens,
    /// then performing a full sync from the remote server.
    /// </summary>
    public async Task<SyncResult> ForceResyncAccountAsync(string accountId,
        CancellationToken cancellationToken = default)
    {
        var result = new SyncResult();
        WeakReferenceMessenger.Default.Send(new SyncStartedMessage());

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
                await SyncAccountAsync(account, cancellationToken);
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
        finally
        {
            WeakReferenceMessenger.Default.Send(new SyncEndedMessage());
        }

        return result;
    }

    /// <summary>
    /// Syncs calendars from all accounts.
    /// </summary>
    public async Task<SyncResult> SyncAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        var result = new SyncResult();
        WeakReferenceMessenger.Default.Send(new SyncStartedMessage());

        try
        {
            var accounts = (await _storage.GetAllAccountsAsync()).ToImmutableList();
            Console.WriteLine($"Found {accounts.Count} accounts to sync");

            for (int i = 0; i < accounts.Count; i++)
            {
                var account = accounts[i];
                try
                {
                    WeakReferenceMessenger.Default.Send(new SyncAccountProgressMessage
                    {
                        AccountName = account.Name,
                        AccountIndex = i,
                        TotalAccounts = accounts.Count
                    });
                    await SyncAccountAsync(account, cancellationToken);
                    result.SyncedAccounts++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error syncing account {account.Name}: {ex}");
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
        finally
        {
            WeakReferenceMessenger.Default.Send(new SyncEndedMessage());
        }

        return result;
    }

    /// <summary>
    /// Syncs a single account using the appropriate provider.
    /// </summary>
    private async Task SyncAccountAsync(
        AccountDbo account,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing account: {account.Name} (Type: {account.Type})");

        // Get the provider for this account type
        if (!_providers.TryGetValue(account.AccountTypeEnum, out var provider))
        {
            throw new InvalidOperationException($"No provider registered for account type: {account.Type}");
        }

        try
        {
            // Sync calendars
            await SyncCalendarsAsync(provider, account, cancellationToken);

            // Sync events for each enabled calendar
            var calendars = await _storage.GetCalendarsByAccountAsync(account.AccountId);
            var enabledCalendars = calendars.Where(c => c.Enabled == 1).ToList();

            for (int i = 0; i < enabledCalendars.Count; i++)
            {
                var calendar = enabledCalendars[i];
                try
                {
                    WeakReferenceMessenger.Default.Send(new SyncCalendarProgressMessage
                    {
                        CalendarName = calendar.Name,
                        CalendarIndex = i,
                        TotalCalendars = enabledCalendars.Count
                    });
                    await SyncCalendarEventsAsync(provider, calendar, account.AccountTypeEnum, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error syncing events for calendar {calendar.Name}: {ex.Message}");
                    // Continue with other calendars
                }
            }
        }
        catch (ReAuthenticationRequiredException ex)
        {
            // Account requires re-authentication - send message and continue with next account
            Console.WriteLine($"Account {account.Name} requires re-authentication: {ex.Message}");
            WeakReferenceMessenger.Default.Send(new ReAuthenticationRequiredMessage(ex.AccountId, ex.ProviderType));
        }
    }

    /// <summary>
    /// Syncs calendars for an account using the provider.
    /// </summary>
    private async Task SyncCalendarsAsync(
        ICalendarProvider provider,
        AccountDbo account,
        CancellationToken cancellationToken)
    {
        // Load sync token from account data for incremental sync
        string? syncToken = await _storage.GetAccountData(account, "calendarSyncToken");
        bool isFullSync = string.IsNullOrEmpty(syncToken);

        // Fetch calendars with optional sync token
        CalendarSyncResult result;
        try
        {
            result = await provider.GetCalendarsAsync(account.AccountId, syncToken, cancellationToken);
            Console.WriteLine(
                $"Found {result.Calendars.Count} calendar {(isFullSync ? "items" : "changes")} for account {account.Name}");
        }
        catch (Exception ex) when (ex.Message.Contains("410") || ex.Message.Contains("invalid") ||
                                   ex.Message.Contains("Sync token"))
        {
            // Sync token is invalid or expired, fall back to full sync
            Console.WriteLine($"Sync token invalid, performing full sync: {ex.Message}");
            isFullSync = true;
            result = await provider.GetCalendarsAsync(account.AccountId, null, cancellationToken);
            Console.WriteLine($"Found {result.Calendars.Count} calendars in full sync for account {account.Name}");
        }

        // Update credentials in case tokens were refreshed (for Google)
        UpdateCredentialsIfNeeded(account);

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

            // CalDAV doesn't have a "selected" concept, so preserve user's enabled preference
            // Google calendars do have selected status, so use the provider's value
            int enabled;
            if (account.AccountTypeEnum == AccountType.CalDav)
            {
                var existingCalendar =
                    await _storage.GetCalendarByExternalIdAsync(account.AccountId, calendar.ExternalId ?? string.Empty);
                enabled = existingCalendar?.Enabled ?? (calendar.Selected ? 1 : 0);
            }
            else
            {
                enabled = calendar.Selected ? 1 : 0;
            }

            var calendarDbo = new CalendarDbo
            {
                AccountId = account.AccountId,
                CalendarId = string.Empty, // Will be set by CreateOrUpdateCalendarAsync
                ExternalId = calendar.ExternalId,
                Name = calendar.Name,
                Color = calendar.Color,
                Enabled = enabled,
                LastSync = currentSyncTime,
            };

            await _storage.CreateOrUpdateCalendarAsync(calendarDbo);

            // Store raw provider data if available
            foreach (var dataPair in calendar.Data)
            {
                switch (dataPair.Value)
                {
                    case DataAttribute.Text text:
                        await _storage.SetCalendarDataAsync(calendarDbo.CalendarId, dataPair.Key, text.value);
                        break;
                    case DataAttribute.JsonText jsonText:
                        await _storage.SetCalendarDataJsonAsync(calendarDbo.CalendarId, dataPair.Key, jsonText.value);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown data type ${dataPair.Value.GetType()}");
                }
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
        AccountType accountType,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing events for calendar: {calendar.Name}");

        // Load sync token from calendar data for incremental sync
        string? syncToken = await _storage.GetCalendarDataAsync(calendar.CalendarId, "eventSyncToken");
        bool isFullSync = string.IsNullOrEmpty(syncToken);

        // Fetch events with optional sync token
        EventSyncResult result;
        try
        {
            result = await provider.GetEventsAsync(calendar.AccountId, calendar.ExternalId ?? string.Empty, syncToken,
                cancellationToken);
            Console.WriteLine(
                $"Found {result.Events.Count} event {(isFullSync ? "items" : "changes")} for calendar {calendar.Name}");
        }
        catch (Exception ex) when (ex.Message.Contains("410") || ex.Message.Contains("invalid") ||
                                   ex.Message.Contains("Sync token"))
        {
            // Sync token is invalid or expired, fall back to full sync
            Console.WriteLine($"Event sync token invalid, performing full sync: {ex.Message}");
            isFullSync = true;
            result = await provider.GetEventsAsync(calendar.AccountId, calendar.ExternalId ?? string.Empty, null,
                cancellationToken);
            Console.WriteLine($"Found {result.Events.Count} events in full sync for calendar {calendar.Name}");
        }

        // Track the current sync timestamp for cleanup
        var currentSyncTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Use account type for reminders
        var reminderAccountType = accountType;

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
            await _reminderService.PopulateRemindersForEventAsync(eventId, calendar.CalendarId, reminderAccountType,
                cancellationToken);

            // Handle override relationship (Google-specific)
            if (!string.IsNullOrEmpty(evt.RecurringEventId))
            {
                var parentEventId =
                    await _storage.GetEventIdByExternalIdAsync(calendar.CalendarId, evt.RecurringEventId);

                if (parentEventId != null)
                {
                    await _storage.CreateEventRelationAsync(parentEventId, eventId);
                }
                else
                {
                    await _storage.AddEventRelationToBacklogAsync(calendar.CalendarId, evt.RecurringEventId,
                        evt.ExternalId);
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
            await _storage.SetCalendarDataAsync(calendar.CalendarId, "eventSyncToken", result.SyncToken);
            Console.WriteLine($"Stored new event sync token for next sync");
        }

        Console.WriteLine($"Synced {result.Events.Count} events for calendar {calendar.Name}");
        WeakReferenceMessenger.Default.Send(new SyncEventsProgressMessage
        {
            CalendarName = calendar.Name,
            EventCount = result.Events.Count
        });
    }

    /// <summary>
    /// Updates credentials in the credential manager if they were refreshed during sync.
    /// </summary>
    private void UpdateCredentialsIfNeeded(AccountDbo account)
    {
        // Providers now manage credentials internally
        // Google provider will store updated access tokens automatically
        // CalDAV credentials don't change during sync
    }

    /// <summary>
    /// Gets the calendar provider for a specific account type.
    /// </summary>
    /// <param name="accountType">The account type (Google or CalDav)</param>
    /// <returns>The calendar provider, or null if not found</returns>
    public ICalendarProvider? GetProviderForAccountType(AccountType accountType)
    {
        return _providers.GetValueOrDefault(accountType);
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