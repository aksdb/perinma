using System;
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

    /// <summary>
    /// Clears all sync data for an account, preparing it for a full resync.
    /// Deletes all calendars (and their events via cascade) and clears the calendar sync token.
    /// </summary>
    public async Task ClearAccountSyncDataAsync(string accountId)
    {
        using var connection = databaseService.GetConnection();

        // Delete all calendars for this account (events are cascade-deleted)
        await connection.ExecuteAsync(
            "DELETE FROM calendar WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );

        // Clear only the account's calendar sync token
        await connection.ExecuteAsync(
            "UPDATE account SET data = jsonb_remove(coalesce(data, jsonb_object()), '$.calendarSyncToken') WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );
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

    public async Task<CalendarDbo?> GetCalendarByIdAsync(string calendarId)
    {
        using var connection = databaseService.GetConnection();
        return await connection.QuerySingleOrDefaultAsync<CalendarDbo>(
            "SELECT account_id AS AccountId, calendar_id AS CalendarId, external_id AS ExternalId, " +
            "name AS Name, color AS Color, enabled AS Enabled, last_sync AS LastSync, data AS Data " +
            "FROM calendar WHERE calendar_id = @CalendarId",
            new { CalendarId = calendarId },
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
                "last_sync = @LastSync " +
                "WHERE account_id = @AccountId AND external_id = @ExternalId",
                new
                {
                    calendar.Name,
                    calendar.Color,
                    calendar.Enabled,
                    calendar.LastSync,
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
                "INSERT INTO calendar (account_id, calendar_id, external_id, name, color, enabled, last_sync) " +
                "VALUES (@AccountId, @CalendarId, @ExternalId, @Name, @Color, @Enabled, @LastSync)",
                new
                {
                    calendar.AccountId,
                    CalendarId = calendarId,
                    calendar.ExternalId,
                    calendar.Name,
                    calendar.Color,
                    calendar.Enabled,
                    calendar.LastSync,
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

    public async Task<bool> SetCalendarData(CalendarDbo calendar, string key, string value)
    {
        using var connection = databaseService.GetConnection();

        var rowsAffected = await connection.ExecuteAsync(
            """
                UPDATE calendar
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, @value)
                WHERE calendar_id = @calendar_id
            """,
            param: new { key = $"$.{key}", value, calendar_id = calendar.CalendarId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<string?> GetCalendarData(CalendarDbo calendar, string key)
    {
        using var connection = databaseService.GetConnection();

        return await connection.QuerySingleAsync<string?>(
            """
            SELECT coalesce(data ->> @key, '') as value
            FROM calendar
            WHERE calendar_id = @calendar_id
            """,
            param: new { key = $"$.{key}", calendar_id = calendar.CalendarId });
    }

    public async Task<bool> UpdateCalendarEnabledAsync(string calendarId, bool enabled)
    {
        using var connection = databaseService.GetConnection();

        var rowsAffected = await connection.ExecuteAsync(
            "UPDATE calendar SET enabled = @Enabled WHERE calendar_id = @CalendarId",
            new { CalendarId = calendarId, Enabled = enabled ? 1 : 0 },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    #endregion

    #region Calendar Events

    public async Task<IEnumerable<CalendarEventDbo>> GetEventsByCalendarAsync(string calendarId)
    {
        using var connection = databaseService.GetConnection();
        return await connection.QueryAsync<CalendarEventDbo>(
            "SELECT calendar_id AS CalendarId, event_id AS EventId, external_id AS ExternalId, " +
            "start_time AS StartTime, end_time AS EndTime, title AS Title, changed_at AS ChangedAt " +
            "FROM calendar_event WHERE calendar_id = @CalendarId",
            new { CalendarId = calendarId },
            commandTimeout: 30
        );
    }

    public async Task<CalendarEventDbo?> GetEventByExternalIdAsync(string calendarId, string externalId)
    {
        using var connection = databaseService.GetConnection();
        return await connection.QuerySingleOrDefaultAsync<CalendarEventDbo>(
            "SELECT calendar_id AS CalendarId, event_id AS EventId, external_id AS ExternalId, " +
            "start_time AS StartTime, end_time AS EndTime, title AS Title, changed_at AS ChangedAt " +
            "FROM calendar_event WHERE calendar_id = @CalendarId AND external_id = @ExternalId",
            new { CalendarId = calendarId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    /// <summary>
    /// Create or update the given event. A possible update is determined by the combination
    /// of calendarId and externalId.
    /// </summary>
    /// <param name="eventDbo"></param>
    /// <returns>The id of the event.</returns>
    public async Task<string> CreateOrUpdateEventAsync(CalendarEventDbo eventDbo)
    {
        using var connection = databaseService.GetConnection();

        // Check if event already exists by external_id
        var existing = await GetEventByExternalIdAsync(eventDbo.CalendarId, eventDbo.ExternalId ?? string.Empty);

        if (existing != null)
        {
            // Update existing event - keep the existing event_id
            await connection.ExecuteAsync(
                "UPDATE calendar_event SET start_time = @start_time, end_time = @end_time, " +
                "title = @title, changed_at = @changed_at " +
                "WHERE calendar_id = @calendar_id AND external_id = @external_id",
                new
                {
                    calendar_id = eventDbo.CalendarId,
                    external_id = eventDbo.ExternalId,
                    start_time = eventDbo.StartTime,
                    end_time = eventDbo.EndTime,
                    title = eventDbo.Title,
                    changed_at = eventDbo.ChangedAt
                },
                commandTimeout: 30
            );

            return existing.EventId;
        }
        else
        {
            // Insert new event with generated UUID
            var newEventId = Guid.NewGuid().ToString();
            await connection.ExecuteAsync(
                "INSERT INTO calendar_event (calendar_id, event_id, external_id, start_time, end_time, title, changed_at) " +
                "VALUES (@calendar_id, @event_id, @external_id, @start_time, @end_time, @title, @changed_at)",
                new
                {
                    calendar_id = eventDbo.CalendarId,
                    event_id = newEventId,
                    external_id = eventDbo.ExternalId,
                    start_time = eventDbo.StartTime,
                    end_time = eventDbo.EndTime,
                    title = eventDbo.Title,
                    changed_at = eventDbo.ChangedAt
                },
                commandTimeout: 30
            );

            return newEventId;
        }
    }

    public async Task<int> DeleteEventsNotSyncedAsync(string calendarId, long currentSyncTime)
    {
        using var connection = databaseService.GetConnection();

        var rowsAffected = await connection.ExecuteAsync(
            "DELETE FROM calendar_event WHERE calendar_id = @CalendarId AND changed_at < @CurrentSyncTime",
            new { CalendarId = calendarId, CurrentSyncTime = currentSyncTime },
            commandTimeout: 30
        );

        return rowsAffected;
    }

    public async Task<bool> SetEventData(string eventId, string key, string value)
    {
        using var connection = databaseService.GetConnection();

        var rowsAffected = await connection.ExecuteAsync(
            """
                UPDATE calendar_event
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, @value)
                WHERE event_id = @eventId
            """,
            param: new { key = $"$.{key}", value, @eventId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }
    
    public async Task<bool> SetEventDataJson(string eventId, string key, string jsonValue)
    {
        using var connection = databaseService.GetConnection();

        var rowsAffected = await connection.ExecuteAsync(
            """
                UPDATE calendar_event
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, jsonb(@jsonValue))
                WHERE event_id = @eventId
            """,
            param: new { key = $"$.{key}", jsonValue, eventId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<string?> GetEventData(string eventId, string key)
    {
        using var connection = databaseService.GetConnection();

        return await connection.QuerySingleAsync<string?>(
            """
            SELECT coalesce(data ->> @key, '') as value
            FROM calendar_event
            WHERE event_id = @eventId
            """,
            param: new { key = $"$.{key}", eventId });
    }

    #endregion

    public async Task<IEnumerable<CalendarEventQueryResult>> GetEventsByTimeRangeAsync(DateTime startTime, DateTime endTime)
    {
        using var connection = databaseService.GetConnection();

        var startTimestamp = new DateTimeOffset(startTime).ToUnixTimeSeconds();
        var endTimestamp = new DateTimeOffset(endTime).ToUnixTimeSeconds();

        var query = @"
            SELECT
                ce.event_id AS EventId,
                ce.external_id AS ExternalId,
                ce.start_time AS StartTime,
                ce.end_time AS EndTime,
                ce.title AS Title,
                ce.changed_at AS ChangedAt,
                json_extract(ce.data, '$.rawData') AS RawData,
                c.calendar_id AS CalendarId,
                c.external_id AS CalendarExternalId,
                c.name AS CalendarName,
                c.color AS CalendarColor,
                c.enabled AS CalendarEnabled,
                c.last_sync AS CalendarLastSync,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType
            FROM calendar_event ce
            INNER JOIN calendar c ON ce.calendar_id = c.calendar_id
            INNER JOIN account a ON c.account_id = a.account_id
            WHERE c.enabled = 1
              AND (
                  (ce.start_time IS NULL OR ce.start_time < @EndTimestamp) AND
                  (ce.end_time IS NULL OR ce.end_time > @StartTimestamp)
              )
            ORDER BY ce.start_time";

        return await connection.QueryAsync<CalendarEventQueryResult>(
            query,
            new { StartTimestamp = startTimestamp, EndTimestamp = endTimestamp },
            commandTimeout: 30
        );
    }
}