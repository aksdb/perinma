using System;
using System.Linq;
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
    /// Syncs calendars from a single Google account
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

        // Create Google Calendar service
        var service = await _googleCalendarService.CreateServiceAsync(credentials, cancellationToken);

        // Update credentials in case tokens were refreshed
        _credentialManager.StoreGoogleCredentials(account.AccountId, credentials);

        // Fetch calendars
        var calendars = await _googleCalendarService.GetCalendarsAsync(service, cancellationToken);
        Console.WriteLine($"Found {calendars.Count} calendars for account {account.Name}");

        // Save calendars to database
        foreach (var calendar in calendars)
        {
            var calendarDbo = new CalendarDbo
            {
                AccountId = account.AccountId,
                CalendarId = calendar.Id ?? Guid.NewGuid().ToString(),
                ExternalId = calendar.Id,
                Name = calendar.Summary ?? "Unnamed Calendar",
                Color = calendar.BackgroundColor,
                Enabled = 1, // Enable by default
                LastSync = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Data = null // Could store additional metadata here if needed
            };

            await _storage.CreateOrUpdateCalendarAsync(calendarDbo);
        }

        Console.WriteLine($"Synced {calendars.Count} calendars for account {account.Name}");
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
