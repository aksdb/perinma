using CredentialStore;
using Dapper;
using perinma.Models;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Storage.Models;
using tests.Fakes;

namespace tests.Base;

/// <summary>
/// Base class for sync-related tests providing common setup infrastructure.
/// Handles database, credentials, fake services, and sync service initialization.
/// </summary>
public abstract class SyncTestBase
{
    protected DatabaseService? Database { get; private set; }
    protected CredentialManagerService CredentialManager { get; private set; } = null!;
    protected SqliteStorage Storage { get; private set; } = null!;
    protected GoogleCalendarServiceStub GoogleServiceStub { get; private set; } = null!;
    protected CalDavServiceStub CalDavServiceStub { get; private set; } = null!;
    protected Dictionary<AccountType, ICalendarProvider> Providers { get; private set; } = null!;
    protected ReminderService ReminderService { get; private set; } = null!;
    protected SyncService SyncService { get; private set; } = null!;

    [SetUp]
    public void BaseSetUp()
    {
        // Initialize database and core services
        Database = new DatabaseService(inMemory: true);
        CredentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        Storage = new SqliteStorage(Database, CredentialManager);

        // Initialize service stubs
        GoogleServiceStub = new GoogleCalendarServiceStub();
        CalDavServiceStub = new CalDavServiceStub();

        // Initialize providers
        var googleProvider = new GoogleCalendarProvider(GoogleServiceStub, CredentialManager);
        var calDavProvider = new CalDavCalendarProvider(CalDavServiceStub, CredentialManager);
        Providers = new Dictionary<AccountType, ICalendarProvider>
        {
            [AccountType.Google] = googleProvider,
            [AccountType.CalDav] = calDavProvider
        };

        // Initialize sync services
        ReminderService = new ReminderService(Storage, Providers);
        SyncService = new SyncService(Storage, CredentialManager, Providers, ReminderService);
    }

    [TearDown]
    public void BaseTearDown()
    {
        // Dispose of database
        Database?.Dispose();
        Storage?.Dispose();
    }

    /// <summary>
    /// Creates a test Google account with default settings.
    /// </summary>
    protected async Task<AccountDbo> CreateGoogleAccountAsync(string? accountId = null, string name = "Test Account")
    {
        accountId ??= Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = name,
            Type = AccountType.Google.ToString()
        };
        await Storage.CreateAccountAsync(account);
        return account;
    }

    /// <summary>
    /// Creates a test CalDAV account with default settings.
    /// </summary>
    protected async Task<AccountDbo> CreateCalDavAccountAsync(string? accountId = null, string name = "Test CalDAV Account")
    {
        accountId ??= Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = name,
            Type = "CalDAV"
        };
        await Storage.CreateAccountAsync(account);
        return account;
    }

    /// <summary>
    /// Stores default test Google credentials for an account.
    /// </summary>
    protected void StoreGoogleCredentials(string accountId, GoogleCredentials? credentials = null)
    {
        credentials ??= new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer",
            Scope = "calendar.readonly"
        };
        CredentialManager.StoreGoogleCredentials(accountId, credentials);
    }

    /// <summary>
    /// Stores default test CalDAV credentials for an account.
    /// </summary>
    protected void StoreCalDavCredentials(string accountId, string serverUrl = "https://caldav.example.com", string username = "testuser", string password = "testpass")
    {
        var credentials = new CalDavCredentials
        {
            Type = "CalDAV",
            ServerUrl = serverUrl,
            Username = username,
            Password = password
        };
        CredentialManager.StoreCalDavCredentials(accountId, credentials);
    }

    /// <summary>
    /// Creates test calendars in storage for an existing account (simulating previous sync).
    /// </summary>
    protected async Task<CalendarDbo[]> CreateExistingCalendarsAsync(string accountId, params (string externalId, string name, string color)[] calendars)
    {
        var result = new List<CalendarDbo>();
        foreach (var (externalId, name, color) in calendars)
        {
            var calendar = new CalendarDbo
            {
                AccountId = accountId,
                CalendarId = Guid.NewGuid().ToString(),
                ExternalId = externalId,
                Name = name,
                Color = color,
                Enabled = 1,
                LastSync = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
            };
            await Storage.CreateOrUpdateCalendarAsync(calendar);
            result.Add(calendar);
        }
        return result.ToArray();
    }

    /// <summary>
    /// Helper method for testing event relation backlogs.
    /// Queries the backlog table and returns the items.
    /// </summary>
    protected async Task<(string ParentExternalId, string ChildExternalId)[]> GetRelationBacklogAsync(string calendarId)
    {
        using var connection = Database!.GetConnection();
        var items = await connection.QueryAsync<(string ParentExternalId, string ChildExternalId)>(
            "SELECT parent_external_id, child_external_id FROM calendar_event_relation_backlog WHERE calendar_id = @CalendarId",
            new { CalendarId = calendarId }
        );
        return items.ToArray();
    }

    /// <summary>
    /// Gets the event relation for a given parent and child event ID.
    /// </summary>
    protected async Task<(string ParentEventId, string ChildEventId)?> GetEventRelationAsync(string parentEventId, string childEventId)
    {
        using var connection = Database!.GetConnection();
        return await connection.QuerySingleOrDefaultAsync<(string ParentEventId, string ChildEventId)?>(
            "SELECT parent_event_id, child_event_id FROM calendar_event_relation WHERE parent_event_id = @ParentEventId AND child_event_id = @ChildEventId",
            new { ParentEventId = parentEventId, ChildEventId = childEventId }
        );
    }
}
