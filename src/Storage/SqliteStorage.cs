using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

    // In-memory cache for Account and Calendar models (low cardinality)
    private readonly ConcurrentDictionary<Guid, Account> _accountCache = new();
    private readonly ConcurrentDictionary<Guid, Calendar> _calendarCache = new();
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
            "SELECT account_id AS AccountId, name AS Name, type AS Type, sort_order AS SortOrder FROM account ORDER BY sort_order, name",
            commandTimeout: 30
        );
    }

    public async Task<AccountDbo?> GetAccountByIdAsync(string accountId)
    {
        return await _connection.QuerySingleOrDefaultAsync<AccountDbo>(
            "SELECT account_id AS AccountId, name AS Name, type AS Type, sort_order AS SortOrder FROM account WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    public async Task<AccountDbo?> GetAccountByNameAsync(string name)
    {
        return await _connection.QuerySingleOrDefaultAsync<AccountDbo>(
            "SELECT account_id AS AccountId, name AS Name, type AS Type, sort_order AS SortOrder FROM account WHERE name = @Name",
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
        var rowsAffected = await _connection.ExecuteAsync(
            "INSERT INTO account (account_id, name, type) VALUES (@AccountId, @Name, @Type)",
            account,
            commandTimeout: 30
        );

        if (rowsAffected > 0)
        {
            var accountModel = new Account
            {
                Id = Guid.Parse(account.AccountId),
                Name = account.Name,
                Type = Enum.Parse<AccountType>(account.Type)
            };
            _accountCache[accountModel.Id] = accountModel;
        }

        return rowsAffected > 0;
    }

    public async Task<bool> UpdateAccountAsync(AccountDbo account)
    {
        var rowsAffected = await _connection.ExecuteAsync(
            "UPDATE account SET name = @Name, type = @Type WHERE account_id = @AccountId",
            account,
            commandTimeout: 30
        );

        if (rowsAffected > 0 && _cacheInitialized)
        {
            var accountId = Guid.Parse(account.AccountId);
            if (_accountCache.TryGetValue(accountId, out var cachedAccount))
            {
                cachedAccount.Name = account.Name;
                cachedAccount.Type = Enum.Parse<AccountType>(account.Type);
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

        if (_cacheInitialized)
        {
            var accountGuid = Guid.Parse(accountId);
            var calendarsToRemove = _calendarCache
                .Where(kvp => kvp.Value.Account.Id == accountGuid)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var calendarId in calendarsToRemove)
            {
                _calendarCache.TryRemove(calendarId, out _);
            }
        }
    }

    #region Calendar Methods

    public async Task<IEnumerable<CalendarDbo>> GetCalendarsByAccountAsync(string accountId)
    {
        return await _connection.QueryAsync<CalendarDbo>(
            "SELECT account_id AS AccountId, calendar_id AS CalendarId, external_id AS ExternalId, " +
            "name AS Name, color AS Color, enabled AS Enabled, last_sync AS LastSync, data AS Data " +
            "FROM calendar WHERE account_id = @AccountId",
            new { AccountId = accountId },
            commandTimeout: 30
        );
    }

    public async Task<CalendarDbo?> GetCalendarByExternalIdAsync(string accountId, string externalId)
    {
        return await _connection.QuerySingleOrDefaultAsync<CalendarDbo>(
            "SELECT account_id AS AccountId, calendar_id AS CalendarId, external_id AS ExternalId, " +
            "name AS Name, color AS Color, enabled AS Enabled, last_sync AS LastSync, data AS Data " +
            "FROM calendar WHERE account_id = @AccountId AND external_id = @ExternalId",
            new { AccountId = accountId, ExternalId = externalId },
            commandTimeout: 30
        );
    }

    public async Task<CalendarDbo?> GetCalendarByIdAsync(string calendarId)
    {
        return await _connection.QuerySingleOrDefaultAsync<CalendarDbo>(
            "SELECT account_id AS AccountId, calendar_id AS CalendarId, external_id AS ExternalId, " +
            "name AS Name, color AS Color, enabled AS Enabled, last_sync AS LastSync, data AS Data " +
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

            if (rowsAffected > 0 && _cacheInitialized)
            {
                var calendarGuid = Guid.Parse(calendar.CalendarId);
                if (_calendarCache.TryGetValue(calendarGuid, out var cachedCalendar))
                {
                    cachedCalendar.Name = calendar.Name;
                    cachedCalendar.Color = calendar.Color;
                    cachedCalendar.Enabled = calendar.Enabled != 0;
                    cachedCalendar.LastSync = calendar.LastSync.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(calendar.LastSync.Value).DateTime
                        : null;
                }
            }

            return rowsAffected > 0;
        }
        else
        {
            var calendarId = Guid.NewGuid().ToString();
            var rowsAffected = await _connection.ExecuteAsync(
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

            if (rowsAffected > 0)
            {
                var accountGuid = Guid.Parse(calendar.AccountId);
                if (!_accountCache.TryGetValue(accountGuid, out var account))
                {
                    account = new Account
                    {
                        Id = accountGuid,
                        Name = await _connection.QuerySingleOrDefaultAsync<string>("SELECT name FROM account WHERE account_id = @AccountId", new { AccountId = calendar.AccountId }, commandTimeout: 30) ?? "Unknown Account",
                        Type = Enum.TryParse<AccountType>(await _connection.QuerySingleOrDefaultAsync<string>("SELECT type FROM account WHERE account_id = @AccountId", new { AccountId = calendar.AccountId }, commandTimeout: 30) ?? "Google", ignoreCase: true, out var accountType) ? accountType : AccountType.Google
                    };
                    _accountCache[accountGuid] = account;
                }
                
                var calendarModel = new Calendar
                {
                    Account = account,
                    Id = Guid.Parse(calendarId),
                    ExternalId = calendar.ExternalId,
                    Name = calendar.Name,
                    Color = calendar.Color,
                    Enabled = calendar.Enabled != 0,
                    LastSync = calendar.LastSync.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(calendar.LastSync.Value).DateTime
                        : null
                };
                _calendarCache[calendarModel.Id] = calendarModel;
            }

            return rowsAffected > 0;
        }
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

        if (rowsAffected > 0 && _cacheInitialized)
        {
            var calendarGuid = Guid.Parse(calendarId);
            if (_calendarCache.TryGetValue(calendarGuid, out var cachedCalendar))
            {
                cachedCalendar.Enabled = enabled;
            }
        }

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
                        CalendarId = calendarId, ParentExternalId = parentExternalId, ChildExternalId = childExternalId
                    },
                    commandTimeout: 30
                );
            }
        }
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

    public async Task CreateReminderAsync(string eventId, DateTime occurrenceTime, DateTime triggerTime)
    {
        var reminderId = Guid.NewGuid().ToString();

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

    public async Task<List<ReminderWithEvent>> GetDueRemindersAsync(HashSet<string> firedReminderIds)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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

    public Calendar? GetCachedCalendar(Guid calendarId)
    {
        EnsureCacheInitializedAsync();
        return _calendarCache.TryGetValue(calendarId, out var calendar) ? calendar : null;
    }

    public IEnumerable<Calendar> GetCachedCalendars(Account account)
    {
        EnsureCacheInitializedAsync();
        return from calendar in _calendarCache.Values
            where calendar.Account.Id == account.Id
            orderby calendar.Name
            select calendar;
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
            "SELECT account_id AS AccountId, name AS Name, type AS Type, sort_order AS SortOrder FROM account",
            commandTimeout: 30
        );

        var calendarDbos = await _connection.QueryAsync<CalendarDbo>(
            "SELECT account_id AS AccountId, calendar_id AS CalendarId, external_id AS ExternalId, " +
            "name AS Name, color AS Color, enabled AS Enabled, last_sync AS LastSync, data AS Data " +
            "FROM calendar",
            commandTimeout: 30
        );

        foreach (var accountDbo in accountDbos)
        {
            if (!Enum.TryParse<AccountType>(accountDbo.Type, ignoreCase: true, out var accountType))
            {
                continue;
            }

            var account = new Account
            {
                Id = Guid.Parse(accountDbo.AccountId),
                Name = accountDbo.Name,
                Type = accountType,
                SortOrder = accountDbo.SortOrder,
            };
            _accountCache[account.Id] = account;
        }

        foreach (var calendarDbo in calendarDbos)
        {
            var accountId = Guid.Parse(calendarDbo.AccountId);
            if (_accountCache.TryGetValue(accountId, out var account))
            {
                var calendar = new Calendar
                {
                    Account = account,
                    Id = Guid.Parse(calendarDbo.CalendarId),
                    ExternalId = calendarDbo.ExternalId,
                    Name = calendarDbo.Name,
                    Color = calendarDbo.Color,
                    Enabled = calendarDbo.Enabled != 0,
                    LastSync = calendarDbo.LastSync.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(calendarDbo.LastSync.Value).DateTime
                        : null
                };
                _calendarCache[calendar.Id] = calendar;
            }
        }
    }

    private void InvalidateAccountCache(Guid accountId)
    {
        _accountCache.TryRemove(accountId, out _);

        var calendarsToRemove = _calendarCache
            .Where(kvp => kvp.Value.Account.Id == accountId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var calendarId in calendarsToRemove)
        {
            _calendarCache.TryRemove(calendarId, out _);
        }
    }

    private void InvalidateCalendarCache(Guid calendarId)
    {
        _calendarCache.TryRemove(calendarId, out _);
    }

    private void ClearCache()
    {
        _accountCache.Clear();
        _calendarCache.Clear();
        _cacheInitialized = false;
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

    public void Dispose()
    {
        _connection.Dispose();
    }
}