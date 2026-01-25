using CredentialStore;
using Dapper;
using NUnit.Framework;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;

namespace tests;

[TestFixture]
public class SqliteStorageReminderTests
{
    private DatabaseService? _database;
    private SqliteStorage? _storage;
    private CredentialManagerService? _credentialManager;

    [SetUp]
    public void SetUp()
    {
        _database = new DatabaseService(inMemory: true);
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        _storage = new SqliteStorage(_database, _credentialManager);
    }

    [TearDown]
    public void TearDown()
    {
        _database?.Dispose();
        _storage?.Dispose();
    }

    #region CreateReminderAsync Tests

    [Test]
    public async Task CreateReminderAsync_ValidData_CreatesReminder()
    {
        // Arrange
        var eventId = Guid.NewGuid().ToString();
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = new DateTime(2026, 1, 25, 9, 30, 0, DateTimeKind.Utc);

        // Act
        await _storage!.CreateReminderAsync(eventId, occurrenceTime, triggerTime);

        // Assert
        using var connection = _database!.GetConnection();
        var reminder = await connection.QuerySingleOrDefaultAsync<ReminderDbo>(
            "SELECT reminder_id AS ReminderId, target_type AS TargetType, target_id AS TargetId, " +
            "target_time AS TargetTime, trigger_time AS TriggerTime " +
            "FROM reminder WHERE target_id = @EventId",
            new { EventId = eventId }
        );

        Assert.That(reminder, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(reminder.TargetType, Is.EqualTo((int)TargetType.CalendarEvent));
            Assert.That(reminder.TargetId, Is.EqualTo(eventId));
            Assert.That(reminder.TargetTime, Is.EqualTo(new DateTimeOffset(occurrenceTime).ToUnixTimeSeconds()));
            Assert.That(reminder.TriggerTime, Is.EqualTo(new DateTimeOffset(triggerTime).ToUnixTimeSeconds()));
        });
    }

    #endregion

    #region GetRemindersByEventAsync Tests

    [Test]
    public async Task GetRemindersByEventAsync_WithReminders_ReturnsReminders()
    {
        // Arrange
        var eventId = Guid.NewGuid().ToString();
        var occurrenceTime1 = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime1 = new DateTime(2026, 1, 25, 9, 30, 0, DateTimeKind.Utc);
        var occurrenceTime2 = new DateTime(2026, 1, 25, 11, 0, 0, DateTimeKind.Utc);
        var triggerTime2 = new DateTime(2026, 1, 25, 10, 30, 0, DateTimeKind.Utc);

        await _storage!.CreateReminderAsync(eventId, occurrenceTime1, triggerTime1);
        await _storage.CreateReminderAsync(eventId, occurrenceTime2, triggerTime2);

        // Act
        var reminders = await _storage.GetRemindersByEventAsync(eventId);

        // Assert
        Assert.That(reminders.Count, Is.EqualTo(2));
        Assert.That(reminders.All(r => r.TargetId == eventId), Is.True);
    }

    [Test]
    public async Task GetRemindersByEventAsync_NoReminders_ReturnsEmptyList()
    {
        // Arrange
        var eventId = Guid.NewGuid().ToString();

        // Act
        var reminders = await _storage!.GetRemindersByEventAsync(eventId);

        // Assert
        Assert.That(reminders.Count, Is.EqualTo(0));
    }

    #endregion

    #region GetDueRemindersAsync Tests

    [Test]
    public async Task GetDueRemindersAsync_WithDueReminders_ReturnsRemindersWithEventDetails()
    {
        // Arrange
        var eventId = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid().ToString();

        // Create account
        await _storage!.CreateAccountAsync(new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        });

        // Create calendar
        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            CalendarId = Guid.NewGuid().ToString(),
            ExternalId = "cal-123",
            Name = "Test Calendar",
            Color = "#FF0000",
            Enabled = 1
        };
        await _storage.CreateOrUpdateCalendarAsync(calendar);
        var calendarId = calendar.CalendarId;

        // Create event with Unix timestamp (stored as int)
        var startTime = new DateTime(2026, 1, 25, 10, 30, 0, DateTimeKind.Utc);
        var startTimeUnix = new DateTimeOffset(startTime).ToUnixTimeSeconds();

        using var connection = _database!.GetConnection();
        await connection.ExecuteAsync(
            "INSERT INTO calendar_event (calendar_id, event_id, start_time, end_time, title) " +
            "VALUES (@CalendarId, @EventId, @StartTime, @EndTime, @Title)",
            new
            {
                CalendarId = calendarId,
                EventId = eventId,
                StartTime = startTimeUnix,
                EndTime = startTimeUnix + 3600,
                Title = "Test Event"
            }
        );

        // Create reminder that is due (trigger time in the past)
        var occurrenceTime = startTime;
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(eventId, occurrenceTime, triggerTime);

        // Act
        var result = await _storage.GetDueRemindersAsync(new HashSet<string>());

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        var reminder = result[0];
        Assert.That(reminder.ReminderId, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(reminder.EventTitle, Is.EqualTo("Test Event"));
            Assert.That(reminder.CalendarName, Is.EqualTo("Test Calendar"));
            Assert.That(reminder.CalendarColor, Is.EqualTo("#FF0000"));
            Assert.That(reminder.StartTime, Is.EqualTo(startTime));
            Assert.That(reminder.TargetType, Is.EqualTo((int)TargetType.CalendarEvent));
            Assert.That(reminder.TargetId, Is.EqualTo(eventId));
        });
    }

    [Test]
    public async Task GetDueRemindersAsync_ExcludesFiredReminders()
    {
        // Arrange
        var eventId = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid().ToString();

        // Create account
        await _storage!.CreateAccountAsync(new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        });

        // Create calendar
        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            CalendarId = Guid.NewGuid().ToString(),
            ExternalId = "cal-123",
            Name = "Test Calendar",
            Color = "#FF0000",
            Enabled = 1
        };
        await _storage.CreateOrUpdateCalendarAsync(calendar);
        var calendarId = calendar.CalendarId;

        // Create event
        var startTime = new DateTime(2026, 1, 25, 10, 30, 0, DateTimeKind.Utc);
        var startTimeUnix = new DateTimeOffset(startTime).ToUnixTimeSeconds();

        using var connection = _database!.GetConnection();
        await connection.ExecuteAsync(
            "INSERT INTO calendar_event (calendar_id, event_id, start_time, end_time, title) " +
            "VALUES (@CalendarId, @EventId, @StartTime, @EndTime, @Title)",
            new
            {
                CalendarId = calendarId,
                EventId = eventId,
                StartTime = startTimeUnix,
                EndTime = startTimeUnix + 3600,
                Title = "Test Event"
            }
        );

        // Create two reminders that are due
        var triggerTime1 = DateTime.UtcNow.AddMinutes(-10);
        var triggerTime2 = DateTime.UtcNow.AddMinutes(-5);
        await _storage.CreateReminderAsync(eventId, startTime, triggerTime1);
        await _storage.CreateReminderAsync(eventId, startTime, triggerTime2);

        var firedReminderIds = new HashSet<string>();
        var reminders1 = await _storage.GetDueRemindersAsync(firedReminderIds);
        firedReminderIds.Add(reminders1[0].ReminderId);

        var reminders2 = await _storage.GetDueRemindersAsync(firedReminderIds);

        // Assert
        Assert.That(reminders1.Count, Is.EqualTo(2));
        Assert.That(reminders2.Count, Is.EqualTo(1));
        Assert.That(reminders2[0].ReminderId, Is.Not.EqualTo(reminders1[0].ReminderId));
    }

    [Test]
    public async Task GetDueRemindersAsync_NoDueReminders_ReturnsEmptyList()
    {
        // Arrange
        var firedReminderIds = new HashSet<string>();

        // Act
        var reminders = await _storage!.GetDueRemindersAsync(firedReminderIds);

        // Assert
        Assert.That(reminders.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task GetDueRemindersAsync_FutureReminders_NotReturned()
    {
        // Arrange
        var eventId = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid().ToString();

        // Create account
        await _storage!.CreateAccountAsync(new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        });

        // Create calendar
        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            CalendarId = Guid.NewGuid().ToString(),
            ExternalId = "cal-123",
            Name = "Test Calendar",
            Color = "#FF0000",
            Enabled = 1
        };
        await _storage.CreateOrUpdateCalendarAsync(calendar);
        var calendarId = calendar.CalendarId;

        // Create event
        var startTime = new DateTime(2026, 1, 25, 10, 30, 0, DateTimeKind.Utc);
        var startTimeUnix = new DateTimeOffset(startTime).ToUnixTimeSeconds();

        using var connection = _database!.GetConnection();
        await connection.ExecuteAsync(
            "INSERT INTO calendar_event (calendar_id, event_id, start_time, end_time, title) " +
            "VALUES (@CalendarId, @EventId, @StartTime, @EndTime, @Title)",
            new
            {
                CalendarId = calendarId,
                EventId = eventId,
                StartTime = startTimeUnix,
                EndTime = startTimeUnix + 3600,
                Title = "Test Event"
            }
        );

        // Create reminder that is NOT due (trigger time in the future)
        var triggerTime = DateTime.UtcNow.AddHours(1);
        await _storage.CreateReminderAsync(eventId, startTime, triggerTime);

        // Act
        var reminders = await _storage.GetDueRemindersAsync(new HashSet<string>());

        // Assert
        Assert.That(reminders.Count, Is.EqualTo(0));
    }

    #endregion

    #region GetReminderAsync Tests

    [Test]
    public async Task GetReminderAsync_ExistingReminder_ReturnsReminder()
    {
        // Arrange
        var eventId = Guid.NewGuid().ToString();
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = new DateTime(2026, 1, 25, 9, 30, 0, DateTimeKind.Utc);
        await _storage!.CreateReminderAsync(eventId, occurrenceTime, triggerTime);

        // Get the reminder ID
        using var connection = _database!.GetConnection();
        var reminderId = await connection.QuerySingleAsync<string>(
            "SELECT reminder_id FROM reminder WHERE target_id = @EventId",
            new { EventId = eventId }
        );

        // Act
        var reminder = await _storage.GetReminderAsync(reminderId);

        // Assert
        Assert.That(reminder, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(reminder.ReminderId, Is.EqualTo(reminderId));
            Assert.That(reminder.TargetId, Is.EqualTo(eventId));
        });
    }

    [Test]
    public async Task GetReminderAsync_NonExistentReminder_ReturnsNull()
    {
        // Act
        var reminder = await _storage!.GetReminderAsync("non-existent-id");

        // Assert
        Assert.That(reminder, Is.Null);
    }

    #endregion

    #region DeleteReminderAsync Tests

    [Test]
    public async Task DeleteReminderAsync_ExistingReminder_DeletesReminder()
    {
        // Arrange
        var eventId = Guid.NewGuid().ToString();
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = new DateTime(2026, 1, 25, 9, 30, 0, DateTimeKind.Utc);
        await _storage!.CreateReminderAsync(eventId, occurrenceTime, triggerTime);

        using var connection = _database!.GetConnection();
        var reminderId = await connection.QuerySingleAsync<string>(
            "SELECT reminder_id FROM reminder WHERE target_id = @EventId",
            new { EventId = eventId }
        );

        // Act
        await _storage.DeleteReminderAsync(reminderId);

        // Assert
        var reminder = await _storage.GetReminderAsync(reminderId);
        Assert.That(reminder, Is.Null);
    }

    #endregion

    #region DeleteRemindersAsync Tests

    [Test]
    public async Task DeleteRemindersAsync_MultipleReminders_DeletesAll()
    {
        // Arrange
        var eventId = Guid.NewGuid().ToString();
        var occurrenceTime1 = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime1 = new DateTime(2026, 1, 25, 9, 30, 0, DateTimeKind.Utc);
        var occurrenceTime2 = new DateTime(2026, 1, 25, 11, 0, 0, DateTimeKind.Utc);
        var triggerTime2 = new DateTime(2026, 1, 25, 10, 30, 0, DateTimeKind.Utc);

        await _storage!.CreateReminderAsync(eventId, occurrenceTime1, triggerTime1);
        await _storage.CreateReminderAsync(eventId, occurrenceTime2, triggerTime2);

        var reminders = await _storage.GetRemindersByEventAsync(eventId);
        var reminderIds = reminders.Select(r => r.ReminderId).ToList();

        // Act
        await _storage.DeleteRemindersAsync(reminderIds);

        // Assert
        var remainingReminders = await _storage.GetRemindersByEventAsync(eventId);
        Assert.That(remainingReminders.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task DeleteRemindersAsync_EmptyList_DoesNothing()
    {
        // Act - should not throw
        await _storage!.DeleteRemindersAsync([]);

        // Assert
        Assert.Pass();
    }

    #endregion
}
