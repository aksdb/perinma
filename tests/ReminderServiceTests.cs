using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CredentialStore;
using Dapper;
using Google.Apis.Json;
using NUnit.Framework;
using NodaTime;
using perinma.Models;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Storage.Models;
using tests.Fakes;

using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;
using GoogleEventDateTime = Google.Apis.Calendar.v3.Data.EventDateTime;

namespace tests;

[TestFixture]
public class ReminderServiceTests
{
    private DatabaseService? _database;
    private SqliteStorage? _storage;
    private CredentialManagerService? _credentialManager;
    private GoogleCalendarServiceStub? _googleServiceStub;
    private CalDavServiceStub? _calDavServiceStub;
    private Dictionary<AccountType, ICalendarProvider>? _providers;
    private ReminderService? _reminderService;
    private string _eventId = null!;
    private string _calendarId = null!;
    private Guid _accountId;

    [SetUp]
    public async Task SetUp()
    {
        _database = new DatabaseService(inMemory: true);
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        _storage = new SqliteStorage(_database, _credentialManager);
        
        _googleServiceStub = new GoogleCalendarServiceStub();
        _calDavServiceStub = new CalDavServiceStub();
        
        var googleProvider = new GoogleCalendarProvider(_googleServiceStub, _credentialManager);
        var calDavProvider = new CalDavCalendarProvider(_calDavServiceStub, _credentialManager);
        
        _providers = new Dictionary<AccountType, ICalendarProvider>
        {
            { AccountType.Google, googleProvider },
            { AccountType.CalDav, calDavProvider }
        };

        _reminderService = new ReminderService(_storage, _providers);

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
        // Arrange - Create Google event with reminder
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() },
            Reminders = new GoogleEvent.RemindersData
            {
                UseDefault = false,
                Overrides = new[] { new Google.Apis.Calendar.v3.Data.EventReminder { Method = "popup", Minutes = 30 } }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        // Act - Pass reference time in the past so reminders are considered
        await _reminderService.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google,
            referenceTime: eventStartTime.Minus(Duration.FromHours(1)));

        // Assert
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(reminders.Count, Is.EqualTo(1));
        Assert.That(reminders[0].TargetId, Is.EqualTo(_eventId));
        var actualTriggerTime = Instant.FromUnixTimeSeconds(reminders[0].TriggerTime);
        var expectedTriggerTime = eventStartTime.Minus(Duration.FromMinutes(30));
        Assert.That(actualTriggerTime, Is.EqualTo(expectedTriggerTime));
    }

    [Test]
    public async Task PopulateRemindersForEventAsync_MultipleReminders_CreatesAll()
    {
        // Arrange - Create Google event with multiple reminders
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() },
            Reminders = new GoogleEvent.RemindersData
            {
                UseDefault = false,
                Overrides = new[]
                {
                    new Google.Apis.Calendar.v3.Data.EventReminder { Method = "popup", Minutes = 30 },
                    new Google.Apis.Calendar.v3.Data.EventReminder { Method = "popup", Minutes = 5 }
                }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        // Act - Pass reference time in the past so reminders are considered
        await _reminderService.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google,
            referenceTime: eventStartTime.Minus(Duration.FromHours(1)));

        // Assert
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(reminders.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task PopulateRemindersForEventAsync_ExistingReminders_SkipsDuplicates()
    {
        // Arrange - Create Google event with reminder
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var triggerTime = eventStartTime.Minus(Duration.FromMinutes(30));
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() },
            Reminders = new GoogleEvent.RemindersData
            {
                UseDefault = false,
                Overrides = new[] { new Google.Apis.Calendar.v3.Data.EventReminder { Method = "popup", Minutes = 30 } }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        // Create initial reminder
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
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
        // Arrange - Create Google event with 5 minute reminder
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var oldTriggerTime = eventStartTime.Minus(Duration.FromMinutes(60));
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() },
            Reminders = new GoogleEvent.RemindersData
            {
                UseDefault = false,
                Overrides = new[] { new Google.Apis.Calendar.v3.Data.EventReminder { Method = "popup", Minutes = 5 } }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        // Create old reminder (60 minutes before)
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), oldTriggerTime.ToDateTimeUtc());

        // Act - Use reference time before event start to include future reminders
        await _reminderService.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google,
            referenceTime: eventStartTime.Minus(Duration.FromHours(2)));

        // Assert
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(reminders.Count, Is.EqualTo(1));
        var actualTriggerTime = Instant.FromUnixTimeSeconds(reminders[0].TriggerTime);
        var newTriggerTime = eventStartTime.Minus(Duration.FromMinutes(5));
        Assert.That(actualTriggerTime, Is.EqualTo(newTriggerTime));
    }

    [Test]
    public async Task PopulateRemindersForEventAsync_NoOccurrences_DoesNothing()
    {
        // Arrange - Create Google event with NO reminders
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
            // No Reminders property - means no reminders
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

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
        // Arrange - Create Google event with multiple reminders
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() },
            Reminders = new GoogleEvent.RemindersData
            {
                UseDefault = false,
                Overrides = new[]
                {
                    new Google.Apis.Calendar.v3.Data.EventReminder { Method = "popup", Minutes = 30 },
                    new Google.Apis.Calendar.v3.Data.EventReminder { Method = "popup", Minutes = 5 }
                }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder that was fired (30 minute reminder already past)
        var firstTriggerTime = eventStartTime.Minus(Duration.FromMinutes(30));
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), firstTriggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Act - dismiss should create the 5-minute reminder
        await _reminderService.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        Assert.That(updatedReminders[0].ReminderId, Is.Not.EqualTo(reminderId));
        var actualTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var secondTriggerTime = eventStartTime.Minus(Duration.FromMinutes(5));
        Assert.That(actualTriggerTime, Is.EqualTo(secondTriggerTime));
    }

    [Test]
    public async Task DismissReminderAsync_BeforeMeetingStarts_NoRecurrence_DeletesReminder()
    {
        // Arrange - Create non-recurring Google event that's starting in 1 hour
        var meetingStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
            // No recurrence = no more occurrences
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder that was already fired (before event starts, no recurrence means no more reminders)
        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50); // 10 minutes before
        await _storage.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Act - dismiss should delete reminder (no more occurrences for non-recurring event)
        await _reminderService.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task DismissReminderAsync_BeforeMeetingStarts_WithRecurrence_CreatesNextReminder()
    {
        // Arrange - Create recurring Google event (weekly)
        var meetingStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.ToDateTimeUtc(), TimeZone = "UTC" },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc(), TimeZone = "UTC" },
            Recurrence = new List<string> { "RRULE:FREQ=WEEKLY;COUNT=5" },
            Reminders = new GoogleEvent.RemindersData
            {
                UseDefault = false,
                Overrides = new[] { new Google.Apis.Calendar.v3.Data.EventReminder { Method = "popup", Minutes = 30 } }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder for first occurrence that was already fired
        var triggerTime = meetingStartTime.Minus(Duration.FromMinutes(30));
        await _storage.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Act - dismiss should create reminder for next week's occurrence
        await _reminderService.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        Assert.That(updatedReminders[0].ReminderId, Is.Not.EqualTo(reminderId));
        var actualTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var nextOccurrence = meetingStartTime.Plus(Duration.FromDays(7));
        var nextTrigger = nextOccurrence.Minus(Duration.FromMinutes(30));
        Assert.That(actualTriggerTime, Is.EqualTo(nextTrigger));
    }

    [Test]
    public async Task DismissReminderAsync_AfterMeetingStarted_NoRecurrence_DeletesReminder()
    {
        // Arrange - Create non-recurring Google event that started 30 minutes ago
        var meetingStartTime = Instant.FromUtc(2026, 1, 25, 9, 30);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder for meeting that has already started
        var triggerTime = meetingStartTime.Minus(Duration.FromMinutes(10));
        await _storage.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Act
        await _reminderService.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task DismissReminderAsync_AfterMeetingStarted_WithRecurrence_CreatesNextReminder()
    {
        // Arrange - Create recurring Google event (weekly) that started 30 minutes ago
        var meetingStartTime = Instant.FromUtc(2026, 1, 25, 9, 30);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.ToDateTimeUtc(), TimeZone = "UTC" },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc(), TimeZone = "UTC" },
            Recurrence = new List<string> { "RRULE:FREQ=WEEKLY;COUNT=5" },
            Reminders = new GoogleEvent.RemindersData
            {
                UseDefault = false,
                Overrides = new[] { new Google.Apis.Calendar.v3.Data.EventReminder { Method = "popup", Minutes = 30 } }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder for first occurrence
        var triggerTime = meetingStartTime.Minus(Duration.FromMinutes(10));
        await _storage.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Act
        await _reminderService.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        Assert.That(updatedReminders[0].ReminderId, Is.Not.EqualTo(reminderId));
        var actualTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var nextOccurrence = meetingStartTime.Plus(Duration.FromDays(7));
        var nextTrigger = nextOccurrence.Minus(Duration.FromMinutes(30));
        Assert.That(actualTriggerTime, Is.EqualTo(nextTrigger));
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
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = SystemClock.Instance.GetCurrentInstant();

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.OneMinute);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = SystemClock.Instance.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromMinutes(1)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromMinutes(1)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_FiveMinutes_CreatesReminderFiveMinutesFromNow()
    {
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = SystemClock.Instance.GetCurrentInstant();

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.FiveMinutes);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = SystemClock.Instance.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromMinutes(5)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromMinutes(5)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_TenMinutes_CreatesReminderTenMinutesFromNow()
    {
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = SystemClock.Instance.GetCurrentInstant();

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.TenMinutes);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = SystemClock.Instance.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromMinutes(10)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromMinutes(10)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_FifteenMinutes_CreatesReminderFifteenMinutesFromNow()
    {
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = SystemClock.Instance.GetCurrentInstant();

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.FifteenMinutes);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = SystemClock.Instance.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromMinutes(15)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromMinutes(15)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_ThirtyMinutes_CreatesReminderThirtyMinutesFromNow()
    {
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = SystemClock.Instance.GetCurrentInstant();

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.ThirtyMinutes);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = SystemClock.Instance.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromMinutes(30)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromMinutes(30)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_OneHour_CreatesReminderOneHourFromNow()
    {
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = SystemClock.Instance.GetCurrentInstant();

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.OneHour);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = SystemClock.Instance.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromHours(1)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromHours(1)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_TwoHours_CreatesReminderTwoHoursFromNow()
    {
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = SystemClock.Instance.GetCurrentInstant();

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.TwoHours);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = SystemClock.Instance.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromHours(2)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromHours(2)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_Tomorrow_CreatesReminderTomorrowMidnight()
    {
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var expectedTomorrow = SystemClock.Instance.GetCurrentInstant().InUtc().Date.PlusDays(1).AtMidnight().InUtc().ToInstant();

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.Tomorrow);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        Assert.That(newTriggerTime.InUtc().Date, Is.EqualTo(expectedTomorrow.InUtc().Date));
    }

    [Test]
    public async Task SnoozeReminderAsync_OneMinuteBeforeStart_CreatesReminderOneMinuteBeforeEvent()
    {
        // Arrange - Create Google event starting in 2 hours
        var meetingStartTime = Instant.FromUtc(2026, 1, 25, 12, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 11, 50);
        await _storage.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var expectedTriggerTime = meetingStartTime.Minus(Duration.FromMinutes(1));

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.OneMinuteBeforeStart);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        Assert.That(newTriggerTime, Is.EqualTo(expectedTriggerTime));
    }

    [Test]
    public async Task SnoozeReminderAsync_WhenItStarts_CreatesReminderAtEventStartTime()
    {
        // Arrange - Create Google event starting in 2 hours
        var meetingStartTime = Instant.FromUtc(2026, 1, 25, 12, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = meetingStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 11, 50);
        await _storage.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var expectedTriggerTime = meetingStartTime;

        // Act
        await _reminderService.SnoozeReminderAsync(reminderId, SnoozeInterval.WhenItStarts);

        // Assert
        var updatedReminders = await _storage.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        Assert.That(newTriggerTime, Is.EqualTo(expectedTriggerTime));
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
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());

        // Act
        var dueReminders = await _reminderService.GetDueRemindersAsync();

        // Assert
        Assert.That(dueReminders.Count, Is.EqualTo(1));
        Assert.That(dueReminders[0].TargetId, Is.EqualTo(_eventId));
    }

    [Test]
    public async Task GetDueRemindersAsync_MarksRemindersAsFired()
    {
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());

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
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder in the future
        var triggerTime = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromHours(1));
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());

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
        // Arrange - Create simple Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());

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
        // Arrange - Create Google event
        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage.SetEventData(_eventId, "rawData", rawEventJson);

        // Act
        var startTime = await _reminderService.GetEventStartTimeAsync(_eventId, eventStartTime, AccountType.Google);

        // Assert
        Assert.That(startTime, Is.Not.Null);
        Assert.That(startTime.Value, Is.EqualTo(eventStartTime));
    }

    [Test]
    public async Task GetEventStartTimeAsync_WithNoRawData_ReturnsNull()
    {
        // Arrange - no raw data stored

        // Act
        var startTime = await _reminderService.GetEventStartTimeAsync(_eventId, SystemClock.Instance.GetCurrentInstant(), AccountType.Google);

        // Assert
        Assert.That(startTime, Is.Null);
    }

    [Test]
    public async Task GetEventStartTimeAsync_WithUnknownAccountType_ReturnsNull()
    {
        // Arrange
        await _storage.SetEventData(_eventId, "rawData", "test-raw-data");

        // Act
        var startTime = await _reminderService.GetEventStartTimeAsync(_eventId, SystemClock.Instance.GetCurrentInstant(), AccountType.CalDav);

        // Assert
        Assert.That(startTime, Is.Null);
    }

    #endregion
}


