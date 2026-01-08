using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using perinma.Services;
using perinma.Storage.Models;

namespace perinma.Storage;

public class SqliteStorage(DatabaseService databaseService, CredentialManagerService credentialManager)
{
    public async Task<IEnumerable<AccountDbo>> GetAllAccountsAsync()
    {
        using var connection = databaseService.GetConnection();
        return await connection.QueryAsync<AccountDbo>(
            "SELECT account_id AS AccountId, name AS Name, type AS Type, data AS Data FROM account",
            commandTimeout: 30
        );
    }

    public async Task<AccountDbo?> GetAccountByIdAsync(string accountId)
    {
        using var connection = databaseService.GetConnection();
        return await connection.QuerySingleOrDefaultAsync<AccountDbo>(
            "SELECT account_id AS AccountId, name AS Name, type AS Type, data AS Data FROM account WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    public async Task<AccountDbo?> GetAccountByNameAsync(string name)
    {
        using var connection = databaseService.GetConnection();
        return await connection.QuerySingleOrDefaultAsync<AccountDbo>(
            "SELECT account_id AS AccountId, name AS Name, type AS Type, data AS Data FROM account WHERE name = @Name",
            new { Name = name },
            commandTimeout: 30
        );
    }

    public async Task<bool> IsAccountNameUniqueAsync(string name, string? excludeAccountId = null)
    {
        using var connection = databaseService.GetConnection();

        var query = excludeAccountId == null
            ? "SELECT COUNT(*) FROM account WHERE name = @Name"
            : "SELECT COUNT(*) FROM account WHERE name = @Name AND account_id != @ExcludeAccountId";

        var count = await connection.ExecuteScalarAsync<int>(
            query,
            new { Name = name, ExcludeAccountId = excludeAccountId },
            commandTimeout: 30
        );

        return count == 0;
    }

    public async Task<bool> CreateAccountAsync(AccountDbo account)
    {
        using var connection = databaseService.GetConnection();

        // Data field is stored as NULL - we use it for sync metadata via SetAccountData/GetAccountData
        var rowsAffected = await connection.ExecuteAsync(
            "INSERT INTO account (account_id, name, type) VALUES (@AccountId, @Name, @Type)",
            account,
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<bool> UpdateAccountAsync(AccountDbo account)
    {
        using var connection = databaseService.GetConnection();

        var rowsAffected = await connection.ExecuteAsync(
            "UPDATE account SET name = @Name, type = @Type WHERE account_id = @AccountId",
            account,
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<bool> SetAccountData(AccountDbo account, string key, string value)
    {
        using var connection = databaseService.GetConnection();

        var rowsAffected = await connection.ExecuteAsync(
            """
                UPDATE account 
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, @value)
                WHERE account_id = @account_id
            """,
            param: new { key = $"$.{key}", value, account_id = account.AccountId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<string?> GetAccountData(AccountDbo account, string key)
    {
        using var connection = databaseService.GetConnection();

        return await connection.QuerySingleAsync<string?>(
            """
            SELECT coalesce(data ->> @key, '') as value
            FROM account
            WHERE account_id = @account_id
            """,
            param: new { key = $"$.{key}", account_id = account.AccountId });
    }

    public async Task<bool> DeleteAccountAsync(string accountId)
    {
        using var connection = databaseService.GetConnection();

        var rowsAffected = await connection.ExecuteAsync(
            "DELETE FROM account WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );

        // Also delete credentials from platform keyring
        if (rowsAffected > 0)
        {
            credentialManager.DeleteCredentials(accountId);
        }

        return rowsAffected > 0;
    }

    #region Calendar Methods

    public async Task<IEnumerable<CalendarDbo>> GetCalendarsByAccountAsync(string accountId)
    {
        using var connection = databaseService.GetConnection();
        return await connection.QueryAsync<CalendarDbo>(
            "SELECT account_id AS AccountId, calendar_id AS CalendarId, external_id AS ExternalId, " +
            "name AS Name, color AS Color, enabled AS Enabled, last_sync AS LastSync, data AS Data " +
            "FROM calendar WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    public async Task<CalendarDbo?> GetCalendarByExternalIdAsync(string accountId, string externalId)
    {
        using var connection = databaseService.GetConnection();
        return await connection.QuerySingleOrDefaultAsync<CalendarDbo>(
            "SELECT account_id AS AccountId, calendar_id AS CalendarId, external_id AS ExternalId, " +
            "name AS Name, color AS Color, enabled AS Enabled, last_sync AS LastSync, data AS Data " +
            "FROM calendar WHERE account_id = @AccountId AND external_id = @ExternalId",
            new { AccountId = accountId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<bool> CreateOrUpdateCalendarAsync(CalendarDbo calendar)
    {
        using var connection = databaseService.GetConnection();

        // Check if calendar already exists by external_id
        var existing = await GetCalendarByExternalIdAsync(calendar.AccountId, calendar.ExternalId ?? string.Empty);

        if (existing != null)
        {
            // Update existing calendar - keep the existing calendar_id (UUID)
            var rowsAffected = await connection.ExecuteAsync(
                "UPDATE calendar SET name = @Name, color = @Color, enabled = @Enabled, " +
                "last_sync = @LastSync, data = @Data " +
                "WHERE account_id = @AccountId AND external_id = @ExternalId",
                new
                {
                    calendar.Name,
                    calendar.Color,
                    calendar.Enabled,
                    calendar.LastSync,
                    calendar.Data,
                    calendar.AccountId,
                    calendar.ExternalId
                },
                commandTimeout: 30
            );

            // Update the calendar object with the existing UUID for the caller
            calendar.CalendarId = existing.CalendarId;

            return rowsAffected > 0;
        }
        else
        {
            // Insert new calendar with a generated UUID
            var calendarId = System.Guid.NewGuid().ToString();
            var rowsAffected = await connection.ExecuteAsync(
                "INSERT INTO calendar (account_id, calendar_id, external_id, name, color, enabled, last_sync, data) " +
                "VALUES (@AccountId, @CalendarId, @ExternalId, @Name, @Color, @Enabled, @LastSync, @Data)",
                new
                {
                    calendar.AccountId,
                    CalendarId = calendarId,
                    calendar.ExternalId,
                    calendar.Name,
                    calendar.Color,
                    calendar.Enabled,
                    calendar.LastSync,
                    calendar.Data
                },
                commandTimeout: 30
            );

            // Update the calendar object with the generated ID for the caller
            calendar.CalendarId = calendarId;

            return rowsAffected > 0;
        }
    }

    public async Task<bool> DeleteCalendarAsync(string calendarId)
    {
        using var connection = databaseService.GetConnection();

        var rowsAffected = await connection.ExecuteAsync(
            "DELETE FROM calendar WHERE calendar_id = @CalendarId",
            new { CalendarId = calendarId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<int> DeleteCalendarsNotSyncedAsync(string accountId, long currentSyncTime)
    {
        using var connection = databaseService.GetConnection();

        var rowsAffected = await connection.ExecuteAsync(
            "DELETE FROM calendar WHERE account_id = @AccountId AND last_sync < @CurrentSyncTime",
            new { AccountId = accountId, CurrentSyncTime = currentSyncTime },
            commandTimeout: 30
        );

        return rowsAffected;
    }

    #endregion
}