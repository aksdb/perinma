using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using NodaTime;
using perinma.Models;
using perinma.Services;
using perinma.Storage.Models;

namespace perinma.Storage;

public class SqliteStorage : IDisposable
{
    private readonly DatabaseService _databaseService;
    private readonly CredentialManagerService _credentialManager;
    private readonly SqliteConnection _connection;

    // In-memory cache for Account models (shared across domains)
    private readonly ConcurrentDictionary<Guid, Account> _accountCache = new();
    private bool _cacheInitialized = false;
    private readonly object _cacheLock = new();

    public SqliteStorage(DatabaseService databaseService, CredentialManagerService credentialManager)
    {
        _databaseService = databaseService;
        _credentialManager = credentialManager;
        _connection = (SqliteConnection)databaseService.GetConnection();
        _connection.Open();
    }

    public async Task<IEnumerable<AccountDbo>> GetAllAccountsAsync()
    {
        return await _connection.QueryAsync<AccountDbo>(
            "SELECT account_id AS AccountId, name AS Name, type AS Type, capabilities AS Capabilities, sort_order AS SortOrder FROM account ORDER BY sort_order, name",
            commandTimeout: 30
        );
    }

    public async Task<AccountDbo?> GetAccountByIdAsync(string accountId)
    {
        return await _connection.QuerySingleOrDefaultAsync<AccountDbo>(
            "SELECT account_id AS AccountId, name AS Name, type AS Type, capabilities AS Capabilities, sort_order AS SortOrder FROM account WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    public async Task<AccountDbo?> GetAccountByNameAsync(string name)
    {
        return await _connection.QuerySingleOrDefaultAsync<AccountDbo>(
            "SELECT account_id AS AccountId, name AS Name, type AS Type, capabilities AS Capabilities, sort_order AS SortOrder FROM account WHERE name = @Name",
            new { Name = name },
            commandTimeout: 30
        );
    }

    public async Task<bool> IsAccountNameUniqueAsync(string name, string? excludeAccountId = null)
    {
        var query = excludeAccountId == null
            ? "SELECT COUNT(*) FROM account WHERE name = @Name"
            : "SELECT COUNT(*) FROM account WHERE name = @Name AND account_id != @ExcludeAccountId";

        var count = await _connection.ExecuteScalarAsync<int>(
            query,
            new { Name = name, ExcludeAccountId = excludeAccountId },
            commandTimeout: 30
        );

        return count == 0;
    }

    public async Task<bool> CreateAccountAsync(AccountDbo account)
    {
        var accountCapabilities = account.Capabilities != 0
            ? account.Capabilities
            : (int)AccountDbo.GetDefaultCapabilities(account.AccountTypeEnum);

        var rowsAffected = await _connection.ExecuteAsync(
            "INSERT INTO account (account_id, name, type, capabilities) VALUES (@AccountId, @Name, @Type, @Capabilities)",
            new
            {
                account.AccountId,
                account.Name,
                account.Type,
                Capabilities = accountCapabilities
            },
            commandTimeout: 30
        );

        if (rowsAffected > 0 && _cacheInitialized)
        {
            var accountModel = new Account
            {
                Id = Guid.Parse(account.AccountId),
                Name = account.Name,
                Type = account.AccountTypeEnum,
                Capabilities = (AccountCapability)accountCapabilities,
                SortOrder = account.SortOrder
            };
            _accountCache[accountModel.Id] = accountModel;
        }

        account.Capabilities = accountCapabilities;
        return rowsAffected > 0;
    }

    public async Task<bool> UpdateAccountAsync(AccountDbo account)
    {
        var accountCapabilities = account.Capabilities != 0
            ? account.Capabilities
            : (int)AccountDbo.GetDefaultCapabilities(account.AccountTypeEnum);

        var rowsAffected = await _connection.ExecuteAsync(
            "UPDATE account SET name = @Name, type = @Type, capabilities = @Capabilities WHERE account_id = @AccountId",
            new
            {
                account.AccountId,
                account.Name,
                account.Type,
                Capabilities = accountCapabilities
            },
            commandTimeout: 30
        );

        if (rowsAffected > 0 && _cacheInitialized)
        {
            var accountId = Guid.Parse(account.AccountId);
            if (_accountCache.TryGetValue(accountId, out var cachedAccount))
            {
                cachedAccount.Name = account.Name;
                cachedAccount.Type = account.AccountTypeEnum;
                cachedAccount.Capabilities = (AccountCapability)accountCapabilities;
            }
        }

        account.Capabilities = accountCapabilities;
        return rowsAffected > 0;
    }

    public async Task<bool> UpdateAccountCapabilitiesAsync(string accountId, AccountCapability capabilities)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "UPDATE account SET capabilities = @Capabilities WHERE account_id = @AccountId",
            new
            {
                AccountId = accountId,
                Capabilities = (int)capabilities
            },
            commandTimeout: 30
        );

        if (rowsAffected > 0 && _cacheInitialized)
        {
            var accountGuid = Guid.Parse(accountId);
            if (_accountCache.TryGetValue(accountGuid, out var cachedAccount))
            {
                cachedAccount.Capabilities = capabilities;
            }
        }

        return rowsAffected > 0;
    }

    public async Task UpdateAccountSortOrdersAsync(IEnumerable<(string AccountId, int SortOrder)> sortOrders)
    {
        foreach (var (accountId, sortOrder) in sortOrders)
        {
            await _connection.ExecuteAsync(
                "UPDATE account SET sort_order = @SortOrder WHERE account_id = @AccountId",
                new { AccountId = accountId, SortOrder = sortOrder },
                commandTimeout: 30
            );
        }
    }

    public async Task<bool> SetAccountData(AccountDbo account, string key, string value)
    {
        var rowsAffected = await _connection.ExecuteAsync(
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
        return await _connection.QuerySingleAsync<string?>(
            """
            SELECT coalesce(data ->> @key, '') as value
            FROM account
            WHERE account_id = @account_id
            """,
            param: new { key = $"$.{key}", account_id = account.AccountId });
    }

    public async Task<bool> DeleteAccountAsync(string accountId)
    {
        var accountIdGuid = Guid.Parse(accountId);
        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM account WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );

        if (rowsAffected > 0)
        {
            _credentialManager.DeleteCredentials(accountId);

            // Always invalidate cache, regardless of whether it's initialized
            InvalidateAccountCache(accountIdGuid);
        }

        return rowsAffected > 0;
    }

    /// <summary>
    /// Clears all sync data for an account, preparing it for a full resync.
    /// Deletes all calendars (and their events via cascade) and clears the calendar sync token.
    /// </summary>
    public async Task ClearAccountSyncDataAsync(string accountId)
    {
        await _connection.ExecuteAsync(
            "DELETE FROM calendar WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );

        await _connection.ExecuteAsync(
            "UPDATE account SET data = jsonb_remove(coalesce(data, jsonb_object()), '$.calendarSyncToken') WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );

        ClearCache();
    }

    #region Calendar Methods

    public async Task<IEnumerable<CalendarDbo>> GetCalendarsByAccountAsync(string accountId)
    {
        return await _connection.QueryAsync<CalendarDbo>(
            "SELECT account_id AS AccountId, calendar_id AS CalendarId, external_id AS ExternalId, " +
            "name AS Name, color AS Color, enabled AS Enabled, last_sync AS LastSync " +
            "FROM calendar WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    public async Task<CalendarDbo?> GetCalendarByExternalIdAsync(string accountId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<CalendarDbo>(
            "SELECT account_id AS AccountId, calendar_id AS CalendarId, external_id AS ExternalId, " +
            "name AS Name, color AS Color, enabled AS Enabled, last_sync AS LastSync " +
            "FROM calendar WHERE account_id = @AccountId AND external_id = @ExternalId",
            new { AccountId = accountId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<CalendarDbo?> GetCalendarByIdAsync(string calendarId)
    {
        return await _connection.QuerySingleOrDefaultAsync<CalendarDbo>(
            "SELECT account_id AS AccountId, calendar_id AS CalendarId, external_id AS ExternalId, " +
            "name AS Name, color AS Color, enabled AS Enabled, last_sync AS LastSync " +
            "FROM calendar WHERE calendar_id = @CalendarId",
            new { CalendarId = calendarId },
            commandTimeout: 30
        );
    }

    public async Task<bool> CreateOrUpdateCalendarAsync(CalendarDbo calendar)
    {
        var existing = await GetCalendarByExternalIdAsync(calendar.AccountId, calendar.ExternalId ?? string.Empty);

        if (existing != null)
        {
            var rowsAffected = await _connection.ExecuteAsync(
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

            calendar.CalendarId = existing.CalendarId;
            return rowsAffected > 0;
        }

        var calendarId = Guid.NewGuid().ToString();
        var inserted = await _connection.ExecuteAsync(
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

        calendar.CalendarId = calendarId;
        return inserted > 0;
    }

    public async Task<bool> DeleteCalendarAsync(string calendarId)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM calendar WHERE calendar_id = @CalendarId",
            new { CalendarId = calendarId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<int> DeleteCalendarsNotSyncedAsync(string accountId, long currentSyncTime)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM calendar WHERE account_id = @AccountId AND last_sync < @CurrentSyncTime",
            new { AccountId = accountId, CurrentSyncTime = currentSyncTime },
            commandTimeout: 30
        );

        return rowsAffected;
    }

    public async Task<bool> SetCalendarDataAsync(string calendarId, string key, string value)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
                UPDATE calendar
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, @value)
                WHERE calendar_id = @calendar_id
            """,
            param: new { key = $"$.{key}", value, calendar_id = calendarId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<bool> SetCalendarDataJsonAsync(string calendarId, string key, string jsonValue)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
                UPDATE calendar
                    SET data = jsonb_set(coalesce(data, jsonb_object()), @key, jsonb(@jsonValue))
                    WHERE calendar_id = @calendar_id
            """,
            param: new { key = $"$.{key}", jsonValue, calendar_id = calendarId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<string?> GetCalendarDataAsync(string calendarId, string key)
    {
        return await _connection.QuerySingleAsync<string?>(
            """
            SELECT coalesce(data ->> @key, '') as value
            FROM calendar
            WHERE calendar_id = @calendar_id
            """,
            param: new { key = $"$.{key}", calendar_id = calendarId });
    }

    public async Task<bool> UpdateCalendarEnabledAsync(string calendarId, bool enabled)
    {
        var rowsAffected = await _connection.ExecuteAsync(
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
        return await _connection.QueryAsync<CalendarEventDbo>(
            "SELECT calendar_id AS CalendarId, event_id AS EventId, external_id AS ExternalId, " +
            "start_time AS StartTime, end_time AS EndTime, title AS Title, changed_at AS ChangedAt " +
            "FROM calendar_event WHERE calendar_id = @CalendarId",
            new { CalendarId = calendarId },
            commandTimeout: 30
        );
    }

    public async Task<CalendarEventDbo?> GetEventByExternalIdAsync(string calendarId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<CalendarEventDbo>(
            "SELECT calendar_id AS CalendarId, event_id AS EventId, external_id AS ExternalId, " +
            "start_time AS StartTime, end_time AS EndTime, title AS Title, changed_at AS ChangedAt " +
            "FROM calendar_event WHERE calendar_id = @CalendarId AND external_id = @ExternalId",
            new { CalendarId = calendarId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<CalendarEventDbo?> GetEventByIdAsync(string eventId)
    {
        return await _connection.QuerySingleOrDefaultAsync<CalendarEventDbo>(
            "SELECT calendar_id AS CalendarId, event_id AS EventId, external_id AS ExternalId, " +
            "start_time AS StartTime, end_time AS EndTime, title AS Title, changed_at AS ChangedAt " +
            "FROM calendar_event WHERE event_id = @EventId",
            new { EventId = eventId },
            commandTimeout: 30
        );
    }

    public async Task<bool> DeleteEventByExternalIdAsync(string calendarId, string externalId)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM calendar_event WHERE calendar_id = @CalendarId AND external_id = @ExternalId",
            new { CalendarId = calendarId, ExternalId = externalId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    /// <summary>
    /// Create or update the given event. A possible update is determined by the combination
    /// of calendarId and externalId.
    /// </summary>
    /// <param name="eventDbo"></param>
    /// <returns>The id of the event.</returns>
    public async Task<string> CreateOrUpdateEventAsync(CalendarEventDbo eventDbo)
    {
        // Check if event already exists by external_id
        var existing = await GetEventByExternalIdAsync(eventDbo.CalendarId, eventDbo.ExternalId ?? string.Empty);

        if (existing != null)
        {
            // Update existing event - keep the existing event_id
            await _connection.ExecuteAsync(
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
            await _connection.ExecuteAsync(
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
        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM calendar_event WHERE calendar_id = @CalendarId AND changed_at < @CurrentSyncTime",
            new { CalendarId = calendarId, CurrentSyncTime = currentSyncTime },
            commandTimeout: 30
        );

        return rowsAffected;
    }

    public async Task<bool> SetEventData(string eventId, string key, string value)
    {
        var rowsAffected = await _connection.ExecuteAsync(
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
        var rowsAffected = await _connection.ExecuteAsync(
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
        return await _connection.QuerySingleAsync<string?>(
            """
            SELECT coalesce(data ->> @key, '') as value
            FROM calendar_event
            WHERE event_id = @eventId
            """,
            param: new { key = $"$.{key}", eventId });
    }

    public async Task<string?> GetEventIdByExternalIdAsync(string calendarId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT event_id FROM calendar_event WHERE calendar_id = @CalendarId AND external_id = @ExternalId",
            new { CalendarId = calendarId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task CreateEventRelationAsync(string parentEventId, string childEventId)
    {
        await _connection.ExecuteAsync(
            "INSERT OR REPLACE INTO calendar_event_relation (parent_event_id, child_event_id) VALUES (@ParentEventId, @ChildEventId)",
            new { ParentEventId = parentEventId, ChildEventId = childEventId },
            commandTimeout: 30
        );
    }

    public async Task AddEventRelationToBacklogAsync(string calendarId, string parentExternalId, string childExternalId)
    {
        await _connection.ExecuteAsync(
            "INSERT OR REPLACE INTO calendar_event_relation_backlog (calendar_id, parent_external_id, child_external_id) VALUES (@CalendarId, @ParentExternalId, @ChildExternalId)",
            new { CalendarId = calendarId, ParentExternalId = parentExternalId, ChildExternalId = childExternalId },
            commandTimeout: 30
        );
    }

    public async Task ProcessEventRelationBacklogAsync(string calendarId)
    {
        var backlogItems = await _connection.QueryAsync<(string ParentExternalId, string ChildExternalId)>(
            "SELECT parent_external_id, child_external_id FROM calendar_event_relation_backlog WHERE calendar_id = @CalendarId",
            new { CalendarId = calendarId },
            commandTimeout: 30
        );

        foreach (var (parentExternalId, childExternalId) in backlogItems)
        {
            var parentEventId = await GetEventIdByExternalIdAsync(calendarId, parentExternalId);
            var childEventId = await GetEventIdByExternalIdAsync(calendarId, childExternalId);

            if (parentEventId != null && childEventId != null)
            {
                await CreateEventRelationAsync(parentEventId, childEventId);
                await _connection.ExecuteAsync(
                    "DELETE FROM calendar_event_relation_backlog WHERE calendar_id = @CalendarId AND parent_external_id = @ParentExternalId AND child_external_id = @ChildExternalId",
                    new
                    {
                        CalendarId = calendarId,
                        ParentExternalId = parentExternalId,
                        ChildExternalId = childExternalId
                    },
                    commandTimeout: 30
                );
            }
        }
    }

    public async Task<string?> GetParentEventIdAsync(string childEventId)
    {
        return await _connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT parent_event_id FROM calendar_event_relation WHERE child_event_id = @ChildEventId",
            new { ChildEventId = childEventId },
            commandTimeout: 30
        );
    }

    public async Task<IEnumerable<(string EventId, string RawData)>> GetOverridesAsync(string parentEventId)
    {
        return await _connection.QueryAsync<(string EventId, string RawData)>(
            """
            SELECT e.event_id, e.data ->> '$.rawData' as RawData
            FROM calendar_event e
            JOIN calendar_event_relation r ON e.event_id = r.child_event_id
            WHERE r.parent_event_id = @ParentEventId
            """,
            new { ParentEventId = parentEventId },
            commandTimeout: 30
        );
    }

    public async Task<string?> GetEventExternalIdAsync(string eventId)
    {
        return await _connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT external_id FROM calendar_event WHERE event_id = @EventId",
            new { EventId = eventId },
            commandTimeout: 30
        );
    }

    #endregion

    #region Settings

    public async Task<string?> GetSettingAsync(string key)
    {
        return await _connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT value FROM setting WHERE key = @Key",
            new { Key = key },
            commandTimeout: 30
        );
    }

    public async Task<string> GetSettingAsync(string key, string defaultValue)
    {
        var value = await GetSettingAsync(key);
        return value ?? defaultValue;
    }

    public async Task SetSettingAsync(string key, string value)
    {
        await _connection.ExecuteAsync(
            "INSERT INTO setting (key, value) VALUES (@Key, @Value) ON CONFLICT(key) DO UPDATE SET value = @Value",
            new { Key = key, Value = value },
            commandTimeout: 30
        );
    }

    public async Task<bool> GetSettingBoolAsync(string key, bool defaultValue)
    {
        var value = await GetSettingAsync(key);
        if (value == null) return defaultValue;
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetSettingBoolAsync(string key, bool value)
    {
        await SetSettingAsync(key, value ? "1" : "0");
    }

    public async Task<int> GetSettingIntAsync(string key, int defaultValue)
    {
        var value = await GetSettingAsync(key);
        if (value == null) return defaultValue;
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    public async Task SetSettingIntAsync(string key, int value)
    {
        await SetSettingAsync(key, value.ToString());
    }

    #endregion

    public async Task<IEnumerable<CalendarEventQueryResult>> GetEventsByTimeRangeAsync(Interval interval)
    {
        var startTimestamp = interval.Start.ToUnixTimeSeconds();
        var endTimestamp = interval.End.ToUnixTimeSeconds();

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

        return await _connection.QueryAsync<CalendarEventQueryResult>(
            query,
            new { StartTimestamp = startTimestamp, EndTimestamp = endTimestamp },
            commandTimeout: 30
        );
    }

    #region Reminders

    public async Task<List<ReminderDbo>> GetRemindersByEventAsync(string eventId)
    {
        return (await _connection.QueryAsync<ReminderDbo>(
            "SELECT reminder_id AS ReminderId, target_type AS TargetType, target_id AS TargetId, " +
            "target_time AS TargetTime, trigger_time AS TriggerTime " +
            "FROM reminder WHERE target_type = @TargetType AND target_id = @TargetId",
            new { TargetType = (int)TargetType.CalendarEvent, TargetId = eventId },
            commandTimeout: 30
        )).ToList();
    }

    public Task CreateReminderAsync(string eventId, DateTime occurrenceTime, DateTime triggerTime)
    {
        return CreateReminderAsync(Guid.NewGuid().ToString(), eventId, occurrenceTime, triggerTime);
    }

    public async Task CreateReminderAsync(string reminderId, string eventId, DateTime occurrenceTime, DateTime triggerTime)
    {
        await _connection.ExecuteAsync(
            "INSERT INTO reminder (reminder_id, target_type, target_id, target_time, trigger_time) " +
            "VALUES (@ReminderId, @TargetType, @TargetId, @TargetTime, @TriggerTime)",
            new
            {
                ReminderId = reminderId,
                TargetType = (int)TargetType.CalendarEvent,
                TargetId = eventId,
                TargetTime = new DateTimeOffset(occurrenceTime).ToUnixTimeSeconds(),
                TriggerTime = new DateTimeOffset(triggerTime).ToUnixTimeSeconds()
            },
            commandTimeout: 30
        );
    }

    public async Task<List<ReminderWithEvent>> GetDueRemindersAsync(HashSet<string> firedReminderIds, long? referenceTime = null)
    {
        var now = referenceTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var firedReminderIdsList = firedReminderIds.ToList();

        var query = @"
            SELECT
                r.reminder_id AS ReminderId,
                r.target_type AS TargetType,
                r.target_id AS TargetId,
                r.target_time AS TargetTime,
                r.trigger_time AS TriggerTime,
                ce.title AS EventTitle,
                c.name AS CalendarName,
                c.color AS CalendarColor,
                r.target_time AS StartTime,
                a.type AS AccountType
            FROM reminder r
            INNER JOIN calendar_event ce ON r.target_id = ce.event_id
            INNER JOIN calendar c ON ce.calendar_id = c.calendar_id
            INNER JOIN account a ON c.account_id = a.account_id
            WHERE r.target_type = @TargetType
              AND r.trigger_time <= @Now
              AND r.reminder_id NOT IN @FiredReminderIds
            ORDER BY r.trigger_time";

        return (await _connection.QueryAsync<ReminderWithEvent>(
            query,
            new { TargetType = (int)TargetType.CalendarEvent, Now = now, FiredReminderIds = firedReminderIdsList },
            commandTimeout: 30
        )).ToList();
    }

    public async Task<List<ReminderWithEvent>> GetRemindersWithEventsAsync(IReadOnlyCollection<string> reminderIds)
    {
        if (reminderIds.Count == 0)
        {
            return [];
        }

        var query = @"
            SELECT
                r.reminder_id AS ReminderId,
                r.target_type AS TargetType,
                r.target_id AS TargetId,
                r.target_time AS TargetTime,
                r.trigger_time AS TriggerTime,
                ce.title AS EventTitle,
                c.name AS CalendarName,
                c.color AS CalendarColor,
                r.target_time AS StartTime,
                a.type AS AccountType
            FROM reminder r
            INNER JOIN calendar_event ce ON r.target_id = ce.event_id
            INNER JOIN calendar c ON ce.calendar_id = c.calendar_id
            INNER JOIN account a ON c.account_id = a.account_id
            WHERE r.reminder_id IN @ReminderIds";

        return (await _connection.QueryAsync<ReminderWithEvent>(
            query,
            new { ReminderIds = reminderIds },
            commandTimeout: 30
        )).ToList();
    }

    public async Task<ReminderDbo?> GetReminderAsync(string reminderId)
    {
        return await _connection.QuerySingleOrDefaultAsync<ReminderDbo>(
            "SELECT reminder_id AS ReminderId, target_type AS TargetType, target_id AS TargetId, " +
            "target_time AS TargetTime, trigger_time AS TriggerTime " +
            "FROM reminder WHERE reminder_id = @ReminderId",
            new { ReminderId = reminderId },
            commandTimeout: 30
        );
    }

    public async Task<string?> GetEventCalendarIdAsync(string eventId)
    {
        return await _connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT calendar_id FROM calendar_event WHERE event_id = @EventId",
            new { EventId = eventId },
            commandTimeout: 30
        );
    }

    public async Task<AccountType?> GetAccountTypeForCalendarAsync(string calendarId)
    {
        var typeStr = await _connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT a.type FROM account a INNER JOIN calendar c ON a.account_id = c.account_id WHERE c.calendar_id = @CalendarId",
            new { CalendarId = calendarId },
            commandTimeout: 30
        );

        if (string.IsNullOrEmpty(typeStr))
        {
            return null;
        }

        return Enum.TryParse<AccountType>(typeStr, out var accountType) ? accountType : null;
    }

    public async Task DeleteRemindersAsync(List<string> reminderIds)
    {
        if (reminderIds.Count == 0)
        {
            return;
        }

        await _connection.ExecuteAsync(
            "DELETE FROM reminder WHERE reminder_id IN @ReminderIds",
            new { ReminderIds = reminderIds },
            commandTimeout: 30
        );
    }

    public async Task DeleteReminderAsync(string reminderId)
    {
        await _connection.ExecuteAsync(
            "DELETE FROM reminder WHERE reminder_id = @ReminderId",
            new { ReminderId = reminderId },
            commandTimeout: 30
        );
    }

    public async Task<int> DeleteAllRemindersAsync()
    {
        return await _connection.ExecuteAsync(
            "DELETE FROM reminder",
            commandTimeout: 30
        );
    }

    #endregion

    #region Cache Management

    public Account? GetCachedAccount(Guid accountId)
    {
        EnsureCacheInitializedAsync();
        return _accountCache.TryGetValue(accountId, out var account) ? account : null;
    }

    public IEnumerable<Account> GetCachedAccounts()
    {
        EnsureCacheInitializedAsync();
        return _accountCache.Values.OrderBy(account => account.SortOrder);
    }



    private void EnsureCacheInitializedAsync()
    {
        if (_cacheInitialized)
        {
            return;
        }

        lock (_cacheLock)
        {
            if (_cacheInitialized)
            {
                return;
            }

            // Load cache synchronously to avoid race conditions
            var loadTask = LoadCacheAsync();
            loadTask.GetAwaiter().GetResult();
            _cacheInitialized = true;
        }
    }

    private async Task LoadCacheAsync()
    {
        var accountDbos = await _connection.QueryAsync<AccountDbo>(
            "SELECT account_id AS AccountId, name AS Name, type AS Type, capabilities AS Capabilities, sort_order AS SortOrder FROM account",
            commandTimeout: 30
        );

        foreach (var accountDbo in accountDbos)
        {
            if (!Enum.TryParse<AccountType>(accountDbo.Type, ignoreCase: true, out var accountType))
            {
                continue;
            }

            var capabilities = accountDbo.Capabilities != 0
                ? (AccountCapability)accountDbo.Capabilities
                : AccountDbo.GetDefaultCapabilities(accountType);

            var account = new Account
            {
                Id = Guid.Parse(accountDbo.AccountId),
                Name = accountDbo.Name,
                Type = accountType,
                Capabilities = capabilities,
                SortOrder = accountDbo.SortOrder,
            };
            _accountCache[account.Id] = account;
        }
    }

    private void InvalidateAccountCache(Guid accountId)
    {
        _accountCache.TryRemove(accountId, out _);
    }

    private void ClearCache()
    {
        _accountCache.Clear();
        _cacheInitialized = false;
    }

    /// <summary>
    /// Searches contacts by name or email prefix for autocomplete.
    /// </summary>
    /// <param name="query">Search query (name or email prefix)</param>
    /// <param name="limit">Maximum results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching contacts</returns>
    public async Task<IEnumerable<ContactQueryResult>> SearchContactsAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return await _connection.QueryAsync<ContactQueryResult>(
            """
            SELECT 
                c.contact_id AS ContactId,
                c.external_id AS ExternalId,
                c.display_name AS DisplayName,
                c.given_name AS GivenName,
                c.family_name AS FamilyName,
                c.primary_email AS PrimaryEmail,
                c.primary_phone AS PrimaryPhone,
                c.photo_url AS PhotoUrl,
                c.changed_at AS ChangedAt,
                c.data ->> '$.rawData' AS RawData,
                ab.address_book_id AS AddressBookId,
                ab.external_id AS AddressBookExternalId,
                ab.name AS AddressBookName,
                ab.enabled AS AddressBookEnabled,
                ab.last_sync AS AddressBookLastSync,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType
            FROM contact c
            INNER JOIN address_book ab ON c.address_book_id = ab.address_book_id
            INNER JOIN account a ON ab.account_id = a.account_id
            WHERE ab.enabled = 1
                AND (
                    c.display_name LIKE @Query || '%'
                    OR c.primary_email LIKE @Query || '%'
                    OR c.given_name LIKE @Query || '%'
                    OR c.family_name LIKE @Query || '%'
                )
            ORDER BY a.sort_order, a.name, ab.name, c.display_name
            LIMIT @Limit
            """,
            new { Query = query, Limit = limit },
            commandTimeout: 30
        );
    }

    #endregion

    #region Address Book Methods

    public async Task<IEnumerable<AddressBookDbo>> GetAddressBooksByAccountAsync(string accountId)
    {
        return await _connection.QueryAsync<AddressBookDbo>(
            "SELECT account_id AS AccountId, address_book_id AS AddressBookId, external_id AS ExternalId, " +
            "name AS Name, enabled AS Enabled, last_sync AS LastSync " +
            "FROM address_book WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    public async Task<AddressBookDbo?> GetAddressBookByExternalIdAsync(string accountId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<AddressBookDbo>(
            "SELECT account_id AS AccountId, address_book_id AS AddressBookId, external_id AS ExternalId, " +
            "name AS Name, enabled AS Enabled, last_sync AS LastSync " +
            "FROM address_book WHERE account_id = @AccountId AND external_id = @ExternalId",
            new { AccountId = accountId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<AddressBookDbo?> GetAddressBookByIdAsync(string addressBookId)
    {
        return await _connection.QuerySingleOrDefaultAsync<AddressBookDbo>(
            "SELECT account_id AS AccountId, address_book_id AS AddressBookId, external_id AS ExternalId, " +
            "name AS Name, enabled AS Enabled, last_sync AS LastSync " +
            "FROM address_book WHERE address_book_id = @AddressBookId",
            new { AddressBookId = addressBookId },
            commandTimeout: 30
        );
    }

    public async Task<bool> CreateOrUpdateAddressBookAsync(AddressBookDbo addressBook)
    {
        var existing =
            await GetAddressBookByExternalIdAsync(addressBook.AccountId, addressBook.ExternalId ?? string.Empty);

        if (existing != null)
        {
            var rowsAffected = await _connection.ExecuteAsync(
                "UPDATE address_book SET name = @Name, enabled = @Enabled, last_sync = @LastSync " +
                "WHERE account_id = @AccountId AND external_id = @ExternalId",
                new
                {
                    addressBook.Name,
                    addressBook.Enabled,
                    addressBook.LastSync,
                    addressBook.AccountId,
                    addressBook.ExternalId
                },
                commandTimeout: 30
            );

            addressBook.AddressBookId = existing.AddressBookId;
            return rowsAffected > 0;
        }
        else
        {
            var addressBookId = Guid.NewGuid().ToString();
            var rowsAffected = await _connection.ExecuteAsync(
                "INSERT INTO address_book (account_id, address_book_id, external_id, name, enabled, last_sync) " +
                "VALUES (@AccountId, @AddressBookId, @ExternalId, @Name, @Enabled, @LastSync)",
                new
                {
                    addressBook.AccountId,
                    AddressBookId = addressBookId,
                    addressBook.ExternalId,
                    addressBook.Name,
                    addressBook.Enabled,
                    addressBook.LastSync
                },
                commandTimeout: 30
            );

            addressBook.AddressBookId = addressBookId;
            return rowsAffected > 0;
        }
    }

    public async Task<bool> DeleteAddressBookAsync(string addressBookId)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM address_book WHERE address_book_id = @AddressBookId",
            new { AddressBookId = addressBookId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<int> DeleteAddressBooksNotSyncedAsync(string accountId, long currentSyncTime)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM address_book WHERE account_id = @AccountId AND last_sync < @CurrentSyncTime",
            new { AccountId = accountId, CurrentSyncTime = currentSyncTime },
            commandTimeout: 30
        );

        return rowsAffected;
    }

    public async Task<bool> SetAddressBookDataAsync(string addressBookId, string key, string value)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
                UPDATE address_book
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, @value)
                WHERE address_book_id = @address_book_id
            """,
            param: new { key = $"$.{key}", value, address_book_id = addressBookId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<string?> GetAddressBookDataAsync(string addressBookId, string key)
    {
        return await _connection.QuerySingleAsync<string?>(
            """
            SELECT coalesce(data ->> @key, '') as value
            FROM address_book
            WHERE address_book_id = @address_book_id
            """,
            param: new { key = $"$.{key}", address_book_id = addressBookId });
    }

    public async Task<bool> UpdateAddressBookEnabledAsync(string addressBookId, bool enabled)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "UPDATE address_book SET enabled = @Enabled WHERE address_book_id = @AddressBookId",
            new { AddressBookId = addressBookId, Enabled = enabled ? 1 : 0 },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    /// <summary>
    /// Gets all address books with their account information
    /// </summary>
    public async Task<IEnumerable<AddressBookQueryResult>> GetAllAddressBooksAsync()
    {
        return await _connection.QueryAsync<AddressBookQueryResult>(
            """
            SELECT 
                ab.address_book_id AS AddressBookId,
                ab.external_id AS ExternalId,
                ab.name AS Name,
                ab.enabled AS Enabled,
                ab.last_sync AS LastSync,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType,
                a.sort_order AS AccountSortOrder,
                (SELECT COUNT(*) FROM contact c WHERE c.address_book_id = ab.address_book_id) AS ContactCount
            FROM address_book ab
            INNER JOIN account a ON ab.account_id = a.account_id
            ORDER BY a.sort_order, a.name, ab.name
            """,
            commandTimeout: 30
        );
    }

    #endregion

    #region Contact Methods

    public async Task<IEnumerable<ContactDbo>> GetContactsByAddressBookAsync(string addressBookId)
    {
        return await _connection.QueryAsync<ContactDbo>(
            "SELECT address_book_id AS AddressBookId, contact_id AS ContactId, external_id AS ExternalId, " +
            "display_name AS DisplayName, given_name AS GivenName, family_name AS FamilyName, " +
            "primary_email AS PrimaryEmail, primary_phone AS PrimaryPhone, photo_url AS PhotoUrl, " +
            "changed_at AS ChangedAt " +
            "FROM contact WHERE address_book_id = @AddressBookId",
            new { AddressBookId = addressBookId },
            commandTimeout: 30
        );
    }

    public async Task<ContactDbo?> GetContactByExternalIdAsync(string addressBookId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<ContactDbo>(
            "SELECT address_book_id AS AddressBookId, contact_id AS ContactId, external_id AS ExternalId, " +
            "display_name AS DisplayName, given_name AS GivenName, family_name AS FamilyName, " +
            "primary_email AS PrimaryEmail, primary_phone AS PrimaryPhone, photo_url AS PhotoUrl, " +
            "changed_at AS ChangedAt " +
            "FROM contact WHERE address_book_id = @AddressBookId AND external_id = @ExternalId",
            new { AddressBookId = addressBookId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<ContactDbo?> GetContactByIdAsync(string contactId)
    {
        return await _connection.QuerySingleOrDefaultAsync<ContactDbo>(
            "SELECT address_book_id AS AddressBookId, contact_id AS ContactId, external_id AS ExternalId, " +
            "display_name AS DisplayName, given_name AS GivenName, family_name AS FamilyName, " +
            "primary_email AS PrimaryEmail, primary_phone AS PrimaryPhone, photo_url AS PhotoUrl, " +
            "changed_at AS ChangedAt " +
            "FROM contact WHERE contact_id = @ContactId",
            new { ContactId = contactId },
            commandTimeout: 30
        );
    }

    public async Task<string?> GetContactPhotoUrlAsync(string addressBookId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT photo_url AS PhotoUrl " +
            "FROM contact WHERE address_book_id = @AddressBookId AND external_id = @ExternalId",
            new { AddressBookId = addressBookId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<string> CreateOrUpdateContactAsync(ContactDbo contactDbo)
    {
        var existing =
            await GetContactByExternalIdAsync(contactDbo.AddressBookId, contactDbo.ExternalId ?? string.Empty);

        if (existing != null)
        {
            await _connection.ExecuteAsync(
                "UPDATE contact SET display_name = @display_name, given_name = @given_name, " +
                "family_name = @family_name, primary_email = @primary_email, primary_phone = @primary_phone, " +
                "photo_url = @photo_url, changed_at = @changed_at " +
                "WHERE address_book_id = @address_book_id AND external_id = @external_id",
                new
                {
                    address_book_id = contactDbo.AddressBookId,
                    external_id = contactDbo.ExternalId,
                    display_name = contactDbo.DisplayName,
                    given_name = contactDbo.GivenName,
                    family_name = contactDbo.FamilyName,
                    primary_email = contactDbo.PrimaryEmail,
                    primary_phone = contactDbo.PrimaryPhone,
                    photo_url = contactDbo.PhotoUrl,
                    changed_at = contactDbo.ChangedAt
                },
                commandTimeout: 30
            );

            return existing.ContactId;
        }
        else
        {
            var newContactId = Guid.NewGuid().ToString();
            await _connection.ExecuteAsync(
                "INSERT INTO contact (address_book_id, contact_id, external_id, display_name, given_name, " +
                "family_name, primary_email, primary_phone, photo_url, changed_at) " +
                "VALUES (@address_book_id, @contact_id, @external_id, @display_name, @given_name, " +
                "@family_name, @primary_email, @primary_phone, @photo_url, @changed_at)",
                new
                {
                    address_book_id = contactDbo.AddressBookId,
                    contact_id = newContactId,
                    external_id = contactDbo.ExternalId,
                    display_name = contactDbo.DisplayName,
                    given_name = contactDbo.GivenName,
                    family_name = contactDbo.FamilyName,
                    primary_email = contactDbo.PrimaryEmail,
                    primary_phone = contactDbo.PrimaryPhone,
                    photo_url = contactDbo.PhotoUrl,
                    changed_at = contactDbo.ChangedAt
                },
                commandTimeout: 30
            );

            return newContactId;
        }
    }

    public async Task<int> DeleteContactsNotSyncedAsync(string addressBookId, long currentSyncTime)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM contact WHERE address_book_id = @AddressBookId AND changed_at < @CurrentSyncTime",
            new { AddressBookId = addressBookId, CurrentSyncTime = currentSyncTime },
            commandTimeout: 30
        );

        return rowsAffected;
    }

    public async Task<bool> DeleteContactAsync(string contactId)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM contact WHERE contact_id = @ContactId",
            new { ContactId = contactId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<bool> SetContactDataAsync(string contactId, string key, string value)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
                UPDATE contact
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, @value)
                WHERE contact_id = @contactId
            """,
            param: new { key = $"$.{key}", value, contactId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<bool> SetContactDataJsonAsync(string contactId, string key, string jsonValue)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
                UPDATE contact
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, jsonb(@jsonValue))
                WHERE contact_id = @contactId
            """,
            param: new { key = $"$.{key}", jsonValue, contactId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<string?> GetContactDataAsync(string contactId, string key)
    {
        return await _connection.QuerySingleAsync<string?>(
            """
            SELECT coalesce(data ->> @key, '') as value
            FROM contact
            WHERE contact_id = @contactId
            """,
            param: new { key = $"$.{key}", contactId });
    }

    /// <summary>
    /// Finds a contact by email address (case-insensitive)
    /// </summary>
    public async Task<ContactQueryResult?> GetContactByEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        return await _connection.QueryFirstOrDefaultAsync<ContactQueryResult>(
            """
            SELECT 
                c.contact_id AS ContactId,
                c.external_id AS ExternalId,
                c.display_name AS DisplayName,
                c.given_name AS GivenName,
                c.family_name AS FamilyName,
                c.primary_email AS PrimaryEmail,
                c.primary_phone AS PrimaryPhone,
                c.photo_url AS PhotoUrl,
                c.changed_at AS ChangedAt,
                c.data ->> '$.rawData' AS RawData,
                ab.address_book_id AS AddressBookId,
                ab.external_id AS AddressBookExternalId,
                ab.name AS AddressBookName,
                ab.enabled AS AddressBookEnabled,
                ab.last_sync AS AddressBookLastSync,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType
            FROM contact c
            INNER JOIN address_book ab ON c.address_book_id = ab.address_book_id
            INNER JOIN account a ON ab.account_id = a.account_id
            WHERE c.primary_email = @Email COLLATE NOCASE
            LIMIT 1
            """,
            new { Email = email },
            commandTimeout: 30
        );
    }

    /// <summary>
    /// Gets all contacts with their address book and account information
    /// </summary>
    public async Task<IEnumerable<ContactQueryResult>> GetAllContactsAsync()
    {
        return await _connection.QueryAsync<ContactQueryResult>(
            """
            SELECT 
                c.contact_id AS ContactId,
                c.external_id AS ExternalId,
                c.display_name AS DisplayName,
                c.given_name AS GivenName,
                c.family_name AS FamilyName,
                c.primary_email AS PrimaryEmail,
                c.primary_phone AS PrimaryPhone,
                c.photo_url AS PhotoUrl,
                c.changed_at AS ChangedAt,
                c.data ->> '$.rawData' AS RawData,
                ab.address_book_id AS AddressBookId,
                ab.external_id AS AddressBookExternalId,
                ab.name AS AddressBookName,
                ab.enabled AS AddressBookEnabled,
                ab.last_sync AS AddressBookLastSync,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType
            FROM contact c
            INNER JOIN address_book ab ON c.address_book_id = ab.address_book_id
            INNER JOIN account a ON ab.account_id = a.account_id
            WHERE ab.enabled = 1
            ORDER BY a.sort_order, a.name, ab.name, c.display_name
            """,
            commandTimeout: 30
        );
    }

    /// <summary>
    /// Gets contacts for a specific account with address book information
    /// </summary>
    public async Task<IEnumerable<ContactQueryResult>> GetContactsByAccountAsync(string accountId)
    {
        return await _connection.QueryAsync<ContactQueryResult>(
            """
            SELECT 
                c.contact_id AS ContactId,
                c.external_id AS ExternalId,
                c.display_name AS DisplayName,
                c.given_name AS GivenName,
                c.family_name AS FamilyName,
                c.primary_email AS PrimaryEmail,
                c.primary_phone AS PrimaryPhone,
                c.photo_url AS PhotoUrl,
                c.changed_at AS ChangedAt,
                c.data ->> '$.rawData' AS RawData,
                ab.address_book_id AS AddressBookId,
                ab.external_id AS AddressBookExternalId,
                ab.name AS AddressBookName,
                ab.enabled AS AddressBookEnabled,
                ab.last_sync AS AddressBookLastSync,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType
            FROM contact c
            INNER JOIN address_book ab ON c.address_book_id = ab.address_book_id
            INNER JOIN account a ON ab.account_id = a.account_id
            WHERE a.account_id = @AccountId AND ab.enabled = 1
            ORDER BY ab.name, c.display_name
            """,
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    public async Task<Contact?> GetHydratedContactByIdAsync(string contactId)
    {
        var result = await _connection.QuerySingleOrDefaultAsync<ContactQueryResult>(
            """
            SELECT
                c.contact_id AS ContactId,
                c.external_id AS ExternalId,
                c.display_name AS DisplayName,
                c.given_name AS GivenName,
                c.family_name AS FamilyName,
                c.primary_email AS PrimaryEmail,
                c.primary_phone AS PrimaryPhone,
                c.photo_url AS PhotoUrl,
                c.changed_at AS ChangedAt,
                c.data ->> '$.rawData' AS RawData,
                ab.address_book_id AS AddressBookId,
                ab.external_id AS AddressBookExternalId,
                ab.name AS AddressBookName,
                ab.enabled AS AddressBookEnabled,
                ab.last_sync AS AddressBookLastSync,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType
            FROM contact c
            INNER JOIN address_book ab ON c.address_book_id = ab.address_book_id
            INNER JOIN account a ON ab.account_id = a.account_id
            WHERE c.contact_id = @ContactId
            LIMIT 1
            """,
            new { ContactId = contactId },
            commandTimeout: 30);

        return result == null ? null : HydrateContact(result);
    }

    public async Task<IEnumerable<Contact>> GetHydratedContactsByAccountAsync(string accountId)
    {
        var contacts = await GetContactsByAccountAsync(accountId);
        return contacts.Select(HydrateContact);
    }

    public async Task<IEnumerable<Contact>> GetAllHydratedContactsAsync()
    {
        var contacts = await GetAllContactsAsync();
        return contacts.Select(HydrateContact);
    }

    private Contact HydrateContact(ContactQueryResult contact)
    {
        var addressBook = HydrateAddressBook(contact);
        return new Contact
        {
            Reference = new ContactReference
            {
                AddressBook = addressBook,
                Id = Guid.Parse(contact.ContactId),
                ExternalId = contact.ExternalId,
            },
            DisplayName = contact.DisplayName,
            GivenName = contact.GivenName,
            FamilyName = contact.FamilyName,
            PrimaryEmail = contact.PrimaryEmail,
            PrimaryPhone = contact.PrimaryPhone,
            PhotoUrl = contact.PhotoUrl,
            ChangedAt = contact.ChangedAt == null
                ? null
                : DateTimeOffset.FromUnixTimeSeconds(contact.ChangedAt.Value).UtcDateTime,
        };
    }

    private AddressBook HydrateAddressBook(ContactQueryResult contact)
    {
        var accountId = Guid.Parse(contact.AccountId);
        var account = GetCachedAccount(accountId) ?? new Account
        {
            Id = accountId,
            Name = contact.AccountName,
            Type = contact.AccountTypeEnum,
        };

        var addressBook = new AddressBook
        {
            Account = account,
            Id = Guid.Parse(contact.AddressBookId),
            ExternalId = contact.AddressBookExternalId,
            Name = contact.AddressBookName,
            Enabled = contact.AddressBookEnabled != 0,
            LastSync = contact.AddressBookLastSync == null
                ? null
                : DateTimeOffset.FromUnixTimeSeconds(contact.AddressBookLastSync.Value).UtcDateTime,
        };

        return addressBook;
    }

    #endregion

    #region Contact Group Methods

    /// <summary>
    /// Gets all contact groups with their account information and member counts
    /// </summary>
    public async Task<IEnumerable<ContactGroupQueryResult>> GetAllContactGroupsAsync()
    {
        return await _connection.QueryAsync<ContactGroupQueryResult>(
            """
            SELECT 
                cg.group_id AS GroupId,
                cg.external_id AS ExternalId,
                cg.name AS Name,
                cg.system_group AS SystemGroup,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType,
                a.sort_order AS AccountSortOrder,
                (SELECT COUNT(*) FROM contact_group_membership cgm WHERE cgm.group_id = cg.group_id) AS MemberCount
            FROM contact_group cg
            INNER JOIN account a ON cg.account_id = a.account_id
            ORDER BY a.sort_order, a.name, cg.system_group DESC, cg.name
            """,
            commandTimeout: 30
        );
    }

    /// <summary>
    /// Gets all contact IDs that belong to a specific group
    /// </summary>
    public async Task<IEnumerable<string>> GetContactIdsByGroupAsync(string groupId)
    {
        return await _connection.QueryAsync<string>(
            "SELECT contact_id FROM contact_group_membership WHERE group_id = @GroupId",
            new { GroupId = groupId },
            commandTimeout: 30
        );
    }

    public async Task<IEnumerable<ContactGroupDbo>> GetContactGroupsByAccountAsync(string accountId)
    {
        return await _connection.QueryAsync<ContactGroupDbo>(
            "SELECT account_id AS AccountId, group_id AS GroupId, external_id AS ExternalId, " +
            "name AS Name, system_group AS SystemGroup " +
            "FROM contact_group WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    public async Task<ContactGroupDbo?> GetContactGroupByExternalIdAsync(string accountId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<ContactGroupDbo>(
            "SELECT account_id AS AccountId, group_id AS GroupId, external_id AS ExternalId, " +
            "name AS Name, system_group AS SystemGroup " +
            "FROM contact_group WHERE account_id = @AccountId AND external_id = @ExternalId",
            new { AccountId = accountId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<bool> CreateOrUpdateContactGroupAsync(ContactGroupDbo groupDbo)
    {
        var existing = await GetContactGroupByExternalIdAsync(groupDbo.AccountId, groupDbo.ExternalId ?? string.Empty);

        if (existing != null)
        {
            var rowsAffected = await _connection.ExecuteAsync(
                "UPDATE contact_group SET name = @Name, system_group = @SystemGroup " +
                "WHERE account_id = @AccountId AND external_id = @ExternalId",
                new
                {
                    groupDbo.Name,
                    groupDbo.SystemGroup,
                    groupDbo.AccountId,
                    groupDbo.ExternalId
                },
                commandTimeout: 30
            );

            groupDbo.GroupId = existing.GroupId;
            return rowsAffected > 0;
        }
        else
        {
            var groupId = Guid.NewGuid().ToString();
            var rowsAffected = await _connection.ExecuteAsync(
                "INSERT INTO contact_group (account_id, group_id, external_id, name, system_group) " +
                "VALUES (@AccountId, @GroupId, @ExternalId, @Name, @SystemGroup)",
                new
                {
                    groupDbo.AccountId,
                    GroupId = groupId,
                    groupDbo.ExternalId,
                    groupDbo.Name,
                    groupDbo.SystemGroup
                },
                commandTimeout: 30
            );

            groupDbo.GroupId = groupId;
            return rowsAffected > 0;
        }
    }

    public async Task<bool> DeleteContactGroupAsync(string groupId)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM contact_group WHERE group_id = @GroupId",
            new { GroupId = groupId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task SetContactGroupMembershipAsync(string contactId, IEnumerable<string> groupIds)
    {
        // Clear existing memberships
        await _connection.ExecuteAsync(
            "DELETE FROM contact_group_membership WHERE contact_id = @ContactId",
            new { ContactId = contactId },
            commandTimeout: 30
        );

        // Add new memberships
        foreach (var groupId in groupIds)
        {
            await _connection.ExecuteAsync(
                "INSERT OR IGNORE INTO contact_group_membership (contact_id, group_id) VALUES (@ContactId, @GroupId)",
                new { ContactId = contactId, GroupId = groupId },
                commandTimeout: 30
            );
        }
    }

    public async Task<IEnumerable<string>> GetContactGroupMembershipsAsync(string contactId)
    {
        return await _connection.QueryAsync<string>(
            "SELECT group_id FROM contact_group_membership WHERE contact_id = @ContactId",
            new { ContactId = contactId },
            commandTimeout: 30
        );
    }

    public async Task<string?> GetContactGroupIdByExternalIdAsync(string accountId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT group_id FROM contact_group WHERE account_id = @AccountId AND external_id = @ExternalId",
            new { AccountId = accountId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    /// <summary>
    /// Clears all contact sync data for an account, preparing it for a full resync.
    /// Deletes all address books (and their contacts via cascade) and clears sync tokens.
    /// </summary>
    public async Task ClearAccountContactSyncDataAsync(string accountId)
    {
        await _connection.ExecuteAsync(
            "DELETE FROM address_book WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );

        await _connection.ExecuteAsync(
            "DELETE FROM contact_group WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );

        await _connection.ExecuteAsync(
            "UPDATE account SET data = jsonb_remove(coalesce(data, jsonb_object()), '$.addressBookSyncToken') WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );

        await _connection.ExecuteAsync(
            "UPDATE account SET data = jsonb_remove(coalesce(data, jsonb_object()), '$.contactGroupSyncToken') WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    #endregion

    #region Mailbox Methods

    public async Task<IEnumerable<MailboxDbo>> GetMailboxesByAccountAsync(string accountId)
    {
        return await _connection.QueryAsync<MailboxDbo>(
            "SELECT account_id AS AccountId, mailbox_id AS MailboxId, external_id AS ExternalId, " +
            "parent_external_id AS ParentExternalId, name AS Name, role AS Role, unread_count AS UnreadCount, " +
            "total_count AS TotalCount, enabled AS Enabled, last_sync AS LastSync " +
            "FROM mailbox WHERE account_id = @AccountId ORDER BY name",
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    public async Task<MailboxDbo?> GetMailboxByExternalIdAsync(string accountId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<MailboxDbo>(
            "SELECT account_id AS AccountId, mailbox_id AS MailboxId, external_id AS ExternalId, " +
            "parent_external_id AS ParentExternalId, name AS Name, role AS Role, unread_count AS UnreadCount, " +
            "total_count AS TotalCount, enabled AS Enabled, last_sync AS LastSync " +
            "FROM mailbox WHERE account_id = @AccountId AND external_id = @ExternalId",
            new { AccountId = accountId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<MailboxDbo?> GetMailboxByIdAsync(string mailboxId)
    {
        return await _connection.QuerySingleOrDefaultAsync<MailboxDbo>(
            "SELECT account_id AS AccountId, mailbox_id AS MailboxId, external_id AS ExternalId, " +
            "parent_external_id AS ParentExternalId, name AS Name, role AS Role, unread_count AS UnreadCount, " +
            "total_count AS TotalCount, enabled AS Enabled, last_sync AS LastSync " +
            "FROM mailbox WHERE mailbox_id = @MailboxId",
            new { MailboxId = mailboxId },
            commandTimeout: 30
        );
    }

    public async Task<bool> CreateOrUpdateMailboxAsync(MailboxDbo mailbox)
    {
        var existing = !string.IsNullOrWhiteSpace(mailbox.ExternalId)
            ? await GetMailboxByExternalIdAsync(mailbox.AccountId, mailbox.ExternalId)
            : !string.IsNullOrWhiteSpace(mailbox.MailboxId)
                ? await GetMailboxByIdAsync(mailbox.MailboxId)
                : null;

        if (existing != null)
        {
            var rowsAffected = await _connection.ExecuteAsync(
                "UPDATE mailbox SET external_id = @ExternalId, parent_external_id = @ParentExternalId, " +
                "name = @Name, role = @Role, unread_count = @UnreadCount, total_count = @TotalCount, " +
                "enabled = @Enabled, last_sync = @LastSync WHERE mailbox_id = @MailboxId",
                new
                {
                    MailboxId = existing.MailboxId,
                    mailbox.ExternalId,
                    mailbox.ParentExternalId,
                    mailbox.Name,
                    mailbox.Role,
                    mailbox.UnreadCount,
                    mailbox.TotalCount,
                    mailbox.Enabled,
                    mailbox.LastSync
                },
                commandTimeout: 30
            );

            mailbox.MailboxId = existing.MailboxId;
            return rowsAffected > 0;
        }

        var mailboxId = !string.IsNullOrWhiteSpace(mailbox.MailboxId)
            ? mailbox.MailboxId
            : Guid.NewGuid().ToString();

        var inserted = await _connection.ExecuteAsync(
            "INSERT INTO mailbox (account_id, mailbox_id, external_id, parent_external_id, name, role, unread_count, total_count, enabled, last_sync) " +
            "VALUES (@AccountId, @MailboxId, @ExternalId, @ParentExternalId, @Name, @Role, @UnreadCount, @TotalCount, @Enabled, @LastSync)",
            new
            {
                mailbox.AccountId,
                MailboxId = mailboxId,
                mailbox.ExternalId,
                mailbox.ParentExternalId,
                mailbox.Name,
                mailbox.Role,
                mailbox.UnreadCount,
                mailbox.TotalCount,
                mailbox.Enabled,
                mailbox.LastSync
            },
            commandTimeout: 30
        );

        mailbox.MailboxId = mailboxId;
        return inserted > 0;
    }

    public async Task<bool> DeleteMailboxAsync(string mailboxId)
    {
        using var transaction = _connection.BeginTransaction();

        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM mailbox WHERE mailbox_id = @MailboxId",
            new { MailboxId = mailboxId },
            transaction: transaction,
            commandTimeout: 30
        );

        await CleanupOrphanMailDataAsync(transaction);
        transaction.Commit();

        return rowsAffected > 0;
    }

    public async Task<int> DeleteMailboxesNotSyncedAsync(string accountId, long currentSyncTime)
    {
        using var transaction = _connection.BeginTransaction();

        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM mailbox WHERE account_id = @AccountId AND last_sync < @CurrentSyncTime",
            new { AccountId = accountId, CurrentSyncTime = currentSyncTime },
            transaction: transaction,
            commandTimeout: 30
        );

        await CleanupOrphanMailDataAsync(transaction);
        transaction.Commit();

        return rowsAffected;
    }

    public async Task<bool> SetMailboxDataAsync(string mailboxId, string key, string value)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
                UPDATE mailbox
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, @value)
                WHERE mailbox_id = @mailbox_id
            """,
            param: new { key = $"$.{key}", value, mailbox_id = mailboxId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<bool> SetMailboxDataJsonAsync(string mailboxId, string key, string jsonValue)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
                UPDATE mailbox
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, jsonb(@jsonValue))
                WHERE mailbox_id = @mailbox_id
            """,
            param: new { key = $"$.{key}", jsonValue, mailbox_id = mailboxId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<string?> GetMailboxDataAsync(string mailboxId, string key)
    {
        return await _connection.QuerySingleAsync<string?>(
            """
            SELECT coalesce(data ->> @key, '') as value
            FROM mailbox
            WHERE mailbox_id = @mailbox_id
            """,
            param: new { key = $"$.{key}", mailbox_id = mailboxId });
    }

    public async Task<bool> UpdateMailboxEnabledAsync(string mailboxId, bool enabled)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "UPDATE mailbox SET enabled = @Enabled WHERE mailbox_id = @MailboxId",
            new { MailboxId = mailboxId, Enabled = enabled ? 1 : 0 },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<IEnumerable<MailboxQueryResult>> GetAllMailboxesAsync()
    {
        return await _connection.QueryAsync<MailboxQueryResult>(
            """
            SELECT
                m.mailbox_id AS MailboxId,
                m.external_id AS ExternalId,
                m.parent_external_id AS ParentExternalId,
                m.name AS Name,
                m.role AS Role,
                m.unread_count AS UnreadCount,
                m.total_count AS TotalCount,
                m.enabled AS Enabled,
                m.last_sync AS LastSync,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType,
                a.capabilities AS AccountCapabilities,
                a.sort_order AS AccountSortOrder
            FROM mailbox m
            INNER JOIN account a ON m.account_id = a.account_id
            ORDER BY a.sort_order, a.name, m.name
            """,
            commandTimeout: 30
        );
    }

    #endregion

    #region Mail Thread Methods

    public async Task<IEnumerable<MailThreadDbo>> GetMailThreadsByAccountAsync(string accountId)
    {
        return await _connection.QueryAsync<MailThreadDbo>(
            "SELECT account_id AS AccountId, thread_id AS ThreadId, external_id AS ExternalId, " +
            "subject AS Subject, participants_summary AS ParticipantsSummary, preview AS Preview, " +
            "latest_message_received_at AS LatestMessageReceivedAt, unread_count AS UnreadCount, " +
            "message_count AS MessageCount, has_attachments AS HasAttachments " +
            "FROM mail_thread WHERE account_id = @AccountId ORDER BY latest_message_received_at DESC, thread_id",
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    public async Task<MailThreadDbo?> GetMailThreadByExternalIdAsync(string accountId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<MailThreadDbo>(
            "SELECT account_id AS AccountId, thread_id AS ThreadId, external_id AS ExternalId, " +
            "subject AS Subject, participants_summary AS ParticipantsSummary, preview AS Preview, " +
            "latest_message_received_at AS LatestMessageReceivedAt, unread_count AS UnreadCount, " +
            "message_count AS MessageCount, has_attachments AS HasAttachments " +
            "FROM mail_thread WHERE account_id = @AccountId AND external_id = @ExternalId",
            new { AccountId = accountId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<MailThreadDbo?> GetMailThreadByIdAsync(string threadId)
    {
        return await _connection.QuerySingleOrDefaultAsync<MailThreadDbo>(
            "SELECT account_id AS AccountId, thread_id AS ThreadId, external_id AS ExternalId, " +
            "subject AS Subject, participants_summary AS ParticipantsSummary, preview AS Preview, " +
            "latest_message_received_at AS LatestMessageReceivedAt, unread_count AS UnreadCount, " +
            "message_count AS MessageCount, has_attachments AS HasAttachments " +
            "FROM mail_thread WHERE thread_id = @ThreadId",
            new { ThreadId = threadId },
            commandTimeout: 30
        );
    }

    public async Task<string> CreateOrUpdateMailThreadAsync(MailThreadDbo threadDbo)
    {
        var existing = !string.IsNullOrWhiteSpace(threadDbo.ExternalId)
            ? await GetMailThreadByExternalIdAsync(threadDbo.AccountId, threadDbo.ExternalId)
            : !string.IsNullOrWhiteSpace(threadDbo.ThreadId)
                ? await GetMailThreadByIdAsync(threadDbo.ThreadId)
                : null;

        if (existing != null)
        {
            await _connection.ExecuteAsync(
                "UPDATE mail_thread SET external_id = @ExternalId, subject = @Subject, participants_summary = @ParticipantsSummary, " +
                "preview = @Preview, latest_message_received_at = @LatestMessageReceivedAt, unread_count = @UnreadCount, " +
                "message_count = @MessageCount, has_attachments = @HasAttachments WHERE thread_id = @ThreadId",
                new
                {
                    ThreadId = existing.ThreadId,
                    threadDbo.ExternalId,
                    threadDbo.Subject,
                    threadDbo.ParticipantsSummary,
                    threadDbo.Preview,
                    threadDbo.LatestMessageReceivedAt,
                    threadDbo.UnreadCount,
                    threadDbo.MessageCount,
                    threadDbo.HasAttachments
                },
                commandTimeout: 30
            );

            threadDbo.ThreadId = existing.ThreadId;
            return existing.ThreadId;
        }

        var threadId = !string.IsNullOrWhiteSpace(threadDbo.ThreadId)
            ? threadDbo.ThreadId
            : Guid.NewGuid().ToString();

        await _connection.ExecuteAsync(
            "INSERT INTO mail_thread (account_id, thread_id, external_id, subject, participants_summary, preview, latest_message_received_at, unread_count, message_count, has_attachments) " +
            "VALUES (@AccountId, @ThreadId, @ExternalId, @Subject, @ParticipantsSummary, @Preview, @LatestMessageReceivedAt, @UnreadCount, @MessageCount, @HasAttachments)",
            new
            {
                threadDbo.AccountId,
                ThreadId = threadId,
                threadDbo.ExternalId,
                threadDbo.Subject,
                threadDbo.ParticipantsSummary,
                threadDbo.Preview,
                threadDbo.LatestMessageReceivedAt,
                threadDbo.UnreadCount,
                threadDbo.MessageCount,
                threadDbo.HasAttachments
            },
            commandTimeout: 30
        );

        threadDbo.ThreadId = threadId;
        return threadId;
    }

    public async Task<bool> DeleteMailThreadAsync(string threadId)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM mail_thread WHERE thread_id = @ThreadId",
            new { ThreadId = threadId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<IEnumerable<MailThreadQueryResult>> GetMailThreadsByMailboxAsync(string mailboxId)
    {
        return await _connection.QueryAsync<MailThreadQueryResult>(
            """
            SELECT
                t.thread_id AS ThreadId,
                t.external_id AS ExternalId,
                t.subject AS Subject,
                t.participants_summary AS ParticipantsSummary,
                t.preview AS Preview,
                t.latest_message_received_at AS LatestMessageReceivedAt,
                t.unread_count AS UnreadCount,
                t.message_count AS MessageCount,
                t.has_attachments AS HasAttachments,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType,
                mb.mailbox_id AS MailboxId,
                mb.name AS MailboxName
            FROM mail_thread t
            INNER JOIN mailbox mb ON mb.mailbox_id = @MailboxId AND mb.account_id = t.account_id
            INNER JOIN account a ON t.account_id = a.account_id
            WHERE EXISTS (
                SELECT 1
                FROM mail_message mm
                INNER JOIN mail_message_mailbox mmm ON mm.message_id = mmm.message_id
                WHERE mm.thread_id = t.thread_id AND mmm.mailbox_id = mb.mailbox_id
            )
            ORDER BY COALESCE(t.latest_message_received_at, 0) DESC, t.thread_id
            """,
            new { MailboxId = mailboxId },
            commandTimeout: 30
        );
    }

    public async Task<MailThreadQueryResult?> GetMailThreadByMailboxAsync(string mailboxId, string threadId)
    {
        return await _connection.QuerySingleOrDefaultAsync<MailThreadQueryResult>(
            """
            SELECT
                t.thread_id AS ThreadId,
                t.external_id AS ExternalId,
                t.subject AS Subject,
                t.participants_summary AS ParticipantsSummary,
                t.preview AS Preview,
                t.latest_message_received_at AS LatestMessageReceivedAt,
                t.unread_count AS UnreadCount,
                t.message_count AS MessageCount,
                t.has_attachments AS HasAttachments,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType,
                mb.mailbox_id AS MailboxId,
                mb.name AS MailboxName
            FROM mail_thread t
            INNER JOIN mailbox mb ON mb.mailbox_id = @MailboxId AND mb.account_id = t.account_id
            INNER JOIN account a ON t.account_id = a.account_id
            WHERE t.thread_id = @ThreadId
                AND EXISTS (
                    SELECT 1
                    FROM mail_message mm
                    INNER JOIN mail_message_mailbox mmm ON mm.message_id = mmm.message_id
                    WHERE mm.thread_id = t.thread_id AND mmm.mailbox_id = mb.mailbox_id
                )
            LIMIT 1
            """,
            new { MailboxId = mailboxId, ThreadId = threadId },
            commandTimeout: 30
        );
    }

    #endregion

    #region Mail Message Methods

    public async Task<MailMessageDbo?> GetMailMessageByExternalIdAsync(string accountId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<MailMessageDbo>(
            "SELECT account_id AS AccountId, thread_id AS ThreadId, message_id AS MessageId, external_id AS ExternalId, " +
            "internet_message_id AS InternetMessageId, subject AS Subject, sender_name AS SenderName, sender_address AS SenderAddress, " +
            "sent_at AS SentAt, received_at AS ReceivedAt, preview AS Preview, plain_text_body AS PlainTextBody, " +
            "html_body AS HtmlBody, body_fetched_at AS BodyFetchedAt, has_html_body AS HasHtmlBody, " +
            "has_plain_text_body AS HasPlainTextBody, has_attachments AS HasAttachments, " +
            "has_external_resources AS HasExternalResources, has_blocked_content AS HasBlockedContent, " +
            "is_unread AS IsUnread, is_starred AS IsStarred, is_answered AS IsAnswered, is_draft AS IsDraft, " +
            "changed_at AS ChangedAt FROM mail_message WHERE account_id = @AccountId AND external_id = @ExternalId",
            new { AccountId = accountId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<MailMessageDbo?> GetMailMessageByIdAsync(string messageId)
    {
        return await _connection.QuerySingleOrDefaultAsync<MailMessageDbo>(
            "SELECT account_id AS AccountId, thread_id AS ThreadId, message_id AS MessageId, external_id AS ExternalId, " +
            "internet_message_id AS InternetMessageId, subject AS Subject, sender_name AS SenderName, sender_address AS SenderAddress, " +
            "sent_at AS SentAt, received_at AS ReceivedAt, preview AS Preview, plain_text_body AS PlainTextBody, " +
            "html_body AS HtmlBody, body_fetched_at AS BodyFetchedAt, has_html_body AS HasHtmlBody, " +
            "has_plain_text_body AS HasPlainTextBody, has_attachments AS HasAttachments, " +
            "has_external_resources AS HasExternalResources, has_blocked_content AS HasBlockedContent, " +
            "is_unread AS IsUnread, is_starred AS IsStarred, is_answered AS IsAnswered, is_draft AS IsDraft, " +
            "changed_at AS ChangedAt FROM mail_message WHERE message_id = @MessageId",
            new { MessageId = messageId },
            commandTimeout: 30
        );
    }

    public async Task<string> CreateOrUpdateMailMessageAsync(MailMessageDbo messageDbo, IEnumerable<string> mailboxIds)
    {
        var existing = !string.IsNullOrWhiteSpace(messageDbo.ExternalId)
            ? await GetMailMessageByExternalIdAsync(messageDbo.AccountId, messageDbo.ExternalId)
            : !string.IsNullOrWhiteSpace(messageDbo.MessageId)
                ? await GetMailMessageByIdAsync(messageDbo.MessageId)
                : null;

        var messageId = existing?.MessageId
            ?? (!string.IsNullOrWhiteSpace(messageDbo.MessageId) ? messageDbo.MessageId : Guid.NewGuid().ToString());

        using var transaction = _connection.BeginTransaction();

        if (existing != null)
        {
            await _connection.ExecuteAsync(
                """
                UPDATE mail_message
                SET thread_id = @ThreadId,
                    external_id = @ExternalId,
                    internet_message_id = @InternetMessageId,
                    subject = @Subject,
                    sender_name = @SenderName,
                    sender_address = @SenderAddress,
                    sent_at = @SentAt,
                    received_at = @ReceivedAt,
                    preview = @Preview,
                    plain_text_body = CASE
                        WHEN @PlainTextBody IS NULL AND @HtmlBody IS NULL AND @BodyFetchedAt IS NULL THEN plain_text_body
                        ELSE @PlainTextBody
                    END,
                    html_body = CASE
                        WHEN @PlainTextBody IS NULL AND @HtmlBody IS NULL AND @BodyFetchedAt IS NULL THEN html_body
                        ELSE @HtmlBody
                    END,
                    body_fetched_at = COALESCE(@BodyFetchedAt, body_fetched_at),
                    has_html_body = CASE
                        WHEN @PlainTextBody IS NULL AND @HtmlBody IS NULL AND @BodyFetchedAt IS NULL THEN has_html_body
                        ELSE @HasHtmlBody
                    END,
                    has_plain_text_body = CASE
                        WHEN @PlainTextBody IS NULL AND @HtmlBody IS NULL AND @BodyFetchedAt IS NULL THEN has_plain_text_body
                        ELSE @HasPlainTextBody
                    END,
                    has_attachments = @HasAttachments,
                    has_external_resources = CASE
                        WHEN @PlainTextBody IS NULL AND @HtmlBody IS NULL AND @BodyFetchedAt IS NULL THEN has_external_resources
                        ELSE @HasExternalResources
                    END,
                    has_blocked_content = CASE
                        WHEN @PlainTextBody IS NULL AND @HtmlBody IS NULL AND @BodyFetchedAt IS NULL THEN has_blocked_content
                        ELSE @HasBlockedContent
                    END,
                    is_unread = @IsUnread,
                    is_starred = @IsStarred,
                    is_answered = @IsAnswered,
                    is_draft = @IsDraft,
                    changed_at = @ChangedAt
                WHERE message_id = @MessageId
                """,
                new
                {
                    MessageId = messageId,
                    messageDbo.ThreadId,
                    messageDbo.ExternalId,
                    messageDbo.InternetMessageId,
                    messageDbo.Subject,
                    messageDbo.SenderName,
                    messageDbo.SenderAddress,
                    messageDbo.SentAt,
                    messageDbo.ReceivedAt,
                    messageDbo.Preview,
                    messageDbo.PlainTextBody,
                    messageDbo.HtmlBody,
                    messageDbo.BodyFetchedAt,
                    messageDbo.HasHtmlBody,
                    messageDbo.HasPlainTextBody,
                    messageDbo.HasAttachments,
                    messageDbo.HasExternalResources,
                    messageDbo.HasBlockedContent,
                    messageDbo.IsUnread,
                    messageDbo.IsStarred,
                    messageDbo.IsAnswered,
                    messageDbo.IsDraft,
                    messageDbo.ChangedAt
                },
                transaction: transaction,
                commandTimeout: 30
            );
        }
        else
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mail_message (account_id, thread_id, message_id, external_id, internet_message_id, subject, sender_name, sender_address, sent_at, received_at, preview, plain_text_body, html_body, body_fetched_at, has_html_body, has_plain_text_body, has_attachments, has_external_resources, has_blocked_content, is_unread, is_starred, is_answered, is_draft, changed_at) " +
                "VALUES (@AccountId, @ThreadId, @MessageId, @ExternalId, @InternetMessageId, @Subject, @SenderName, @SenderAddress, @SentAt, @ReceivedAt, @Preview, @PlainTextBody, @HtmlBody, @BodyFetchedAt, @HasHtmlBody, @HasPlainTextBody, @HasAttachments, @HasExternalResources, @HasBlockedContent, @IsUnread, @IsStarred, @IsAnswered, @IsDraft, @ChangedAt)",
                new
                {
                    messageDbo.AccountId,
                    messageDbo.ThreadId,
                    MessageId = messageId,
                    messageDbo.ExternalId,
                    messageDbo.InternetMessageId,
                    messageDbo.Subject,
                    messageDbo.SenderName,
                    messageDbo.SenderAddress,
                    messageDbo.SentAt,
                    messageDbo.ReceivedAt,
                    messageDbo.Preview,
                    messageDbo.PlainTextBody,
                    messageDbo.HtmlBody,
                    messageDbo.BodyFetchedAt,
                    messageDbo.HasHtmlBody,
                    messageDbo.HasPlainTextBody,
                    messageDbo.HasAttachments,
                    messageDbo.HasExternalResources,
                    messageDbo.HasBlockedContent,
                    messageDbo.IsUnread,
                    messageDbo.IsStarred,
                    messageDbo.IsAnswered,
                    messageDbo.IsDraft,
                    messageDbo.ChangedAt
                },
                transaction: transaction,
                commandTimeout: 30
            );
        }

        await ReplaceMailMessageMailboxesAsync(messageId, mailboxIds, transaction);
        await CleanupOrphanMailDataAsync(transaction);
        transaction.Commit();

        messageDbo.MessageId = messageId;
        return messageId;
    }

    public async Task<IEnumerable<string>> GetMailboxIdsByMessageAsync(string messageId)
    {
        return await _connection.QueryAsync<string>(
            "SELECT mailbox_id FROM mail_message_mailbox WHERE message_id = @MessageId ORDER BY mailbox_id",
            new { MessageId = messageId },
            commandTimeout: 30
        );
    }

    public async Task<bool> ReplaceMailMessageMailboxesAsync(string messageId, IEnumerable<string> mailboxIds)
    {
        using var transaction = _connection.BeginTransaction();
        await ReplaceMailMessageMailboxesAsync(messageId, mailboxIds, transaction);
        await CleanupOrphanMailDataAsync(transaction);
        transaction.Commit();
        return true;
    }

    public async Task<bool> RemoveMailMessageFromMailboxAsync(string messageId, string mailboxId)
    {
        using var transaction = _connection.BeginTransaction();

        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM mail_message_mailbox WHERE message_id = @MessageId AND mailbox_id = @MailboxId",
            new { MessageId = messageId, MailboxId = mailboxId },
            transaction: transaction,
            commandTimeout: 30
        );

        await CleanupOrphanMailDataAsync(transaction);
        transaction.Commit();

        return rowsAffected > 0;
    }

    public async Task<bool> DeleteMailMessageAsync(string messageId)
    {
        using var transaction = _connection.BeginTransaction();

        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM mail_message WHERE message_id = @MessageId",
            new { MessageId = messageId },
            transaction: transaction,
            commandTimeout: 30
        );

        await CleanupOrphanMailDataAsync(transaction);
        transaction.Commit();

        return rowsAffected > 0;
    }

    public async Task<bool> DeleteMailMessageByExternalIdAsync(string accountId, string externalId)
    {
        using var transaction = _connection.BeginTransaction();

        var rowsAffected = await _connection.ExecuteAsync(
            "DELETE FROM mail_message WHERE account_id = @AccountId AND external_id = @ExternalId",
            new { AccountId = accountId, ExternalId = externalId },
            transaction: transaction,
            commandTimeout: 30
        );

        await CleanupOrphanMailDataAsync(transaction);
        transaction.Commit();

        return rowsAffected > 0;
    }

    public async Task<bool> SetMailMessageDataAsync(string messageId, string key, string value)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
                UPDATE mail_message
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, @value)
                WHERE message_id = @message_id
            """,
            param: new { key = $"$.{key}", value, message_id = messageId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<bool> SetMailMessageDataJsonAsync(string messageId, string key, string jsonValue)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
                UPDATE mail_message
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, jsonb(@jsonValue))
                WHERE message_id = @message_id
            """,
            param: new { key = $"$.{key}", jsonValue, message_id = messageId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<string?> GetMailMessageDataAsync(string messageId, string key)
    {
        return await _connection.QuerySingleAsync<string?>(
            """
            SELECT coalesce(data ->> @key, '') as value
            FROM mail_message
            WHERE message_id = @message_id
            """,
            param: new { key = $"$.{key}", message_id = messageId });
    }

    public async Task<bool> SetMailMessageRawDataAsync(string messageId, string rawData)
    {
        return await SetMailMessageDataAsync(messageId, "rawData", rawData);
    }

    public async Task<string?> GetMailMessageRawDataAsync(string messageId)
    {
        return await GetMailMessageDataAsync(messageId, "rawData");
    }

    public async Task<bool> UpdateMailMessageStateAsync(string messageId, bool isUnread, bool isStarred, bool? isAnswered = null, bool? isDraft = null)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
            UPDATE mail_message
            SET is_unread = @IsUnread,
                is_starred = @IsStarred,
                is_answered = COALESCE(@IsAnswered, is_answered),
                is_draft = COALESCE(@IsDraft, is_draft)
            WHERE message_id = @MessageId
            """,
            new
            {
                MessageId = messageId,
                IsUnread = isUnread ? 1 : 0,
                IsStarred = isStarred ? 1 : 0,
                IsAnswered = isAnswered.HasValue ? (int?)(isAnswered.Value ? 1 : 0) : null,
                IsDraft = isDraft.HasValue ? (int?)(isDraft.Value ? 1 : 0) : null
            },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<bool> UpdateMailMessageBodyAsync(string messageId, string? plainTextBody, string? htmlBody, long? bodyFetchedAt, bool hasHtmlBody, bool hasPlainTextBody, bool hasExternalResources, bool hasBlockedContent)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
            UPDATE mail_message
            SET plain_text_body = @PlainTextBody,
                html_body = @HtmlBody,
                body_fetched_at = @BodyFetchedAt,
                has_html_body = @HasHtmlBody,
                has_plain_text_body = @HasPlainTextBody,
                has_external_resources = @HasExternalResources,
                has_blocked_content = @HasBlockedContent
            WHERE message_id = @MessageId
            """,
            new
            {
                MessageId = messageId,
                PlainTextBody = plainTextBody,
                HtmlBody = htmlBody,
                BodyFetchedAt = bodyFetchedAt,
                HasHtmlBody = hasHtmlBody ? 1 : 0,
                HasPlainTextBody = hasPlainTextBody ? 1 : 0,
                HasExternalResources = hasExternalResources ? 1 : 0,
                HasBlockedContent = hasBlockedContent ? 1 : 0
            },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<IEnumerable<MailMessageQueryResult>> GetMailMessagesByThreadAsync(string threadId)
    {
        return await _connection.QueryAsync<MailMessageQueryResult>(
            """
            SELECT
                m.message_id AS MessageId,
                m.external_id AS ExternalId,
                m.internet_message_id AS InternetMessageId,
                m.subject AS Subject,
                m.sender_name AS SenderName,
                m.sender_address AS SenderAddress,
                m.sent_at AS SentAt,
                m.received_at AS ReceivedAt,
                m.preview AS Preview,
                m.plain_text_body AS PlainTextBody,
                m.html_body AS HtmlBody,
                m.body_fetched_at AS BodyFetchedAt,
                m.has_html_body AS HasHtmlBody,
                m.has_plain_text_body AS HasPlainTextBody,
                m.has_attachments AS HasAttachments,
                m.has_external_resources AS HasExternalResources,
                m.has_blocked_content AS HasBlockedContent,
                m.is_unread AS IsUnread,
                m.is_starred AS IsStarred,
                m.is_answered AS IsAnswered,
                m.is_draft AS IsDraft,
                m.changed_at AS ChangedAt,
                m.data ->> '$.rawData' AS RawData,
                m.thread_id AS ThreadId,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType
            FROM mail_message m
            INNER JOIN account a ON m.account_id = a.account_id
            WHERE m.thread_id = @ThreadId
            ORDER BY COALESCE(m.received_at, m.sent_at, 0), m.message_id
            """,
            new { ThreadId = threadId },
            commandTimeout: 30
        );
    }

    public async Task<IEnumerable<MailMessageQueryResult>> GetMailMessagesByMailboxAsync(string mailboxId)
    {
        return await _connection.QueryAsync<MailMessageQueryResult>(
            """
            SELECT
                m.message_id AS MessageId,
                m.external_id AS ExternalId,
                m.internet_message_id AS InternetMessageId,
                m.subject AS Subject,
                m.sender_name AS SenderName,
                m.sender_address AS SenderAddress,
                m.sent_at AS SentAt,
                m.received_at AS ReceivedAt,
                m.preview AS Preview,
                m.plain_text_body AS PlainTextBody,
                m.html_body AS HtmlBody,
                m.body_fetched_at AS BodyFetchedAt,
                m.has_html_body AS HasHtmlBody,
                m.has_plain_text_body AS HasPlainTextBody,
                m.has_attachments AS HasAttachments,
                m.has_external_resources AS HasExternalResources,
                m.has_blocked_content AS HasBlockedContent,
                m.is_unread AS IsUnread,
                m.is_starred AS IsStarred,
                m.is_answered AS IsAnswered,
                m.is_draft AS IsDraft,
                m.changed_at AS ChangedAt,
                m.data ->> '$.rawData' AS RawData,
                m.thread_id AS ThreadId,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType
            FROM mail_message m
            INNER JOIN mail_message_mailbox mmm ON m.message_id = mmm.message_id
            INNER JOIN account a ON m.account_id = a.account_id
            WHERE mmm.mailbox_id = @MailboxId
            ORDER BY COALESCE(m.received_at, m.sent_at, 0) DESC, m.message_id
            """,
            new { MailboxId = mailboxId },
            commandTimeout: 30
        );
    }

    public async Task<MailMessageQueryResult?> GetMailMessageQueryByIdAsync(string messageId)
    {
        return await _connection.QuerySingleOrDefaultAsync<MailMessageQueryResult>(
            """
            SELECT
                m.message_id AS MessageId,
                m.external_id AS ExternalId,
                m.internet_message_id AS InternetMessageId,
                m.subject AS Subject,
                m.sender_name AS SenderName,
                m.sender_address AS SenderAddress,
                m.sent_at AS SentAt,
                m.received_at AS ReceivedAt,
                m.preview AS Preview,
                m.plain_text_body AS PlainTextBody,
                m.html_body AS HtmlBody,
                m.body_fetched_at AS BodyFetchedAt,
                m.has_html_body AS HasHtmlBody,
                m.has_plain_text_body AS HasPlainTextBody,
                m.has_attachments AS HasAttachments,
                m.has_external_resources AS HasExternalResources,
                m.has_blocked_content AS HasBlockedContent,
                m.is_unread AS IsUnread,
                m.is_starred AS IsStarred,
                m.is_answered AS IsAnswered,
                m.is_draft AS IsDraft,
                m.changed_at AS ChangedAt,
                m.data ->> '$.rawData' AS RawData,
                m.thread_id AS ThreadId,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType
            FROM mail_message m
            INNER JOIN account a ON m.account_id = a.account_id
            WHERE m.message_id = @MessageId
            LIMIT 1
            """,
            new { MessageId = messageId },
            commandTimeout: 30
        );
    }

    public async Task<MailMessageQueryResult?> GetMailMessageByMailboxAsync(string mailboxId, string messageId)
    {
        return await _connection.QuerySingleOrDefaultAsync<MailMessageQueryResult>(
            """
            SELECT
                m.message_id AS MessageId,
                m.external_id AS ExternalId,
                m.internet_message_id AS InternetMessageId,
                m.subject AS Subject,
                m.sender_name AS SenderName,
                m.sender_address AS SenderAddress,
                m.sent_at AS SentAt,
                m.received_at AS ReceivedAt,
                m.preview AS Preview,
                m.plain_text_body AS PlainTextBody,
                m.html_body AS HtmlBody,
                m.body_fetched_at AS BodyFetchedAt,
                m.has_html_body AS HasHtmlBody,
                m.has_plain_text_body AS HasPlainTextBody,
                m.has_attachments AS HasAttachments,
                m.has_external_resources AS HasExternalResources,
                m.has_blocked_content AS HasBlockedContent,
                m.is_unread AS IsUnread,
                m.is_starred AS IsStarred,
                m.is_answered AS IsAnswered,
                m.is_draft AS IsDraft,
                m.changed_at AS ChangedAt,
                m.data ->> '$.rawData' AS RawData,
                m.thread_id AS ThreadId,
                a.account_id AS AccountId,
                a.name AS AccountName,
                a.type AS AccountType
            FROM mail_message m
            INNER JOIN mail_message_mailbox mmm ON m.message_id = mmm.message_id
            INNER JOIN account a ON m.account_id = a.account_id
            WHERE mmm.mailbox_id = @MailboxId AND m.message_id = @MessageId
            LIMIT 1
            """,
            new { MailboxId = mailboxId, MessageId = messageId },
            commandTimeout: 30
        );
    }

    #endregion

    #region Mail Attachment Methods

    public async Task<IEnumerable<MailAttachmentDbo>> GetAttachmentsByMessageAsync(string messageId)
    {
        return await _connection.QueryAsync<MailAttachmentDbo>(
            "SELECT message_id AS MessageId, attachment_id AS AttachmentId, external_id AS ExternalId, file_name AS FileName, " +
            "mime_type AS MimeType, size AS Size, is_inline AS IsInline, content_id AS ContentId, " +
            "content_path AS ContentPath, downloaded_at AS DownloadedAt FROM mail_attachment WHERE message_id = @MessageId ORDER BY is_inline DESC, file_name, attachment_id",
            new { MessageId = messageId },
            commandTimeout: 30
        );
    }

    public async Task<MailAttachmentDbo?> GetAttachmentByExternalIdAsync(string messageId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<MailAttachmentDbo>(
            "SELECT message_id AS MessageId, attachment_id AS AttachmentId, external_id AS ExternalId, file_name AS FileName, " +
            "mime_type AS MimeType, size AS Size, is_inline AS IsInline, content_id AS ContentId, " +
            "content_path AS ContentPath, downloaded_at AS DownloadedAt FROM mail_attachment WHERE message_id = @MessageId AND external_id = @ExternalId",
            new { MessageId = messageId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<MailAttachmentDbo?> GetAttachmentByIdAsync(string attachmentId)
    {
        return await _connection.QuerySingleOrDefaultAsync<MailAttachmentDbo>(
            "SELECT message_id AS MessageId, attachment_id AS AttachmentId, external_id AS ExternalId, file_name AS FileName, " +
            "mime_type AS MimeType, size AS Size, is_inline AS IsInline, content_id AS ContentId, " +
            "content_path AS ContentPath, downloaded_at AS DownloadedAt FROM mail_attachment WHERE attachment_id = @AttachmentId",
            new { AttachmentId = attachmentId },
            commandTimeout: 30
        );
    }

    public async Task ReplaceAttachmentsAsync(string messageId, IEnumerable<MailAttachmentDbo> attachments)
    {
        var existingAttachments = (await GetAttachmentsByMessageAsync(messageId)).ToArray();
        var attachmentsToApply = attachments.ToArray();
        var existingByExternalId = existingAttachments
            .Where(attachment => !string.IsNullOrWhiteSpace(attachment.ExternalId))
            .ToDictionary(attachment => attachment.ExternalId!, StringComparer.Ordinal);
        var existingById = existingAttachments
            .Where(attachment => !string.IsNullOrWhiteSpace(attachment.AttachmentId))
            .ToDictionary(attachment => attachment.AttachmentId, StringComparer.Ordinal);
        var keptAttachmentIds = new HashSet<string>(StringComparer.Ordinal);

        using var transaction = _connection.BeginTransaction();

        foreach (var attachment in attachmentsToApply)
        {
            attachment.MessageId = messageId;

            var existing = !string.IsNullOrWhiteSpace(attachment.ExternalId) && existingByExternalId.TryGetValue(attachment.ExternalId, out var byExternalId)
                ? byExternalId
                : !string.IsNullOrWhiteSpace(attachment.AttachmentId) && existingById.TryGetValue(attachment.AttachmentId, out var byId)
                    ? byId
                    : null;

            var attachmentId = existing?.AttachmentId
                ?? (!string.IsNullOrWhiteSpace(attachment.AttachmentId) ? attachment.AttachmentId : Guid.NewGuid().ToString());

            if (existing != null)
            {
                await _connection.ExecuteAsync(
                    """
                    UPDATE mail_attachment
                    SET external_id = @ExternalId,
                        file_name = @FileName,
                        mime_type = @MimeType,
                        size = @Size,
                        is_inline = @IsInline,
                        content_id = @ContentId,
                        content_path = COALESCE(@ContentPath, content_path),
                        downloaded_at = COALESCE(@DownloadedAt, downloaded_at)
                    WHERE attachment_id = @AttachmentId
                    """,
                    new
                    {
                        AttachmentId = attachmentId,
                        attachment.ExternalId,
                        attachment.FileName,
                        attachment.MimeType,
                        attachment.Size,
                        attachment.IsInline,
                        attachment.ContentId,
                        attachment.ContentPath,
                        attachment.DownloadedAt
                    },
                    transaction: transaction,
                    commandTimeout: 30
                );
            }
            else
            {
                await _connection.ExecuteAsync(
                    "INSERT INTO mail_attachment (message_id, attachment_id, external_id, file_name, mime_type, size, is_inline, content_id, content_path, downloaded_at) " +
                    "VALUES (@MessageId, @AttachmentId, @ExternalId, @FileName, @MimeType, @Size, @IsInline, @ContentId, @ContentPath, @DownloadedAt)",
                    new
                    {
                        MessageId = messageId,
                        AttachmentId = attachmentId,
                        attachment.ExternalId,
                        attachment.FileName,
                        attachment.MimeType,
                        attachment.Size,
                        attachment.IsInline,
                        attachment.ContentId,
                        attachment.ContentPath,
                        attachment.DownloadedAt
                    },
                    transaction: transaction,
                    commandTimeout: 30
                );
            }

            attachment.AttachmentId = attachmentId;
            keptAttachmentIds.Add(attachmentId);
        }

        if (keptAttachmentIds.Count == 0)
        {
            await _connection.ExecuteAsync(
                "DELETE FROM mail_attachment WHERE message_id = @MessageId",
                new { MessageId = messageId },
                transaction: transaction,
                commandTimeout: 30
            );
        }
        else
        {
            await _connection.ExecuteAsync(
                "DELETE FROM mail_attachment WHERE message_id = @MessageId AND attachment_id NOT IN @AttachmentIds",
                new { MessageId = messageId, AttachmentIds = keptAttachmentIds.ToArray() },
                transaction: transaction,
                commandTimeout: 30
            );
        }

        transaction.Commit();
    }

    public async Task<bool> UpdateAttachmentContentAsync(string attachmentId, string? contentPath, long? downloadedAt)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "UPDATE mail_attachment SET content_path = @ContentPath, downloaded_at = @DownloadedAt WHERE attachment_id = @AttachmentId",
            new { AttachmentId = attachmentId, ContentPath = contentPath, DownloadedAt = downloadedAt },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<bool> SetAttachmentDataAsync(string attachmentId, string key, string value)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
                UPDATE mail_attachment
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, @value)
                WHERE attachment_id = @attachment_id
            """,
            param: new { key = $"$.{key}", value, attachment_id = attachmentId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<bool> SetAttachmentDataJsonAsync(string attachmentId, string key, string jsonValue)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            """
                UPDATE mail_attachment
                SET data = jsonb_set(coalesce(data, jsonb_object()), @key, jsonb(@jsonValue))
                WHERE attachment_id = @attachment_id
            """,
            param: new { key = $"$.{key}", jsonValue, attachment_id = attachmentId },
            commandTimeout: 30
        );

        return rowsAffected > 0;
    }

    public async Task<string?> GetAttachmentDataAsync(string attachmentId, string key)
    {
        return await _connection.QuerySingleAsync<string?>(
            """
            SELECT coalesce(data ->> @key, '') as value
            FROM mail_attachment
            WHERE attachment_id = @attachment_id
            """,
            param: new { key = $"$.{key}", attachment_id = attachmentId });
    }

    #endregion

    private async Task ReplaceMailMessageMailboxesAsync(string messageId, IEnumerable<string> mailboxIds, SqliteTransaction transaction)
    {
        var normalizedMailboxIds = mailboxIds
            .Where(mailboxId => !string.IsNullOrWhiteSpace(mailboxId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await _connection.ExecuteAsync(
            "DELETE FROM mail_message_mailbox WHERE message_id = @MessageId",
            new { MessageId = messageId },
            transaction: transaction,
            commandTimeout: 30
        );

        foreach (var mailboxId in normalizedMailboxIds)
        {
            await _connection.ExecuteAsync(
                "INSERT OR IGNORE INTO mail_message_mailbox (message_id, mailbox_id) VALUES (@MessageId, @MailboxId)",
                new { MessageId = messageId, MailboxId = mailboxId },
                transaction: transaction,
                commandTimeout: 30
            );
        }
    }

    private async Task CleanupOrphanMailDataAsync(SqliteTransaction? transaction = null)
    {
        await _connection.ExecuteAsync(
            """
            DELETE FROM mail_message
            WHERE NOT EXISTS (
                SELECT 1
                FROM mail_message_mailbox mmm
                WHERE mmm.message_id = mail_message.message_id
            )
            """,
            transaction: transaction,
            commandTimeout: 30
        );

        await _connection.ExecuteAsync(
            """
            DELETE FROM mail_thread
            WHERE NOT EXISTS (
                SELECT 1
                FROM mail_message mm
                WHERE mm.thread_id = mail_thread.thread_id
            )
            """,
            transaction: transaction,
            commandTimeout: 30
        );
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}