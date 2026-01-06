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

        // TODO: Serialize credentials to JSON for the data blob
        // For now, we're storing accounts without credentials (data = null)
        var rowsAffected = await connection.ExecuteAsync(
            "INSERT INTO account (account_id, name, type, data) VALUES (@AccountId, @Name, @Type, @Data)",
            account,
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<bool> UpdateAccountAsync(AccountDbo account)
    {
        using var connection = databaseService.GetConnection();

        // TODO: Serialize credentials to JSON for the data blob
        var rowsAffected = await connection.ExecuteAsync(
            "UPDATE account SET name = @Name, type = @Type, data = @Data WHERE account_id = @AccountId",
            account,
            commandTimeout: 30
        );

        return rowsAffected > 0;
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

        // Check if calendar already exists
        var existing = await GetCalendarByExternalIdAsync(calendar.AccountId, calendar.ExternalId ?? string.Empty);

        if (existing != null)
        {
            // Update existing calendar
            var rowsAffected = await connection.ExecuteAsync(
                "UPDATE calendar SET name = @Name, color = @Color, enabled = @Enabled, " +
                "last_sync = @LastSync, data = @Data " +
                "WHERE account_id = @AccountId AND external_id = @ExternalId",
                calendar,
                commandTimeout: 30
            );
            return rowsAffected > 0;
        }
        else
        {
            // Insert new calendar
            var rowsAffected = await connection.ExecuteAsync(
                "INSERT INTO calendar (account_id, calendar_id, external_id, name, color, enabled, last_sync, data) " +
                "VALUES (@AccountId, @CalendarId, @ExternalId, @Name, @Color, @Enabled, @LastSync, @Data)",
                calendar,
                commandTimeout: 30
            );
            return rowsAffected > 0;
        }
    }

    #endregion
}
