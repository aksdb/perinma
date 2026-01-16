using System;
using CredentialStore;
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

        var fakeCalDavService = new FakeCalDavService();
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = "Google"
        };
        await storage.CreateAccountAsync(account);

        // Store test credentials
        var credentials = new GoogleCredentials
        {
            Type = "Google",
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
            Type = "Google"
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
            Type = "Google",
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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

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
            Type = "Google"
        };
        await storage.CreateAccountAsync(account);

        // Store test credentials
        var credentials = new GoogleCredentials
        {
            Type = "Google",
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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

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
            Type = "Google"
        };
        await storage.CreateAccountAsync(account);

        // Store test credentials
        var credentials = new GoogleCredentials
        {
            Type = "Google",
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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

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
            Type = "Google"
        };
        await storage.CreateAccountAsync(account);

        // Store test credentials
        var credentials = new GoogleCredentials
        {
            Type = "Google",
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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

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
            Type = "Google"
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = "Google",
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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // EndTime should be UNTIL date (March 31, 2025 23:59:59) + 1 hour duration
        // This gives us April 1, 2025 00:59:59 UTC
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(4));
        Assert.That(endTimeUtc.Day, Is.EqualTo(1));
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
            Type = "Google"
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = "Google",
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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

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
            Type = "Google"
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = "Google",
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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

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
    public async Task GoogleRecurringEvent_WithNoEndClause_KeepsOriginalEndTime()
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
            Type = "Google"
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = "Google",
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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // For infinite recurrence, EndTime should be the single occurrence end time
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(1));
        Assert.That(endTimeUtc.Day, Is.EqualTo(1));
        Assert.That(endTimeUtc.Hour, Is.EqualTo(11)); // Original end time
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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

        // Act
        await syncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        var calendar = calendars.First();
        var events = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // EndTime should be UNTIL date (June 1, 2025 23:59:59) + 1.5 hour duration
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(6));
        Assert.That(endTimeUtc.Day, Is.EqualTo(2)); // June 2 due to duration addition
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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

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
            Type = "Google"
        };
        await storage.CreateAccountAsync(account);

        var credentials = new GoogleCredentials
        {
            Type = "Google",
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
        var syncService = new SyncService(storage, credentialManager, fakeGoogleService, fakeCalDavService);

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
}
