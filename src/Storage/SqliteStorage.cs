using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using perinma.Storage.Models;

namespace perinma.Storage;

public class SqliteStorage(DatabaseService databaseService)
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

        return rowsAffected > 0;
    }
}
