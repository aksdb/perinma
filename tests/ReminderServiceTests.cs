using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CredentialStore;
using Dapper;
using NUnit.Framework;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;

namespace tests;

[TestFixture]
public class ReminderServiceTests
{
    private DatabaseService? _database;
    private SqliteStorage? _storage;
    private CredentialManagerService? _credentialManager;
    private FakeReminderProvider _provider = null!;
    private ReminderService _reminderService = null!;
    private string _eventId = null!;
    private string _calendarId = null!;
    private Guid _accountId;

    [SetUp]
    public async Task SetUp()
    {
        _database = new DatabaseService(inMemory: true);
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        _storage = new SqliteStorage(_database, _credentialManager);
        _provider = new FakeReminderProvider();

        var providers = new Dictionary<AccountType, ICalendarProvider>
        {
            { AccountType.Google, _provider }
        };

        _reminderService = new ReminderService(_storage, providers);

        // Setup test account and calendar
        _accountId = Guid.NewGuid();
        await _storage.CreateAccountAsync(new AccountDbo
        {
            AccountId = _accountId.ToString(),
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        });

        var calendar = new CalendarDbo
        {
            AccountId = _accountId.ToString(),
            CalendarId = Guid.NewGuid().ToString(),
            ExternalId = "cal-123",
            Name = "Test Calendar",
            Color = "#FF0000",
            Enabled = 1
        };
        await _storage.CreateOrUpdateCalendarAsync(calendar);
        _calendarId = calendar.CalendarId;
        _eventId = Guid.NewGuid().ToString();

        // Create a base event in the database
        var startTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var startTimeUnix = new DateTimeOffset(startTime).ToUnixTimeSeconds();
        using var connection = _database.GetConnection();
        await connection.ExecuteAsync(
            "INSERT INTO calendar_event (calendar_id, event_id, start_time, end_time, title) " +
            "VALUES (@CalendarId, @EventId, @StartTime, @EndTime, @Title)",
            new
            {
                CalendarId = _calendarId,
                EventId = _eventId,
                StartTime = startTimeUnix,
                EndTime = startTimeUnix + 3600,
                Title = "Test Event"
            }
        );

        // Set raw data for the event (required for PopulateRemindersForEventAsync)
        await _storage.SetEventData(_eventId, "rawData", "test-raw-event-data");
    }

    [TearDown]
    public void TearDown()
    {
        _database?.Dispose();
        _storage?.Dispose();
    }

    #region PopulateRemindersForEventAsync Tests

    [Test]
    public async Task PopulateRemindersForEventAsync_CreatesNewReminders()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = occurrenceTime.AddMinutes(-30);
        _provider.SetReminderOccurrences([(occurrenceTime, triggerTime)]);
        _provider.SetEventStartTime(occurrenceTime);

        // Act
        await _reminderService.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google);

        // Assert
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(reminders.Count, Is.EqualTo(1));
        Assert.That(reminders[0].TargetId, Is.EqualTo(_eventId));
        var actualTriggerTime = DateTimeOffset.FromUnixTimeSeconds(reminders[0].TriggerTime).DateTime;
        Assert.That(actualTriggerTime, Is.EqualTo(triggerTime).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task PopulateRemindersForEventAsync_MultipleReminders_CreatesAll()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime1 = occurrenceTime.AddMinutes(-30);
        var triggerTime2 = occurrenceTime.AddMinutes(-5);
        _provider.SetReminderOccurrences([(occurrenceTime, triggerTime1), (occurrenceTime, triggerTime2)]);
        _provider.SetEventStartTime(occurrenceTime);

        // Act
        await _reminderService.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google);

        // Assert
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(reminders.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task PopulateRemindersForEventAsync_ExistingReminders_SkipsDuplicates()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = occurrenceTime.AddMinutes(-30);
        _provider.SetReminderOccurrences([(occurrenceTime, triggerTime)]);
        _provider.SetEventStartTime(occurrenceTime);

        // Create initial reminder
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var initialReminders = await _storage.GetRemindersByEventAsync(_eventId);

        // Act
        await _reminderService.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google);
        var finalReminders = await _storage.GetRemindersByEventAsync(_eventId);

        // Assert
        Assert.That(finalReminders.Count, Is.EqualTo(initialReminders.Count));
        Assert.That(finalReminders[0].ReminderId, Is.EqualTo(initialReminders[0].ReminderId));
    }

    [Test]
    public async Task PopulateRemindersForEventAsync_DeletesStaleReminders()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var oldTriggerTime = occurrenceTime.AddMinutes(-60);
        var newTriggerTime = occurrenceTime.AddMinutes(-5);

        // Create old reminder
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, oldTriggerTime);

        // Provider returns only new reminder
        _provider.SetReminderOccurrences([(occurrenceTime, newTriggerTime)]);
        _provider.SetEventStartTime(occurrenceTime);

        // Act
        await _reminderService.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google);

        // Assert
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(reminders.Count, Is.EqualTo(1));
        var actualTriggerTime = DateTimeOffset.FromUnixTimeSeconds(reminders[0].TriggerTime).DateTime;
        Assert.That(actualTriggerTime, Is.EqualTo(newTriggerTime).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task PopulateRemindersForEventAsync_NoOccurrences_DoesNothing()
    {
        // Arrange
        _provider.SetReminderOccurrences([]);

        // Act
        await _reminderService.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google);

        // Assert
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(reminders.Count, Is.EqualTo(0));
    }

    #endregion

    #region DismissReminderAsync Tests

    [Test]
    public async Task DismissReminderAsync_WithAnotherNotificationConfigured_CreatesNewReminder()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var firstTriggerTime = occurrenceTime.AddMinutes(-30);
        var secondTriggerTime = occurrenceTime.AddMinutes(-5);

        // Create reminder that was fired
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, firstTriggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Provider returns next notification
        _provider.SetReminderOccurrences([(occurrenceTime, secondTriggerTime)]);
        _provider.SetEventStartTime(occurrenceTime);

        // Act
        await _reminderService.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        Assert.That(updatedReminders[0].ReminderId, Is.Not.EqualTo(reminderId));
        var actualTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        Assert.That(actualTriggerTime, Is.EqualTo(secondTriggerTime).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task DismissReminderAsync_BeforeMeetingStarts_NoRecurrence_DeletesReminder()
    {
        // Arrange
        var meetingStartTime = DateTime.UtcNow.AddHours(1);
        var occurrenceTime = meetingStartTime;
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);

        // Create reminder for meeting that hasn't started yet
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Provider returns no more occurrences (no recurrence)
        _provider.SetReminderOccurrences([]);
        _provider.SetEventStartTime(meetingStartTime);

        // Act
        await _reminderService.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task DismissReminderAsync_BeforeMeetingStarts_WithRecurrence_CreatesNextReminder()
    {
        // Arrange
        var meetingStartTime = DateTime.UtcNow.AddHours(1);
        var occurrenceTime = meetingStartTime;
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);

        // Create reminder for recurring meeting that hasn't started yet
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Provider returns next occurrence (recurrence)
        var nextOccurrence = meetingStartTime.AddDays(7);
        var nextTrigger = nextOccurrence.AddMinutes(-30);
        _provider.SetReminderOccurrences([(nextOccurrence, nextTrigger)]);
        _provider.SetEventStartTime(nextOccurrence);

        // Act
        await _reminderService.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        Assert.That(updatedReminders[0].ReminderId, Is.Not.EqualTo(reminderId));
        var actualTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        Assert.That(actualTriggerTime, Is.EqualTo(nextTrigger).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task DismissReminderAsync_AfterMeetingStarted_NoRecurrence_DeletesReminder()
    {
        // Arrange
        var meetingStartTime = DateTime.UtcNow.AddMinutes(-30);
        var occurrenceTime = meetingStartTime;
        var triggerTime = meetingStartTime.AddMinutes(-40);

        // Create reminder for meeting that has already started
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Provider returns no more occurrences (no recurrence)
        _provider.SetReminderOccurrences([]);
        _provider.SetEventStartTime(meetingStartTime);

        // Act
        await _reminderService.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task DismissReminderAsync_AfterMeetingStarted_WithRecurrence_CreatesNextReminder()
    {
        // Arrange
        var meetingStartTime = DateTime.UtcNow.AddMinutes(-30);
        var occurrenceTime = meetingStartTime;
        var triggerTime = meetingStartTime.AddMinutes(-40);

        // Create reminder for recurring meeting that has started
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Provider returns next occurrence (recurrence)
        var nextOccurrence = meetingStartTime.AddDays(7);
        var nextTrigger = nextOccurrence.AddMinutes(-30);
        _provider.SetReminderOccurrences([(nextOccurrence, nextTrigger)]);
        _provider.SetEventStartTime(nextOccurrence);

        // Act
        await _reminderService.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        Assert.That(updatedReminders[0].ReminderId, Is.Not.EqualTo(reminderId));
        var actualTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        Assert.That(actualTriggerTime, Is.EqualTo(nextTrigger).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task DismissReminderAsync_NonExistentReminder_DoesNotThrow()
    {
        // Arrange
        var reminderId = "non-existent-id";

        // Act & Assert - should not throw
        Assert.That(async () => await _reminderService.DismissReminderAsync(reminderId), Throws.Nothing);
    }

    #endregion

    #region SnoozeReminderAsync Tests

    [Test]
    public async Task SnoozeReminderAsync_OneMinute_CreatesReminderOneMinuteFromNow()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = DateTime.UtcNow;

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.OneMinute);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        var newTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        var afterSnooze = DateTime.UtcNow;
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.AddMinutes(1).AddSeconds(-1)));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.AddMinutes(1).AddSeconds(1)));
    }

    [Test]
    public async Task SnoozeReminderAsync_FiveMinutes_CreatesReminderFiveMinutesFromNow()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = DateTime.UtcNow;

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.FiveMinutes);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        var afterSnooze = DateTime.UtcNow;
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.AddMinutes(5).AddSeconds(-1)));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.AddMinutes(5).AddSeconds(1)));
    }

    [Test]
    public async Task SnoozeReminderAsync_TenMinutes_CreatesReminderTenMinutesFromNow()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = DateTime.UtcNow;

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.TenMinutes);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        var afterSnooze = DateTime.UtcNow;
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.AddMinutes(10).AddSeconds(-1)));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.AddMinutes(10).AddSeconds(1)));
    }

    [Test]
    public async Task SnoozeReminderAsync_FifteenMinutes_CreatesReminderFifteenMinutesFromNow()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = DateTime.UtcNow;

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.FifteenMinutes);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        var afterSnooze = DateTime.UtcNow;
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.AddMinutes(15).AddSeconds(-1)));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.AddMinutes(15).AddSeconds(1)));
    }

    [Test]
    public async Task SnoozeReminderAsync_ThirtyMinutes_CreatesReminderThirtyMinutesFromNow()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = DateTime.UtcNow;

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.ThirtyMinutes);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        var afterSnooze = DateTime.UtcNow;
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.AddMinutes(30).AddSeconds(-1)));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.AddMinutes(30).AddSeconds(1)));
    }

    [Test]
    public async Task SnoozeReminderAsync_OneHour_CreatesReminderOneHourFromNow()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = DateTime.UtcNow;

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.OneHour);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        var afterSnooze = DateTime.UtcNow;
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.AddHours(1).AddSeconds(-1)));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.AddHours(1).AddSeconds(1)));
    }

    [Test]
    public async Task SnoozeReminderAsync_TwoHours_CreatesReminderTwoHoursFromNow()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = DateTime.UtcNow;

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.TwoHours);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        var afterSnooze = DateTime.UtcNow;
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.AddHours(2).AddSeconds(-1)));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.AddHours(2).AddSeconds(1)));
    }

    [Test]
    public async Task SnoozeReminderAsync_Tomorrow_CreatesReminderTomorrowMidnight()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var expectedTomorrow = DateTime.UtcNow.AddDays(1).Date;

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.Tomorrow);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        Assert.That(newTriggerTime.Date, Is.EqualTo(expectedTomorrow.Date));
    }

    [Test]
    public async Task SnoozeReminderAsync_OneMinuteBeforeStart_CreatesReminderOneMinuteBeforeEvent()
    {
        // Arrange
        var meetingStartTime = DateTime.UtcNow.AddHours(2);
        var occurrenceTime = meetingStartTime;
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var expectedTriggerTime = meetingStartTime.AddMinutes(-1);

        // Provider needs to return event start time for this snooze interval
        _provider.SetEventStartTime(meetingStartTime);

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.OneMinuteBeforeStart);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        var newTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        Assert.That(newTriggerTime, Is.EqualTo(expectedTriggerTime).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task SnoozeReminderAsync_WhenItStarts_CreatesReminderAtEventStartTime()
    {
        // Arrange
        var meetingStartTime = DateTime.UtcNow.AddHours(2);
        var occurrenceTime = meetingStartTime;
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var expectedTriggerTime = meetingStartTime;

        // Provider needs to return event start time for this snooze interval
        _provider.SetEventStartTime(meetingStartTime);

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.WhenItStarts);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        var newTriggerTime = DateTimeOffset.FromUnixTimeSeconds(updatedReminders[0].TriggerTime).DateTime;
        Assert.That(newTriggerTime, Is.EqualTo(expectedTriggerTime).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task SnoozeReminderAsync_NonExistentReminder_DoesNotThrow()
    {
        // Arrange
        var reminderId = "non-existent-id";

        // Act & Assert - should not throw
        Assert.That(async () => await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.FiveMinutes), Throws.Nothing);
    }

    #endregion

    #region GetDueRemindersAsync Tests

    [Test]
    public async Task GetDueRemindersAsync_ReturnsDueReminders()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);

        // Act
        var dueReminders = await _reminderService.GetDueRemindersAsync();

        // Assert
        Assert.That(dueReminders.Count, Is.EqualTo(1));
        Assert.That(dueReminders[0].TargetId, Is.EqualTo(_eventId));
    }

    [Test]
    public async Task GetDueRemindersAsync_MarksRemindersAsFired()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);

        // Act
        var dueReminders1 = await _reminderService.GetDueRemindersAsync();
        var dueReminders2 = await _reminderService.GetDueRemindersAsync();

        // Assert - second call should return empty because reminder was marked as fired
        Assert.That(dueReminders1.Count, Is.EqualTo(1));
        Assert.That(dueReminders2.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task GetDueRemindersAsync_NoDueReminders_ReturnsEmpty()
    {
        // Arrange - create reminder in the future
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddHours(1);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);

        // Act
        var dueReminders = await _reminderService.GetDueRemindersAsync();

        // Assert
        Assert.That(dueReminders.Count, Is.EqualTo(0));
    }

    #endregion

    #region ClearFiredReminders Tests

    [Test]
    public async Task ClearFiredReminders_ResetsFiredRemindersSet()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var triggerTime = DateTime.UtcNow.AddMinutes(-10);
        await _storage.CreateReminderAsync(_eventId, occurrenceTime, triggerTime);

        // Get reminders to mark them as fired
        await _reminderService.GetDueRemindersAsync();

        // Act
        _reminderService.ClearFiredReminders();

        // Assert - should be able to get the same reminder again
        var dueReminders = await _reminderService.GetDueRemindersAsync();
        Assert.That(dueReminders.Count, Is.EqualTo(1));
    }

    #endregion

    #region GetEventStartTimeAsync Tests

    [Test]
    public async Task GetEventStartTimeAsync_WithValidData_ReturnsStartTime()
    {
        // Arrange
        var occurrenceTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var expectedStartTime = new DateTimeOffset(occurrenceTime);
        _provider.SetEventStartTime(occurrenceTime);

        // Store raw data
        await _storage.SetEventData(_eventId, "rawData", "test-raw-data");

        // Act
        var startTime = await _reminderService.GetEventStartTimeAsync(_eventId, occurrenceTime, AccountType.Google);

        // Assert
        Assert.That(startTime, Is.Not.Null);
        Assert.That(startTime.Value.Kind, Is.EqualTo(DateTimeKind.Local));
        var expectedLocalTime = expectedStartTime.LocalDateTime;
        Assert.That(startTime, Is.EqualTo(expectedLocalTime).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task GetEventStartTimeAsync_WithNoRawData_ReturnsNull()
    {
        // Arrange - no raw data stored

        // Act
        var startTime = await _reminderService.GetEventStartTimeAsync(_eventId, DateTime.UtcNow, AccountType.Google);

        // Assert
        Assert.That(startTime, Is.Null);
    }

    [Test]
    public async Task GetEventStartTimeAsync_WithUnknownAccountType_ReturnsNull()
    {
        // Arrange
        await _storage.SetEventData(_eventId, "rawData", "test-raw-data");

        // Act
        var startTime = await _reminderService.GetEventStartTimeAsync(_eventId, DateTime.UtcNow, AccountType.CalDav);

        // Assert
        Assert.That(startTime, Is.Null);
    }

    #endregion
}

#region FakeReminderProvider

/// <summary>
/// Fake implementation of ICalendarProvider for testing ReminderService.
/// </summary>
public class FakeReminderProvider : ICalendarProvider
{
    private readonly CredentialManagerService _credentialManager;
    private List<(DateTime Occurrence, DateTime TriggerTime)> _reminderOccurrences = [];
    private DateTime? _eventStartTime;

    public FakeReminderProvider()
    {
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
    }

    public CredentialManagerService CredentialManager => _credentialManager;

    public void SetReminderOccurrences(List<(DateTime Occurrence, DateTime TriggerTime)> occurrences)
    {
        _reminderOccurrences = new List<(DateTime Occurrence, DateTime TriggerTime)>(occurrences);
    }

    public void SetEventStartTime(DateTime startTime)
    {
        _eventStartTime = startTime;
    }

    public Task<CalendarSyncResult> GetCalendarsAsync(
        string accountId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<EventSyncResult> GetEventsAsync(
        string accountId,
        string calendarExternalId,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> TestConnectionAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IList<int>> GetReminderMinutesAsync(
        string rawEventData,
        string? rawCalendarData = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IList<int>>([]);
    }

    public Task<DateTimeOffset?> GetEventStartTimeAsync(
        string rawEventData,
        DateTime? occurrenceTime = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset? result = _eventStartTime.HasValue ? new DateTimeOffset(_eventStartTime.Value) : null;
        return Task.FromResult(result);
    }

    public Task<IList<(DateTime Occurrence, DateTime TriggerTime)>> GetNextReminderOccurrencesAsync(
        string rawEventData,
        string? rawCalendarData = null,
        DateTime referenceTime = default,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IList<(DateTime Occurrence, DateTime TriggerTime)>>(_reminderOccurrences);
    }

    public Task RespondToEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string rawEventData,
        string responseStatus,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<string> CreateEventAsync(
        string accountId,
        string calendarId,
        string title,
        string? description,
        string? location,
        DateTime startTime,
        DateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task UpdateEventAsync(
        string accountId,
        string calendarId,
        string eventId,
        string title,
        string? description,
        string? location,
        DateTime startTime,
        DateTime endTime,
        string? rawEventData = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}

#endregion
