using CredentialStore;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using NodaTime;
using NodaTime.Testing;
using perinma.Models;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Storage.Models;
using tests.Fakes;
using tests.Helpers;
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
    private SyncService? _syncService;
    private FakeClock _clock = null!;
    private string _eventId = null!;
    private string _calendarId = null!;
    private Guid _accountId;

    [SetUp]
    public async Task SetUp()
    {
        _clock = new FakeClock(Instant.FromUtc(2026, 1, 1, 0, 0));

        _database = new DatabaseService(inMemory: true);
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        _storage = new SqliteStorage(_database, _credentialManager);

        _googleServiceStub = new GoogleCalendarServiceStub();
        _calDavServiceStub = new CalDavServiceStub();

        _clock = new FakeClock(Instant.FromUtc(2026, 1, 1, 0, 0));
        
        var googleProvider = new GoogleCalendarProvider(_googleServiceStub, _credentialManager, _clock);
        var calDavProvider = new CalDavCalendarProvider(_calDavServiceStub, _credentialManager);

        _providers = new Dictionary<AccountType, ICalendarProvider>
        {
            { AccountType.Google, googleProvider },
            { AccountType.CalDav, calDavProvider }
        };

        _reminderService = new ReminderService(_storage, _providers, _clock);
        _syncService = new SyncService(_storage, _credentialManager, _providers, _reminderService);

        // Setup test account and calendar
        _accountId = Guid.NewGuid();
        await _storage!.CreateAccountAsync(new AccountDbo
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
        await _storage!.CreateOrUpdateCalendarAsync(calendar);
        _calendarId = calendar.CalendarId;

        // Create a base event in the database
        var startTime = new DateTime(2026, 1, 25, 10, 0, 0, DateTimeKind.Utc);
        var startTimeUnix = new DateTimeOffset(startTime).ToUnixTimeSeconds();
        _eventId = await _storage!.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = _calendarId,
            StartTime = startTimeUnix,
            EndTime = startTimeUnix + 3600,
            Title = "Test Event",
        });
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
                Overrides = new[] { new EventReminder { Method = "popup", Minutes = 30 } }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Act - Pass reference time in the past so reminders are considered
        await _reminderService!.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google,
            referenceTime: eventStartTime.Minus(Duration.FromHours(1)));

        // Assert
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
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
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = new[]
                {
                    new EventReminder { Method = "popup", Minutes = 30 },
                    new EventReminder { Method = "popup", Minutes = 5 }
                }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Act - Pass reference time in the past so reminders are considered
        await _reminderService!.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google,
            referenceTime: eventStartTime.Minus(Duration.FromHours(1)));

        // Assert
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
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
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = new[] { new EventReminder { Method = "popup", Minutes = 30 } }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Create initial reminder
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var initialReminders = await _storage!.GetRemindersByEventAsync(_eventId);

        // Act
        await _reminderService!.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google);
        var finalReminders = await _storage!.GetRemindersByEventAsync(_eventId);

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
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = new[] { new EventReminder { Method = "popup", Minutes = 5 } }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Create old reminder (60 minutes before)
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), oldTriggerTime.ToDateTimeUtc());

        // Act - Use reference time before event start to include future reminders
        await _reminderService!.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google,
            referenceTime: eventStartTime.Minus(Duration.FromHours(2)));

        // Assert
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Act
        await _reminderService!.PopulateRemindersForEventAsync(_eventId, _calendarId, AccountType.Google);

        // Assert
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        Assert.That(reminders.Count, Is.EqualTo(0));
    }

    #endregion

    #region DismissReminderAsync Tests

    [Test]
    [Ignore("We don't do this for now. Rethink it later")] // TODO rethink it later
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
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides =
                [
                    new EventReminder { Method = "popup", Minutes = 30 },
                    new EventReminder { Method = "popup", Minutes = 5 }
                ]
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder that was fired (30 minute reminder already past)
        var firstTriggerTime = eventStartTime.Minus(Duration.FromMinutes(30));
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), firstTriggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Act - dismiss should create the 5-minute reminder
        await _reminderService!.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder that was already fired (before event starts, no recurrence means no more reminders)
        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50); // 10 minutes before
        await _storage!.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Act - dismiss should delete reminder (no more occurrences for non-recurring event)
        await _reminderService!.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
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
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = new[] { new EventReminder { Method = "popup", Minutes = 30 } }
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder for first occurrence that was already fired
        var triggerTime = meetingStartTime.Minus(Duration.FromMinutes(30));
        await _storage!.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Act - dismiss should create reminder for next week's occurrence
        await _reminderService!.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder for meeting that has already started
        var triggerTime = meetingStartTime.Minus(Duration.FromMinutes(10));
        await _storage!.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Act
        await _reminderService!.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
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
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = [new EventReminder { Method = "popup", Minutes = 10 }]
            }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder for first occurrence
        var triggerTime = meetingStartTime.Minus(Duration.FromMinutes(10));
        await _storage!.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        // Act
        await _reminderService!.DismissReminderAsync(reminderId);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        Assert.That(updatedReminders[0].ReminderId, Is.Not.EqualTo(reminderId));
        var actualTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var nextOccurrence = meetingStartTime.Plus(Duration.FromDays(7));
        var nextTrigger = nextOccurrence.Minus(Duration.FromMinutes(10));
        Assert.That(actualTriggerTime, Is.EqualTo(nextTrigger));
    }

    [Test]
    public async Task DismissReminderAsync_NonExistentReminder_DoesNotThrow()
    {
        // Arrange
        var reminderId = "non-existent-id";

        // Act & Assert - should not throw
        Assert.That(async () => await _reminderService!.DismissReminderAsync(reminderId), Throws.Nothing);
    }

    #endregion

    #region SnoozeReminderAsync Tests

    [Test]
    public async Task SnoozeReminderAsync_OneMinute_CreatesReminderOneMinuteFromNow()
    {
        _clock.Reset(Instant.FromUtc(2026, 1, 25, 10, 0));

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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = _clock.GetCurrentInstant();

        // Act
        await _reminderService!.SnoozeReminderAsync(reminderId, SnoozeInterval.OneMinute);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = _clock.GetCurrentInstant();
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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = _clock.GetCurrentInstant();

        // Act
        await _reminderService!.SnoozeReminderAsync(reminderId, SnoozeInterval.FiveMinutes);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
        Assert.That(updatedReminders.Count, Is.EqualTo(1));
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = _clock.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromMinutes(5)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromMinutes(5)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_TenMinutes_CreatesReminderTenMinutesFromNow()
    {
        _clock.Reset(Instant.FromUtc(2026, 1, 25, 10, 0));

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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = _clock.GetCurrentInstant();

        // Act
        await _reminderService!.SnoozeReminderAsync(reminderId, SnoozeInterval.TenMinutes);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = _clock.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromMinutes(10)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromMinutes(10)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_FifteenMinutes_CreatesReminderFifteenMinutesFromNow()
    {
        _clock.Reset(Instant.FromUtc(2026, 1, 25, 10, 0));

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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = _clock.GetCurrentInstant();

        // Act
        await _reminderService!.SnoozeReminderAsync(reminderId, SnoozeInterval.FifteenMinutes);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = _clock.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromMinutes(15)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromMinutes(15)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_ThirtyMinutes_CreatesReminderThirtyMinutesFromNow()
    {
        _clock.Reset(Instant.FromUtc(2026, 1, 25, 10, 0));

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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = _clock.GetCurrentInstant();

        // Act
        await _reminderService!.SnoozeReminderAsync(reminderId, SnoozeInterval.ThirtyMinutes);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = _clock.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromMinutes(30)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromMinutes(30)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_OneHour_CreatesReminderOneHourFromNow()
    {
        _clock.Reset(Instant.FromUtc(2026, 1, 25, 10, 0));

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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = _clock.GetCurrentInstant();

        // Act
        await _reminderService!.SnoozeReminderAsync(reminderId, SnoozeInterval.OneHour);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = _clock.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromHours(1)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromHours(1)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_TwoHours_CreatesReminderTwoHoursFromNow()
    {
        _clock.Reset(Instant.FromUtc(2026, 1, 25, 10, 0));

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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var beforeSnooze = _clock.GetCurrentInstant();

        // Act
        await _reminderService!.SnoozeReminderAsync(reminderId, SnoozeInterval.TwoHours);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var newTriggerTime = Instant.FromUnixTimeSeconds(updatedReminders[0].TriggerTime);
        var afterSnooze = _clock.GetCurrentInstant();
        Assert.That(newTriggerTime, Is.GreaterThanOrEqualTo(beforeSnooze.Plus(Duration.FromHours(2)).Minus(Duration.FromSeconds(1))));
        Assert.That(newTriggerTime, Is.LessThanOrEqualTo(afterSnooze.Plus(Duration.FromHours(2)).Plus(Duration.FromSeconds(1))));
    }

    [Test]
    public async Task SnoozeReminderAsync_Tomorrow_CreatesReminderTomorrowMidnight()
    {
        _clock.Reset(Instant.FromUtc(2026, 1, 25, 10, 0));

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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var expectedTomorrow = _clock.GetCurrentInstant().InUtc().Date.PlusDays(1).AtMidnight().InUtc().ToInstant();

        // Act
        await _reminderService!.SnoozeReminderAsync(reminderId, SnoozeInterval.Tomorrow);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 11, 50);
        await _storage!.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var expectedTriggerTime = meetingStartTime.Minus(Duration.FromMinutes(1));

        // Act
        await _reminderService!.SnoozeReminderAsync(reminderId, SnoozeInterval.OneMinuteBeforeStart);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 11, 50);
        await _storage!.CreateReminderAsync(_eventId, meetingStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());
        var reminders = await _storage!.GetRemindersByEventAsync(_eventId);
        var reminderId = reminders[0].ReminderId;

        var expectedTriggerTime = meetingStartTime;

        // Act
        await _reminderService!.SnoozeReminderAsync(reminderId, SnoozeInterval.WhenItStarts);

        // Assert
        var updatedReminders = await _storage!.GetRemindersByEventAsync(_eventId);
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
        Assert.That(async () => await _reminderService!.SnoozeReminderAsync(reminderId, SnoozeInterval.FiveMinutes), Throws.Nothing);
    }

    #endregion

    #region GetDueRemindersAsync Tests

    [Test]
    public async Task GetDueRemindersAsync_ReturnsDueReminders()
    {
        // Arrange - Create simple Google event
        _clock.Reset(Instant.FromUtc(2026, 1, 25, 10, 0));

        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());

        // Act
        var dueReminders = await _reminderService!.GetDueRemindersAsync();

        // Assert
        Assert.That(dueReminders.Count, Is.EqualTo(1));
        Assert.That(dueReminders[0].TargetId, Is.EqualTo(_eventId));
    }

    [Test]
    public async Task GetDueRemindersAsync_MarksRemindersAsFired()
    {
        // Arrange - Create simple Google event
        _clock.Reset(Instant.FromUtc(2026, 1, 25, 10, 0));

        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());

        // Act
        var dueReminders1 = await _reminderService!.GetDueRemindersAsync();
        var dueReminders2 = await _reminderService!.GetDueRemindersAsync();

        // Assert - second call should return empty because reminder was marked as fired
        Assert.That(dueReminders1.Count, Is.EqualTo(1));
        Assert.That(dueReminders2.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task GetDueRemindersAsync_NoDueReminders_ReturnsEmpty()
    {
        // Arrange - Create simple Google event
        _clock.Reset(Instant.FromUtc(2026, 1, 25, 10, 0));

        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Create reminder in the future
        var triggerTime = _clock.GetCurrentInstant().Plus(Duration.FromHours(1));
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());

        // Act
        var dueReminders = await _reminderService!.GetDueRemindersAsync();

        // Assert
        Assert.That(dueReminders.Count, Is.EqualTo(0));
    }

    #endregion

    #region ClearFiredReminders Tests

    [Test]
    public async Task ClearFiredReminders_ResetsFiredRemindersSet()
    {
        // Arrange - Create simple Google event
        _clock.Reset(Instant.FromUtc(2026, 1, 25, 10, 0));

        var eventStartTime = Instant.FromUtc(2026, 1, 25, 10, 0);
        var googleEvent = new GoogleEvent
        {
            Id = "test-event",
            Summary = "Test Event",
            Start = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.ToDateTimeUtc() },
            End = new GoogleEventDateTime { DateTimeDateTimeOffset = eventStartTime.Plus(Duration.FromHours(1)).ToDateTimeUtc() }
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        var triggerTime = Instant.FromUtc(2026, 1, 25, 9, 50);
        await _storage!.CreateReminderAsync(_eventId, eventStartTime.ToDateTimeUtc(), triggerTime.ToDateTimeUtc());

        // Get reminders to mark them as fired
        await _reminderService!.GetDueRemindersAsync();

        // Act
        _reminderService.ClearFiredReminders();

        // Assert - should be able to get the same reminder again
        var dueReminders = await _reminderService!.GetDueRemindersAsync();
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
        await _storage!.SetEventData(_eventId, "rawData", rawEventJson);

        // Act
        var startTime = await _reminderService!.GetEventStartTimeAsync(_eventId, eventStartTime, AccountType.Google);

        // Assert
        Assert.That(startTime, Is.Not.Null);
        Assert.That(startTime.Value, Is.EqualTo(eventStartTime));
    }

    [Test]
    public async Task GetEventStartTimeAsync_WithNoRawData_ReturnsNull()
    {
        // Arrange - no raw data stored

        // Act
        var startTime = await _reminderService!.GetEventStartTimeAsync(_eventId, _clock.GetCurrentInstant(), AccountType.Google);

        // Assert
        Assert.That(startTime, Is.Null);
    }

    [Test]
    [Ignore("That's no longer a useful approach to test this. Revisit later.")]
    // TODO can this scenario even happen anymore? Is this the right spot to handle it?
    public async Task GetEventStartTimeAsync_WithUnknownAccountType_ReturnsNull()
    {
        // Arrange
        await _storage!.SetEventData(_eventId, "rawData", "test-raw-data");

        // Act
        var startTime = await _reminderService!.GetEventStartTimeAsync(_eventId, _clock.GetCurrentInstant(), AccountType.CalDav);

        // Assert
        Assert.That(startTime, Is.Null);
    }

    #endregion

    #region Recurring Event With Override Tests

    private async Task<AccountDbo> CreateGoogleAccountAsync()
    {
        var accountId = Guid.NewGuid().ToString();
        await _storage!.CreateAccountAsync(new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = nameof(AccountType.Google)
        });
        return (await _storage.GetAccountByIdAsync(accountId))!;
    }

    private void StoreGoogleCredentials(string accountId)
    {
        _credentialManager!.StoreGoogleCredentials(accountId, new GoogleCredentials
        {
            Type = "Google",
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer",
            Scope = "calendar.readonly"
        });
    }

    [Test]
    public async Task RecurringEventWithOverride_DismissFlow_WorksCorrectly()
    {
        var firstOccurrenceStart = Instant.FromUtc(2026, 2, 1, 10, 0);
        var secondOccurrenceOriginalStart = Instant.FromUtc(2026, 2, 8, 10, 0);
        var secondOccurrenceNewStart = Instant.FromUtc(2026, 2, 8, 9, 30);

        var masterEvent = new GoogleEvent
        {
            Id = "master-event",
            Summary = "Weekly Team Meeting",
            Status = "confirmed",
            Start = new GoogleEventDateTime
            {
                DateTimeDateTimeOffset = firstOccurrenceStart.ToDateTimeUtc()
            },
            End = new GoogleEventDateTime
            {
                DateTimeDateTimeOffset = firstOccurrenceStart.Plus(Duration.FromHours(1)).ToDateTimeUtc()
            },
            Recurrence = new List<string> { "RRULE:FREQ=WEEKLY;BYDAY=SU" },
            Reminders = new Event.RemindersData
            {
                UseDefault = true
            }
        };
        var masterEventJson = new NewtonsoftJsonSerializer().Serialize(masterEvent);

        var overrideEvent = new GoogleEvent
        {
            Id = "override-event",
            RecurringEventId = "master-event",
            Summary = "Weekly Team Meeting",
            Status = "confirmed",
            Start = new GoogleEventDateTime
            {
                DateTimeDateTimeOffset = secondOccurrenceNewStart.ToDateTimeUtc()
            },
            End = new GoogleEventDateTime
            {
                DateTimeDateTimeOffset = secondOccurrenceNewStart.Plus(Duration.FromHours(1)).ToDateTimeUtc()
            },
            OriginalStartTime = new GoogleEventDateTime
            {
                DateTimeDateTimeOffset = secondOccurrenceOriginalStart.ToDateTimeUtc()
            },
            Reminders = new Event.RemindersData
            {
                UseDefault = true
            }
        };
        var overrideEventJson = new NewtonsoftJsonSerializer().Serialize(overrideEvent);

        var calendarJson = TestDataHelpers.CreateGoogleCalendarWithDefaultReminders(
            "cal1",
            "Test Calendar",
            30);
        _googleServiceStub!.SetRawCalendars(calendarJson);
        _googleServiceStub.SetRawEvents("cal1", masterEventJson, overrideEventJson);

        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        _clock.Reset(Instant.FromUtc(2026, 1, 28, 0, 0));

        await _syncService!.SyncAllAccountsAsync();

        var calendars = await _storage!.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First(c => c.ExternalId == "cal1");
        var allEvents = (await _storage.GetEventsByCalendarAsync(calendar.CalendarId)).ToList();
        var allReminders = new List<ReminderDbo>();
        foreach (var evt in allEvents)
        {
            var reminders = await _storage.GetRemindersByEventAsync(evt.EventId);
            allReminders.AddRange(reminders);
        }

        allReminders.Sort((a, b) => a.TargetTime.CompareTo(b.TargetTime));

        var expectedFirstTrigger = firstOccurrenceStart.Minus(Duration.FromMinutes(30));
        Assert.That(allReminders.Count, Is.EqualTo(1),
            $"Expected 1 reminder total, got {allReminders.Count}");
        
        var firstReminder = allReminders[0];
        var firstTriggerTime = Instant.FromUnixTimeSeconds(firstReminder.TriggerTime);
        Assert.That(firstTriggerTime, Is.EqualTo(expectedFirstTrigger),
            $"Expected first reminder at {expectedFirstTrigger}, got {firstTriggerTime}");

        await _reminderService!.DismissReminderAsync(firstReminder.ReminderId);

        allReminders.Clear();
        foreach (var evt in allEvents)
        {
            var reminders = await _storage!.GetRemindersByEventAsync(evt.EventId);
            allReminders.AddRange(reminders);
        }
        allReminders.Sort((a, b) => a.TriggerTime.CompareTo(b.TriggerTime));

        var expectedSecondTrigger = secondOccurrenceNewStart.Minus(Duration.FromMinutes(30));
        Assert.That(allReminders, Has.Count.EqualTo(1),
            $"Expected 1 reminder after first dismissal, got {allReminders.Count}");

        var secondReminder = allReminders[0];
        var secondTriggerTime = Instant.FromUnixTimeSeconds(secondReminder.TriggerTime);
        Assert.That(secondTriggerTime, Is.EqualTo(expectedSecondTrigger),
            $"Expected second reminder at {expectedSecondTrigger}, got {secondTriggerTime}");

        await _reminderService!.DismissReminderAsync(secondReminder.ReminderId);

        allReminders.Clear();
        foreach (var evt in allEvents)
        {
            var reminders = await _storage!.GetRemindersByEventAsync(evt.EventId);
            allReminders.AddRange(reminders);
        }
        allReminders.Sort((a, b) => a.TriggerTime.CompareTo(b.TriggerTime));

        var thirdOccurrenceStart = firstOccurrenceStart.Plus(Duration.FromDays(14));
        var expectedThirdTrigger = thirdOccurrenceStart.Minus(Duration.FromMinutes(30));
        Assert.That(allReminders, Has.Count.EqualTo(1),
            $"Expected 1 reminder after second dismissal, got {allReminders.Count}");

        var thirdReminder = allReminders[0];
        var thirdTriggerTime = Instant.FromUnixTimeSeconds(thirdReminder.TriggerTime);
        Assert.That(thirdTriggerTime, Is.EqualTo(expectedThirdTrigger), $"Expected third reminder at {expectedThirdTrigger}, got {thirdTriggerTime}");
    }

    #endregion
}
