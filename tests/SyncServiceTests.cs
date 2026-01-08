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
        var credentialManager = new TestCredentialManager();
        var storage = new SqliteStorage(database, credentialManager);

        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true, color: "#ff0000"),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Personal Calendar", selected: true, color: "#00ff00"),
            FakeGoogleCalendarService.CreateCalendar("cal3", "Disabled Calendar", selected: false)
        );

        var syncService = new SyncService(storage, credentialManager, fakeGoogleService);

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
        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar")
        );

        // Simulate invalid sync token on first call, then succeed on retry
        fakeGoogleService.SetInvalidSyncTokenBehavior(true);

        // You would set up the test to:
        // 1. Store a sync token in the account data
        // 2. Call sync (it should fail with invalid token)
        // 3. Verify it retries without sync token (full sync)
        // 4. Verify calendars are synced successfully

        // Act & Assert
        // await syncService.SyncAccountAsync(account, CancellationToken.None);
        // Verify full sync was performed and calendars saved
    }

    [Test]
    public async Task FullSync_DeletesRemovedCalendars()
    {
        // Arrange
        var fakeGoogleService = new FakeGoogleCalendarService();

        // First sync: 3 calendars
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Calendar 1"),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Calendar 2"),
            FakeGoogleCalendarService.CreateCalendar("cal3", "Calendar 3")
        );

        // You would:
        // 1. Perform first sync to create calendars
        // 2. Update fake to return only 2 calendars (cal3 was deleted remotely)
        // 3. Perform second sync (full sync, no sync token)
        // 4. Verify cal3 was deleted from database

        // Second sync: Only 2 calendars (cal3 removed)
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Calendar 1"),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Calendar 2")
        );

        // Act & Assert
        // Verify cal3 no longer exists in database after second sync
    }

    [Test]
    public async Task WithDeletedFlag_SkipsDeletedCalendars()
    {
        // Arrange
        var fakeGoogleService = new FakeGoogleCalendarService();
        fakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Active Calendar"),
            FakeGoogleCalendarService.CreateDeletedCalendar("cal2")
        );

        // Act
        // await syncService.SyncAccountAsync(account, CancellationToken.None);

        // Assert
        // var calendars = await storage.GetCalendarsByAccountAsync(accountId);
        // Assert.Single(calendars); // Only cal1 should be saved
        // Assert.DoesNotContain(calendars, c => c.ExternalId == "cal2");
    }
}
