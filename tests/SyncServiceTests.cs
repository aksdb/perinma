using CredentialStore;
using Dapper;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;
using perinma.Tests.Fakes;

namespace tests;

public class SyncServiceTests
{
    [Test]
    public async Task WithNewCalendars_SavesCalendarsToDatabase()
    {
        // Arrange - Use real DatabaseService in memory mode
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true, color: "#ff0000"),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Personal Calendar", selected: true, color: "#00ff00"),
            FakeGoogleCalendarService.CreateCalendar("cal3", "Disabled Calendar", selected: false)
        );
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);

        var fakeCalDavService = new FakeCalDavService();
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        // Store test credentials
        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer",
            Scope = "calendar.readonly"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendarList = calendars.ToList();

        Assert.That(calendarList, Has.Count.EqualTo(3));
        Assert.That(calendarList.Any(c => c.ExternalId == "cal1" && c.Enabled == 1), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal2" && c.Enabled == 1), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal3" && c.Enabled == 0), Is.True);
    }

    [Test]
    public async Task WithInvalidSyncToken_FallsBackToFullSync()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        // Create 3 existing calendars from a "previous sync"
        var cal1 = new CalendarDbo
        {
            AccountId = accountId,
            CalendarId = Guid.NewGuid().ToString(),
            ExternalId = "cal1",
            Name = "Work Calendar",
            Color = "#ff0000",
            Enabled = 1,
            LastSync = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
        };
        var cal2 = new CalendarDbo
        {
            AccountId = accountId,
            CalendarId = Guid.NewGuid().ToString(),
            ExternalId = "cal2",
            Name = "Personal Calendar",
            Color = "#00ff00",
            Enabled = 1,
            LastSync = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
        };
        var cal3 = new CalendarDbo
        {
            AccountId = accountId,
            CalendarId = Guid.NewGuid().ToString(),
            ExternalId = "cal3",
            Name = "Old Calendar",
            Color = "#0000ff",
            Enabled = 1,
            LastSync = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateCalendarAsync(cal1);
        await storage.CreateOrUpdateCalendarAsync(cal2);
        await storage.CreateOrUpdateCalendarAsync(cal3);

        // Store test credentials
        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer",
            Scope = "calendar.readonly"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        // Store an invalid/expired sync token to simulate a previous sync
        await storage.SetAccountData(account, "calendarSyncToken", "invalid-expired-token");

        // Set up fake service to return only 2 calendars:
        // - cal1 unchanged
        // - cal2 with updated name and disabled
        // - cal3 not returned (deleted remotely)
        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true, color: "#ff0000"),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Personal Calendar - Updated", selected: false, color: "#00ff00")
        );

        // Simulate invalid sync token on first call, then succeed on retry with full sync
        fakeGoogleService.SetInvalidSyncTokenBehavior(true);

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendarList = calendars.ToList();

        // Should have only 2 calendars now (cal3 deleted)
        Assert.That(calendarList, Has.Count.EqualTo(2));

        // cal1 should still exist unchanged
        var resultCal1 = calendarList.FirstOrDefault(c => c.ExternalId == "cal1");
        Assert.That(resultCal1, Is.Not.Null);
        Assert.That(resultCal1!.Name, Is.EqualTo("Work Calendar"));
        Assert.That(resultCal1.Enabled, Is.EqualTo(1));

        // cal2 should exist with updated data
        var resultCal2 = calendarList.FirstOrDefault(c => c.ExternalId == "cal2");
        Assert.That(resultCal2, Is.Not.Null);
        Assert.That(resultCal2!.Name, Is.EqualTo("Personal Calendar - Updated"));
        Assert.That(resultCal2.Enabled, Is.EqualTo(0)); // Changed to disabled

        // cal3 should be deleted
        var resultCal3 = calendarList.FirstOrDefault(c => c.ExternalId == "cal3");
        Assert.That(resultCal3, Is.Null);
    }

    [Test]
    public async Task FullSync_DeletesRemovedCalendars()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        // Store test credentials
        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer",
            Scope = "calendar.readonly"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        // First sync: 3 calendars
        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Calendar 1"),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Calendar 2"),
            FakeGoogleCalendarService.CreateCalendar("cal3", "Calendar 3")
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Perform first sync
        await syncService.SyncAllAccountsAsync();

        // Verify all 3 calendars were created
        var calendarsAfterFirstSync = await storage.GetCalendarsByAccountAsync(accountId);
        Assert.That(calendarsAfterFirstSync.Count(), Is.EqualTo(3));

        // Clear the sync token to force a full sync (simulating token expiration or manual refresh)
        await storage.SetAccountData(account, "calendarSyncToken", "");

        // Wait 1 second to ensure the second sync has a different timestamp
        await Task.Delay(1000);

        // Second sync: Only 2 calendars (cal3 removed remotely)
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Calendar 1"),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Calendar 2")
        );

        // Act - Perform second full sync (no sync token means full sync)
        await syncService.SyncAllAccountsAsync();

        // Assert - Verify cal3 was deleted from database
        var calendarsAfterSecondSync = await storage.GetCalendarsByAccountAsync(accountId);
        var calendarList = calendarsAfterSecondSync.ToList();

        Assert.That(calendarList, Has.Count.EqualTo(2));
        Assert.That(calendarList.Any(c => c.ExternalId == "cal1"), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal2"), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal3"), Is.False);
    }

    [Test]
    public async Task WithDeletedFlag_SkipsDeletedCalendars()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        // Store test credentials
        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer",
            Scope = "calendar.readonly"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        // Set up fake service with one active calendar and one deleted calendar
        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Active Calendar"),
            FakeGoogleCalendarService.CreateDeletedCalendar("cal2")
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert - Only cal1 should be saved, cal2 should be skipped
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendarList = calendars.ToList();

        Assert.That(calendarList, Has.Count.EqualTo(1));
        Assert.That(calendarList.Any(c => c.ExternalId == "cal1"), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal2"), Is.False);
    }

    [Test]
    public async Task SyncsEventsForEnabledCalendars()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        // Store test credentials
        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer",
            Scope = "calendar.readonly"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        // Set up fake service with calendar and events
        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var eventStart = DateTime.UtcNow.AddHours(1);
        var eventEnd = DateTime.UtcNow.AddHours(2);
        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateEvent("event1", "Team Meeting", eventStart, eventEnd),
            FakeGoogleCalendarService.CreateEvent("event2", "Lunch Break", eventStart.AddHours(2), eventEnd.AddHours(2))
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert - Verify calendar was created
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        Assert.That(calendar.ExternalId, Is.EqualTo("cal1"));

        // Verify events were synced
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(2));
        Assert.That(eventList.Any(e => e.ExternalId == "event1" && e.Title == "Team Meeting"), Is.True);
        Assert.That(eventList.Any(e => e.ExternalId == "event2" && e.Title == "Lunch Break"), Is.True);

        // Verify raw event data is stored
        var event1 = eventList.First(e => e.ExternalId == "event1");
        var rawData = await storage.GetEventData(event1.EventId, "rawData");
        Assert.That(rawData, Is.Not.Null.And.Not.Empty);
        Assert.That(rawData, Does.Contain("Team Meeting"));
        Assert.That(rawData, Does.Contain("event1"));
    }

    #region Recurrence Handling Tests

    [Test]
    public async Task GoogleRecurringEvent_WithUntilClause_StoresRecurrenceEndTime()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        // Create a recurring event with UNTIL clause
        // Weekly meeting starting Jan 1, 2025 at 10:00 UTC, ending March 31, 2025
        var eventStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring1",
                "Weekly Team Sync",
                eventStart,
                eventEnd,
                "RRULE:FREQ=WEEKLY;UNTIL=20250331T235959Z"
            )
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();
        
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc, Is.EqualTo(new DateTime(2025, 3, 26, 11, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task GoogleRecurringEvent_WithCountClause_StoresCalculatedEndTime()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        // Create a daily recurring event with COUNT=5
        // Starting Jan 15, 2025 at 14:00 UTC, 2 hours duration
        var eventStart = new DateTime(2025, 1, 15, 14, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 15, 16, 0, 0, DateTimeKind.Utc);
        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring2",
                "Daily Standup",
                eventStart,
                eventEnd,
                "RRULE:FREQ=DAILY;COUNT=5"
            )
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // 5 daily occurrences starting Jan 15: Jan 15, 16, 17, 18, 19
        // Last occurrence ends Jan 19 at 16:00 UTC
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(1));
        Assert.That(endTimeUtc.Day, Is.EqualTo(19));
        Assert.That(endTimeUtc.Hour, Is.EqualTo(16));
    }

    [Test]
    public async Task GoogleRecurringEvent_WithTimezone_HandlesTimezoneCorrectly()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        // Create a recurring event with timezone (America/New_York = UTC-5 in winter)
        // Monthly event at 9:00 AM New York time for 3 months
        var eventStart = new DateTime(2025, 2, 1, 9, 0, 0, DateTimeKind.Utc); // 9 AM in UTC for test
        var eventEnd = new DateTime(2025, 2, 1, 10, 0, 0, DateTimeKind.Utc);
        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEventWithTimezone(
                "recurring3",
                "Monthly Review",
                eventStart,
                eventEnd,
                "America/New_York",
                "RRULE:FREQ=MONTHLY;COUNT=3"
            )
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // 3 monthly occurrences: Feb 1, Mar 1, Apr 1
        // Last occurrence ends Apr 1 at 10:00 UTC
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(4));
        Assert.That(endTimeUtc.Day, Is.EqualTo(1));
    }

    [Test]
    public async Task GoogleRecurringEvent_WithNoEndClause_SetsMaximumEndTime()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        // Create an infinite recurring event (no UNTIL or COUNT)
        var eventStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring4",
                "Weekly Infinite Meeting",
                eventStart,
                eventEnd,
                "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR"
            )
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // For infinite recurrence, EndTime should be the max available value, since there is no
        // theoretical end.
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime.Date;
        Assert.That(endTimeUtc, Is.EqualTo(DateTimeOffset.MaxValue.UtcDateTime.Date));
    }

    [Test]
    public async Task CalDavRecurringEvent_WithUntilClause_StoresRecurrenceEndTime()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test CalDAV Account",
            Type = "CalDAV"
        };
        await storage.CreateAccountAsync(account);

        var credentials = new CalDavCredentials
        {
            Type = "CalDAV",
            ServerUrl = "https://caldav.example.com",
            Username = "testuser",
            Password = "testpass"
        };
        credentialManager.StoreCalDavCredentials(accountId, credentials);

        var fakeCalDavService = new FakeCalDavService();
        fakeCalDavService.SetCalendars(new CalDavCalendar
        {
            Url = "https://caldav.example.com/calendars/work",
            DisplayName = "Work Calendar",
            Deleted = false
        });

        // Create a recurring event with UNTIL clause via raw iCalendar
        var eventStart = new DateTime(2025, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 3, 1, 10, 30, 0, DateTimeKind.Utc);
        fakeCalDavService.SetEvents(
            "https://caldav.example.com/calendars/work",
            FakeCalDavService.CreateRecurringEvent(
                "caldav-recurring1",
                "https://caldav.example.com/calendars/work/event1.ics",
                "Weekly Planning",
                eventStart,
                eventEnd,
                "RRULE:FREQ=WEEKLY;UNTIL=20250601T235959Z"
            )
        );

        var fakeGoogleService = new FakeGoogleCalendarService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // The last occurrence should end on 31.05. at 10:30.
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc, Is.EqualTo(new DateTime(2025, 5, 31, 10, 30, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task CalDavRecurringEvent_WithCountClause_StoresCalculatedEndTime()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test CalDAV Account",
            Type = "CalDAV"
        };
        await storage.CreateAccountAsync(account);

        var credentials = new CalDavCredentials
        {
            Type = "CalDAV",
            ServerUrl = "https://caldav.example.com",
            Username = "testuser",
            Password = "testpass"
        };
        credentialManager.StoreCalDavCredentials(accountId, credentials);

        var fakeCalDavService = new FakeCalDavService();
        fakeCalDavService.SetCalendars(new CalDavCalendar
        {
            Url = "https://caldav.example.com/calendars/personal",
            DisplayName = "Personal Calendar",
            Deleted = false
        });

        // Create a recurring event with COUNT clause
        // Daily for 10 occurrences, starting May 1, 2025
        var eventStart = new DateTime(2025, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 5, 1, 8, 30, 0, DateTimeKind.Utc);
        fakeCalDavService.SetEvents(
            "https://caldav.example.com/calendars/personal",
            FakeCalDavService.CreateRecurringEvent(
                "caldav-recurring2",
                "https://caldav.example.com/calendars/personal/event2.ics",
                "Morning Routine",
                eventStart,
                eventEnd,
                "RRULE:FREQ=DAILY;COUNT=10"
            )
        );

        var fakeGoogleService = new FakeGoogleCalendarService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // 10 daily occurrences starting May 1: May 1-10
        // Last occurrence ends May 10 at 8:30 UTC
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(5));
        Assert.That(endTimeUtc.Day, Is.EqualTo(10));
        Assert.That(endTimeUtc.Hour, Is.EqualTo(8));
        Assert.That(endTimeUtc.Minute, Is.EqualTo(30));
    }

    [Test]
    public async Task CalDavRecurringEvent_WithTimezone_HandlesTimezoneCorrectly()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test CalDAV Account",
            Type = "CalDAV"
        };
        await storage.CreateAccountAsync(account);

        var credentials = new CalDavCredentials
        {
            Type = "CalDAV",
            ServerUrl = "https://caldav.example.com",
            Username = "testuser",
            Password = "testpass"
        };
        credentialManager.StoreCalDavCredentials(accountId, credentials);

        var fakeCalDavService = new FakeCalDavService();
        fakeCalDavService.SetCalendars(new CalDavCalendar
        {
            Url = "https://caldav.example.com/calendars/europe",
            DisplayName = "Europe Calendar",
            Deleted = false
        });

        // Create a recurring event with Europe/Berlin timezone
        // Weekly for 4 occurrences, starting June 1, 2025 at 15:00 local time
        var eventStart = new DateTime(2025, 6, 1, 15, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 6, 1, 16, 0, 0, DateTimeKind.Utc);
        fakeCalDavService.SetEvents(
            "https://caldav.example.com/calendars/europe",
            FakeCalDavService.CreateRecurringEventWithTimezone(
                "caldav-recurring3",
                "https://caldav.example.com/calendars/europe/event3.ics",
                "European Team Call",
                eventStart,
                eventEnd,
                "Europe/Berlin",
                "RRULE:FREQ=WEEKLY;COUNT=4"
            )
        );

        var fakeGoogleService = new FakeGoogleCalendarService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // 4 weekly occurrences: June 1, 8, 15, 22
        // Last occurrence ends June 22 at 16:00 UTC
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(6));
        Assert.That(endTimeUtc.Day, Is.EqualTo(22));
    }

    [Test]
    public async Task NonRecurringEvent_KeepsOriginalEndTime()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        // Create a non-recurring event
        var eventStart = new DateTime(2025, 7, 15, 14, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 7, 15, 15, 30, 0, DateTimeKind.Utc);
        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateEvent(
                "single-event",
                "One-time Meeting",
                eventStart,
                eventEnd
            )
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var singleEvent = eventList.First();

        // EndTime should be the original event end time
        Assert.That(singleEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(singleEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(7));
        Assert.That(endTimeUtc.Day, Is.EqualTo(15));
        Assert.That(endTimeUtc.Hour, Is.EqualTo(15));
        Assert.That(endTimeUtc.Minute, Is.EqualTo(30));
    }

    #endregion

    #region Force Resync Tests

    [Test]
    public async Task ForceResync_ClearsAllDataAndPerformsFullSync()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        // Store test credentials
        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer",
            Scope = "calendar.readonly"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        // Initial sync with 3 calendars and events
        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Calendar 1", selected: true),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Calendar 2", selected: true),
            FakeGoogleCalendarService.CreateCalendar("cal3", "Calendar 3", selected: true)
        );

        var eventStart = DateTime.UtcNow.AddHours(1);
        var eventEnd = DateTime.UtcNow.AddHours(2);
        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateEvent("event1", "Event 1", eventStart, eventEnd)
        );
        fakeGoogleService.SetEvents("cal2",
            FakeGoogleCalendarService.CreateEvent("event2", "Event 2", eventStart, eventEnd)
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Perform initial sync
        await syncService.SyncAllAccountsAsync();

        // Verify initial state
        var calendarsBeforeResync = await storage.GetCalendarsByAccountAsync(accountId);
        Assert.That(calendarsBeforeResync.Count(), Is.EqualTo(3));

        var cal1 = calendarsBeforeResync.First(c => c.ExternalId == "cal1");
        var eventsBeforeResync = await storage.GetEventsByCalendarAsync(cal1.CalendarId);
        Assert.That(eventsBeforeResync.Count(), Is.EqualTo(1));

        // Store a sync token to verify it gets cleared
        await storage.SetAccountData(account, "calendarSyncToken", "some-sync-token");
        var tokenBefore = await storage.GetAccountData(account, "calendarSyncToken");
        Assert.That(tokenBefore, Is.EqualTo("some-sync-token"));

        // Now simulate remote changes - only 2 calendars exist now
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Calendar 1 - Updated", selected: true),
            FakeGoogleCalendarService.CreateCalendar("cal4", "New Calendar", selected: true)
        );
        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateEvent("event1", "Event 1 - Updated", eventStart, eventEnd),
            FakeGoogleCalendarService.CreateEvent("event3", "New Event", eventStart.AddHours(3), eventEnd.AddHours(3))
        );
        fakeGoogleService.SetEvents("cal4",
            FakeGoogleCalendarService.CreateEvent("event4", "Event in New Calendar", eventStart, eventEnd)
        );

        // Act - Force resync
        var result = await syncService.ForceResyncAccountAsync(accountId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.SyncedAccounts, Is.EqualTo(1));
        Assert.That(result.FailedAccounts, Is.EqualTo(0));

        // Verify sync token was cleared (or has a new value from the fresh sync)
        var tokenAfter = await storage.GetAccountData(account, "calendarSyncToken");
        Assert.That(tokenAfter, Is.Not.EqualTo("some-sync-token"));

        // Verify calendars reflect the new remote state
        var calendarsAfterResync = await storage.GetCalendarsByAccountAsync(accountId);
        var calendarList = calendarsAfterResync.ToList();

        Assert.That(calendarList, Has.Count.EqualTo(2));
        Assert.That(calendarList.Any(c => c.ExternalId == "cal1" && c.Name == "Calendar 1 - Updated"), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal4" && c.Name == "New Calendar"), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal2"), Is.False);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal3"), Is.False);

        // Verify events reflect the new remote state
        var updatedCal1 = calendarList.First(c => c.ExternalId == "cal1");
        var eventsAfterResync = await storage.GetEventsByCalendarAsync(updatedCal1.CalendarId);
        var eventList = eventsAfterResync.ToList();

        Assert.That(eventList, Has.Count.EqualTo(2));
        Assert.That(eventList.Any(e => e.ExternalId == "event1" && e.Title == "Event 1 - Updated"), Is.True);
        Assert.That(eventList.Any(e => e.ExternalId == "event3" && e.Title == "New Event"), Is.True);
    }

    [Test]
    public async Task ForceResync_WithInvalidAccountId_ReturnsError()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var fakeGoogleService = new FakeGoogleCalendarService();
        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        var result = await syncService.ForceResyncAccountAsync("non-existent-account-id");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(result.Errors[0], Does.Contain("not found"));
    }

    [Test]
    public async Task ForceResync_CalDavAccount_ClearsDataAndResyncs()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        // Create CalDAV account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test CalDAV Account",
            Type = "CalDAV"
        };
        await storage.CreateAccountAsync(account);

        var credentials = new CalDavCredentials
        {
            Type = "CalDAV",
            ServerUrl = "https://caldav.example.com",
            Username = "testuser",
            Password = "testpass"
        };
        credentialManager.StoreCalDavCredentials(accountId, credentials);

        var fakeCalDavService = new FakeCalDavService();
        fakeCalDavService.SetCalendars(new CalDavCalendar
        {
            Url = "https://caldav.example.com/calendars/work",
            DisplayName = "Work Calendar",
            Deleted = false
        });

        var fakeGoogleService = new FakeGoogleCalendarService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Perform initial sync
        await syncService.SyncAllAccountsAsync();

        // Verify initial state
        var calendarsBeforeResync = await storage.GetCalendarsByAccountAsync(accountId);
        Assert.That(calendarsBeforeResync.Count(), Is.EqualTo(1));

        // Store a sync token
        await storage.SetAccountData(account, "calendarSyncToken", "caldav-sync-token");

        // Change remote state
        fakeCalDavService.SetCalendars(
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/personal",
                DisplayName = "Personal Calendar",
                Deleted = false
            }
        );

        // Act - Force resync
        var result = await syncService.ForceResyncAccountAsync(accountId);

        // Assert
        Assert.That(result.Success, Is.True);

        var calendarsAfterResync = await storage.GetCalendarsByAccountAsync(accountId);
        var calendarList = calendarsAfterResync.ToList();

        Assert.That(calendarList, Has.Count.EqualTo(1));
        Assert.That(calendarList[0].Name, Is.EqualTo("Personal Calendar"));
        Assert.That(calendarList[0].ExternalId, Is.EqualTo("https://caldav.example.com/calendars/personal"));
    }

    [Test]
    public async Task ClearAccountSyncData_RemovesCalendarsEventsAndSyncToken()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        // Create calendars
        var cal1 = new CalendarDbo
        {
            AccountId = accountId,
            CalendarId = Guid.NewGuid().ToString(),
            ExternalId = "cal1",
            Name = "Calendar 1",
            Enabled = 1,
            LastSync = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateCalendarAsync(cal1);

        // Create events
        var event1 = new CalendarEventDbo
        {
            CalendarId = cal1.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "event1",
            Title = "Event 1",
            StartTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            EndTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            ChangedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(event1);

        // Store sync token
        await storage.SetAccountData(account, "calendarSyncToken", "test-sync-token");

        // Verify data exists
        var calendarsBeforeClear = await storage.GetCalendarsByAccountAsync(accountId);
        Assert.That(calendarsBeforeClear.Count(), Is.EqualTo(1));

        var eventsBeforeClear = await storage.GetEventsByCalendarAsync(cal1.CalendarId);
        Assert.That(eventsBeforeClear.Count(), Is.EqualTo(1));

        var tokenBeforeClear = await storage.GetAccountData(account, "calendarSyncToken");
        Assert.That(tokenBeforeClear, Is.EqualTo("test-sync-token"));

        // Act
        await storage.ClearAccountSyncDataAsync(accountId);

        // Assert - All data should be cleared
        var calendarsAfterClear = await storage.GetCalendarsByAccountAsync(accountId);
        Assert.That(calendarsAfterClear.Count(), Is.EqualTo(0));

        // Events are cascade-deleted with calendars, but verify by checking the account still exists
        var accountAfterClear = await storage.GetAccountByIdAsync(accountId);
        Assert.That(accountAfterClear, Is.Not.Null);

        // Sync token should be cleared (empty JSON object means key won't exist)
        var tokenAfterClear = await storage.GetAccountData(account, "calendarSyncToken");
        Assert.That(string.IsNullOrEmpty(tokenAfterClear), Is.True);
    }

    #endregion

    #region Google Calendar Override Tests

    [Test]
    public async Task GoogleCancelledOverride_StoresWithOriginalStartAsStartAndEnd()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var recurringStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var recurringEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);

        var overrideTime = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);

        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring1",
                "Weekly Meeting",
                recurringStart,
                recurringEnd,
                "RRULE:FREQ=WEEKLY;BYDAY=WE"
            ),
            FakeGoogleCalendarService.CreateCancelledOverride(
                "override1",
                "recurring1",
                overrideTime
            )
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(2));

        var overrideEvent = eventList.FirstOrDefault(e => e.ExternalId == "override1");
        Assert.That(overrideEvent, Is.Not.Null);

        // For cancelled override, start and end should both be the original start time
        var startTimestamp = overrideEvent!.StartTime!.Value;
        var endTimestamp = overrideEvent.EndTime!.Value;
        var startUtc = DateTimeOffset.FromUnixTimeSeconds(startTimestamp).UtcDateTime;
        var endUtc = DateTimeOffset.FromUnixTimeSeconds(endTimestamp).UtcDateTime;

        Assert.That(startUtc, Is.EqualTo(overrideTime));
        Assert.That(endUtc, Is.EqualTo(overrideTime));
    }

    [Test]
    public async Task GoogleModifiedOverride_WithTimeOutsideBounds_ExpandsBounds()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var recurringStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var recurringEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);

        var originalStartTime = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newStart = new DateTime(2025, 1, 8, 9, 0, 0, DateTimeKind.Utc); // Earlier than original
        var newEnd = new DateTime(2025, 1, 8, 10, 30, 0, DateTimeKind.Utc);

        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring1",
                "Weekly Meeting",
                recurringStart,
                recurringEnd,
                "RRULE:FREQ=WEEKLY;BYDAY=WE"
            ),
            FakeGoogleCalendarService.CreateModifiedOverride(
                "override1",
                "recurring1",
                "Extended Meeting",
                originalStartTime,
                newStart,
                newEnd
            )
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(2));

        var overrideEvent = eventList.FirstOrDefault(e => e.ExternalId == "override1");
        Assert.That(overrideEvent, Is.Not.Null);

        // Bounds should be expanded to include original start time (10:00)
        var startTimestamp = overrideEvent!.StartTime!.Value;
        var startUtc = DateTimeOffset.FromUnixTimeSeconds(startTimestamp).UtcDateTime;

        Assert.That(startUtc, Is.EqualTo(newStart)); // Starts at 9:00 (original new start)
        Assert.That(startUtc, Is.LessThan(originalStartTime)); // Includes original start time
    }

    [Test]
    public async Task GoogleOverride_WithExistingParent_CreatesRelation()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var recurringStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var recurringEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        var overrideTime = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newStart = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newEnd = new DateTime(2025, 1, 8, 11, 30, 0, DateTimeKind.Utc);

        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring1",
                "Weekly Meeting",
                recurringStart,
                recurringEnd,
                "RRULE:FREQ=WEEKLY;BYDAY=WE"
            ),
            FakeGoogleCalendarService.CreateModifiedOverride(
                "override1",
                "recurring1",
                "Rescheduled Meeting",
                overrideTime,
                newStart,
                newEnd
            )
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(2));

        var parentEvent = eventList.FirstOrDefault(e => e.ExternalId == "recurring1");
        var overrideEvent = eventList.FirstOrDefault(e => e.ExternalId == "override1");
        Assert.That(parentEvent, Is.Not.Null);
        Assert.That(overrideEvent, Is.Not.Null);

        // Verify relation was created
        using var connection = database.GetConnection();
        var relation = await connection.QuerySingleOrDefaultAsync<(string ParentEventId, string ChildEventId)>(
            "SELECT parent_event_id, child_event_id FROM calendar_event_relation WHERE parent_event_id = @ParentEventId AND child_event_id = @ChildEventId",
            new { ParentEventId = parentEvent!.EventId, ChildEventId = overrideEvent!.EventId }
        );

        Assert.That(relation.ParentEventId, Is.EqualTo(parentEvent.EventId));
        Assert.That(relation.ChildEventId, Is.EqualTo(overrideEvent.EventId));
    }

    [Test]
    public async Task GoogleOverride_WithParentAfterOverride_CreatesRelationAfterBacklogProcessing()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var recurringStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var recurringEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        var overrideTime = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newStart = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newEnd = new DateTime(2025, 1, 8, 11, 30, 0, DateTimeKind.Utc);

        // First sync: Override arrives before parent
        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateModifiedOverride(
                "override1",
                "recurring1",
                "Rescheduled Meeting",
                overrideTime,
                newStart,
                newEnd
            )
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act - First sync
        await syncService.SyncAllAccountsAsync();

        // Assert - Override exists but no parent yet
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var eventsAfterFirstSync = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventListAfterFirstSync = eventsAfterFirstSync.ToList();

        Assert.That(eventListAfterFirstSync, Has.Count.EqualTo(1));
        Assert.That(eventListAfterFirstSync[0].ExternalId, Is.EqualTo("override1"));

        // Second sync: Parent arrives
        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring1",
                "Weekly Meeting",
                recurringStart,
                recurringEnd,
                "RRULE:FREQ=WEEKLY;BYDAY=WE"
            ),
            FakeGoogleCalendarService.CreateModifiedOverride(
                "override1",
                "recurring1",
                "Rescheduled Meeting",
                overrideTime,
                newStart,
                newEnd
            )
        );

        // Act - Second sync
        await syncService.SyncAllAccountsAsync();

        // Assert - Both events exist with relation
        var eventsAfterSecondSync = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventListAfterSecondSync = eventsAfterSecondSync.ToList();

        Assert.That(eventListAfterSecondSync, Has.Count.EqualTo(2));

        var parentEvent = eventListAfterSecondSync.FirstOrDefault(e => e.ExternalId == "recurring1");
        var overrideEvent = eventListAfterSecondSync.FirstOrDefault(e => e.ExternalId == "override1");
        Assert.That(parentEvent, Is.Not.Null);
        Assert.That(overrideEvent, Is.Not.Null);

        using var connection = database.GetConnection();
        var relation = await connection.QuerySingleOrDefaultAsync<(string ParentEventId, string ChildEventId)>(
            "SELECT parent_event_id, child_event_id FROM calendar_event_relation WHERE parent_event_id = @ParentEventId AND child_event_id = @ChildEventId",
            new { ParentEventId = parentEvent!.EventId, ChildEventId = overrideEvent!.EventId }
        );

        Assert.That(relation.ParentEventId, Is.EqualTo(parentEvent.EventId));
        Assert.That(relation.ChildEventId, Is.EqualTo(overrideEvent.EventId));
    }

    [Test]
    public async Task GoogleOverride_WithParentNeverArrived_StaysInBacklog()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = AccountType.Google.ToString(),
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        credentialManager.StoreGoogleCredentials(accountId, credentials);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var overrideTime = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newStart = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newEnd = new DateTime(2025, 1, 8, 11, 30, 0, DateTimeKind.Utc);

        // Sync with only the override, parent never arrives
        fakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateModifiedOverride(
                "override1",
                "recurring1",
                "Rescheduled Meeting",
                overrideTime,
                newStart,
                newEnd
            )
        );

        var fakeCalDavService = new FakeCalDavService();
        var googleProvider = new GoogleCalendarProvider(fakeGoogleService);
        var calDavProvider = new CalDavCalendarProvider(fakeCalDavService);
        var providers = new Dictionary<string, ICalendarProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = googleProvider,
            ["CalDAV"] = calDavProvider
        };
        var reminderService = new ReminderService(storage, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();

        // Override should be stored
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();
        Assert.That(eventList, Has.Count.EqualTo(1));
        Assert.That(eventList[0].ExternalId, Is.EqualTo("override1"));

        // Backlog should contain the pending relation
        using var connection = database.GetConnection();
        var backlogItems = await connection.QueryAsync<(string ParentExternalId, string ChildExternalId)>(
            "SELECT parent_external_id, child_external_id FROM calendar_event_relation_backlog WHERE calendar_id = @CalendarId",
            new { CalendarId = calendar.CalendarId }
        );

        var backlogList = backlogItems.ToList();
        Assert.That(backlogList, Has.Count.EqualTo(1));
        Assert.That(backlogList[0].ParentExternalId, Is.EqualTo("recurring1"));
        Assert.That(backlogList[0].ChildExternalId, Is.EqualTo("override1"));

        // No relation should exist yet
        var relations = await connection.QueryAsync<string>(
            "SELECT child_event_id FROM calendar_event_relation WHERE parent_event_id = @EventId OR child_event_id = @EventId",
            new { EventId = eventList[0].EventId }
        );

        Assert.That(relations.Any(), Is.False);
    }

    #endregion
}
