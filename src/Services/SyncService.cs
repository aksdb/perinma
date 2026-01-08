using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using perinma.Storage;
using perinma.Storage.Models;

namespace perinma.Services;

public class SyncService
{
    private readonly SqliteStorage _storage;
    private readonly CredentialManagerService _credentialManager;
    private readonly GoogleCalendarService _googleCalendarService;

    public SyncService(
        SqliteStorage storage,
        CredentialManagerService credentialManager,
        GoogleCalendarService googleCalendarService)
    {
        _storage = storage;
        _credentialManager = credentialManager;
        _googleCalendarService = googleCalendarService;
    }

    /// <summary>
    /// Syncs calendars from all Google accounts
    /// </summary>
    public async Task<SyncResult> SyncAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        var result = new SyncResult();

        try
        {
            // Get all accounts
            var accounts = await _storage.GetAllAccountsAsync();
            var googleAccounts = accounts.Where(a => a.Type.Equals("Google", StringComparison.OrdinalIgnoreCase)).ToList();

            Console.WriteLine($"Found {googleAccounts.Count} Google accounts to sync");

            foreach (var account in googleAccounts)
            {
                try
                {
                    await SyncAccountAsync(account, cancellationToken);
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
    /// Syncs calendars from a single Google account using incremental sync when possible
    /// </summary>
    private async Task SyncAccountAsync(AccountDbo account, CancellationToken cancellationToken)
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
                Enabled = 1, // Enable by default
                LastSync = currentSyncTime,
                Data = null // Future: Could store per-calendar sync data here
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
