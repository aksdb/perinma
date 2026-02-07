using CredentialStore;
using Google.Apis.Json;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;

using GoogleEvent = Google.Apis.Calendar.v3.Data.Event;
using GoogleEventDateTime =  Google.Apis.Calendar.v3.Data.EventDateTime;

namespace tests;

public class DatabaseCalendarSourceTests
{
    private static IDisposable CreateTestSetup(out DatabaseCalendarSource calendarSource, out SqliteStorage storage)
    {
        var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        storage = new SqliteStorage(database, credentialManager);
        calendarSource = new DatabaseCalendarSource(storage);
        return database;
    }

    private static async Task<string> CreateCalendar(SqliteStorage storage, AccountType accountType = AccountType.Google)
    {
        var accountId = Guid.NewGuid().ToString();
        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = accountType.ToString() });

        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            Name = "Test Calendar",
            CalendarId = "",
            Enabled = 1
        };
        await storage.CreateOrUpdateCalendarAsync(calendar);

        return calendar.CalendarId;
    }

    private static GoogleEventDateTime CreateGoogleEventDateTime(DateTime dateTime, TimeZoneInfo? timeZone = null)
    {
        return new GoogleEventDateTime
        {
            DateTimeRaw = dateTime.ToString("o"),
            TimeZone = timeZone?.Id ?? TimeZoneInfo.Local.Id
        };
    }
    
    [Test]
    public async Task GetCalendarEvents_ReturnsEventsFromEnabledCalendars()
    {
        // Arrange
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        // Create enabled calendar
        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            ExternalId = "cal1",
            Name = "Work Calendar",
            CalendarId = "",
            Color = "#ff0000",
            Enabled = 1
        };
        await storage.CreateOrUpdateCalendarAsync(calendar);

        // Create test event - use the calendar_id returned from CreateOrUpdateCalendarAsync
        var eventStart = weekStart.AddHours(10);
        var eventEnd = weekStart.AddHours(11);
        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "event1",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventEnd).ToUnixTimeSeconds(),
            Title = "Team Meeting",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        // Act
        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        var calendarEvent = events[0];
        Assert.That(calendarEvent.Title, Is.EqualTo("Team Meeting"));
        Assert.That(calendarEvent.EventReference.ExternalId, Is.EqualTo("event1"));
        Assert.That(calendarEvent.EventReference.Calendar.Id, Is.EqualTo(Guid.Parse(calendar.CalendarId)));
        Assert.That(calendarEvent.EventReference.Calendar.Name, Is.EqualTo("Work Calendar"));
        Assert.That(calendarEvent.EventReference.Calendar.Enabled, Is.True);
        Assert.That(calendarEvent.EventReference.Calendar.Account.Id, Is.EqualTo(Guid.Parse(accountId)));
        Assert.That(calendarEvent.EventReference.Calendar.Account.Name, Is.EqualTo("Test Account"));
        Assert.That(calendarEvent.EventReference.Calendar.Account.Type, Is.EqualTo(AccountType.Google));
    }

    [Test]
    public async Task GetCalendarEvents_ExcludesEventsFromDisabledCalendars()
    {
        // Arrange
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = AccountType.Google.ToString()
        };
        await storage.CreateAccountAsync(account);

        // Create disabled calendar
        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            ExternalId = "cal1",
            Name = "Disabled Calendar",
            CalendarId = "",
            Color = "#ff0000",
            Enabled = 0
        };
        await storage.CreateOrUpdateCalendarAsync(calendar);

        // Create test event in disabled calendar
        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "event1",
            StartTime = new DateTimeOffset(weekStart.AddHours(10)).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekStart.AddHours(11)).ToUnixTimeSeconds(),
            Title = "Hidden Event",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        // Act
        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        // Assert
        Assert.That(events, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task GetCalendarEvents_OnlyReturnsEventsInTimeRange()
    {
        // Arrange
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        // Create test account and calendar
        var accountId = Guid.NewGuid().ToString();
        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            Name = "Test Calendar",
            CalendarId = "",
            Enabled = 1
        };
        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "Google" });
        await storage.CreateOrUpdateCalendarAsync(calendar);

        // Create event before time range (ends exactly at weekStart)
        await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            StartTime = new DateTimeOffset(weekStart.AddDays(-1)).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekStart).ToUnixTimeSeconds(),
            Title = "Before Range"
        });

        // Create event in time range
        await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            StartTime = new DateTimeOffset(weekStart.AddHours(10)).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekStart.AddHours(11)).ToUnixTimeSeconds(),
            Title = "In Range"
        });

        // Create event after time range (starts exactly at weekEnd)
        await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            StartTime = new DateTimeOffset(weekEnd).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekEnd.AddHours(1)).ToUnixTimeSeconds(),
            Title = "After Range"
        });

        // Create event that spans the time range
        await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            StartTime = new DateTimeOffset(weekStart.AddDays(-1)).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekStart.AddDays(1)).ToUnixTimeSeconds(),
            Title = "Spans Range"
        });

        // Act
        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        // Assert - Should only get events that overlap with the time range
        Assert.That(events, Has.Count.EqualTo(2));
        var eventTitles = events.Select(e => e.Title).ToList();
        Assert.That(eventTitles, Does.Contain("In Range"));
        Assert.That(eventTitles, Does.Contain("Spans Range"));
    }

    [Test]
    public async Task GetCalendarEvents_CorrectlyMapsTimestampsToDateTime()
    {
        // Arrange
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        // Setup test data
        var accountId = Guid.NewGuid().ToString();
        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            Name = "Test Calendar",
            CalendarId = "",
            Enabled = 1
        };
        var startTime = weekStart.AddHours(10).AddMinutes(30);
        var endTime = weekStart.AddHours(11).AddMinutes(30);
        var changedAt = weekStart.AddDays(-1).AddHours(12);

        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "Google" });
        await storage.CreateOrUpdateCalendarAsync(calendar);

        await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "event1",
            StartTime = new DateTimeOffset(startTime).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(endTime).ToUnixTimeSeconds(),
            Title = "Test Event",
            ChangedAt = new DateTimeOffset(changedAt).ToUnixTimeSeconds()
        });

        // Act
        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        var calendarEvent = events[0];
        Assert.That(calendarEvent.StartTime, Is.EqualTo(startTime));
        Assert.That(calendarEvent.EndTime, Is.EqualTo(endTime));
        Assert.That(calendarEvent.ChangedAt, Is.EqualTo(changedAt));
    }

    [Test]
    public async Task GetCalendarEvents_ReturnsEmptyListWhenNoEventsExist()
    {
        // Arrange
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        // Create test account and calendar but no events
        var accountId = Guid.NewGuid().ToString();
        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            Name = "Test Calendar",
            CalendarId = "",
            Enabled = 1
        };
        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "Google" });
        await storage.CreateOrUpdateCalendarAsync(calendar);

        // Act
        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        // Assert
        Assert.That(events, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task GetCalendarEvents_ReturnsEventsFromMultipleEnabledCalendars()
    {
        // Arrange
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "Google" });

        // Create two enabled calendars
        var calendar1 = new CalendarDbo
        {
            AccountId = accountId,
            Name = "Work Calendar",
            CalendarId = "",
            Color = "#ff0000",
            Enabled = 1
        };
        var calendar2 = new CalendarDbo
        {
            AccountId = accountId,
            Name = "Personal Calendar",
            CalendarId = "",
            Color = "#00ff00",
            Enabled = 1
        };
        await storage.CreateOrUpdateCalendarAsync(calendar1);
        await storage.CreateOrUpdateCalendarAsync(calendar2);

        // Create events in both calendars
        await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar1.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            StartTime = new DateTimeOffset(weekStart.AddHours(10)).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekStart.AddHours(11)).ToUnixTimeSeconds(),
            Title = "Work Event"
        });
        await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar2.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            StartTime = new DateTimeOffset(weekStart.AddHours(12)).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekStart.AddHours(13)).ToUnixTimeSeconds(),
            Title = "Personal Event"
        });

        // Act
        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        // Assert
        Assert.That(events, Has.Count.EqualTo(2));
        var eventTitles = events.Select(e => e.Title).ToList();
        Assert.That(eventTitles, Does.Contain("Work Event"));
        Assert.That(eventTitles, Does.Contain("Personal Event"));
    }

    [Test]
    public async Task GetCalendarEvents_PreservesCalendarMetadata()
    {
        // Arrange
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        // Setup test data with full calendar metadata
        var accountId = Guid.NewGuid().ToString();
        var calendarExternalId = "cal_123";
        var calendarName = "Test Calendar";
        var calendarColor = "#ff5733";
        var lastSync = new DateTime(2024, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            ExternalId = calendarExternalId,
            Name = calendarName,
            CalendarId = "",
            Color = calendarColor,
            Enabled = 1,
            LastSync = new DateTimeOffset(lastSync).ToUnixTimeSeconds()
        };
        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "Google" });
        await storage.CreateOrUpdateCalendarAsync(calendar);

        await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            StartTime = new DateTimeOffset(weekStart.AddHours(10)).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekStart.AddHours(11)).ToUnixTimeSeconds(),
            Title = "Test Event"
        });

        // Act
        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        var calendarData = events[0].EventReference.Calendar;
        Assert.That(calendarData.Id, Is.EqualTo(Guid.Parse(calendar.CalendarId)));
        Assert.That(calendarData.ExternalId, Is.EqualTo(calendarExternalId));
        Assert.That(calendarData.Name, Is.EqualTo(calendarName));
        Assert.That(calendarData.Color, Is.EqualTo(calendarColor));
        Assert.That(calendarData.Enabled, Is.True);
        Assert.That(calendarData.LastSync, Is.EqualTo(lastSync));
    }

    #region Recurrence Tests

    [Test]
    public async Task GetCalendarEvents_GoogleRecurringEvent_ReturnsOccurrencesInRange()
    {
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var calendarId = await CreateCalendar(storage);

        var eventStart = weekStart.AddHours(10);
        var eventEnd = weekStart.AddHours(11);
        var eventId = Guid.NewGuid().ToString();

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            EventId = eventId,
            ExternalId = "recurring_event",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekEnd.AddDays(30)).ToUnixTimeSeconds(),
            Title = "Weekly Meeting",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        var googleEvent = new GoogleEvent
        {
            Id = "recurring_event",
            Summary = "Weekly Meeting",
            Status = "confirmed",
            Start = CreateGoogleEventDateTime(eventStart),
            End = CreateGoogleEventDateTime(eventEnd),
            Recurrence = new List<string> { "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR" }
        };

        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await storage.SetEventDataJson(eventId, "rawData", rawEventJson);

        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        Assert.That(events.Count, Is.GreaterThan(0));
        foreach (var evt in events)
        {
            Assert.That(evt.Title, Is.EqualTo("Weekly Meeting"));
        }
    }

    [Test]
    public async Task GetCalendarEvents_GoogleNonRecurringEvent_ReturnsSingleEvent()
    {
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var calendarId = await CreateCalendar(storage);

        var eventStart = weekStart.AddHours(10);
        var eventEnd = weekStart.AddHours(11);
        var eventId = Guid.NewGuid().ToString();

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            EventId = eventId,
            ExternalId = "single_event",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventEnd).ToUnixTimeSeconds(),
            Title = "One-time Meeting",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        var googleEvent = new GoogleEvent
        {
            Id = "single_event",
            Summary = "One-time Meeting",
            Status = "confirmed",
            Start = CreateGoogleEventDateTime(eventStart),
            End = CreateGoogleEventDateTime(eventEnd)
        };

        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await storage.SetEventDataJson(eventId, "rawData", rawEventJson);

        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Title, Is.EqualTo("One-time Meeting"));
        Assert.That(events[0].StartTime, Is.EqualTo(eventStart));
        Assert.That(events[0].EndTime, Is.EqualTo(eventEnd));
    }

    [Test]
    public async Task GetCalendarEvents_CalDavRecurringEvent_ReturnsOccurrencesInRange()
    {
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var accountId = Guid.NewGuid().ToString();
        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "CalDAV" });

        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            Name = "Test Calendar",
            CalendarId = "",
            Enabled = 1
        };
        await storage.CreateOrUpdateCalendarAsync(calendar);

        var eventStart = weekStart.AddHours(10);
        var eventEnd = weekStart.AddHours(11);
        var eventId = Guid.NewGuid().ToString();

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = eventId,
            ExternalId = "uid_123",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekEnd.AddDays(30)).ToUnixTimeSeconds(),
            Title = "Daily Standup",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        var rawICalendar = $@"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:uid_123
DTSTART:{eventStart:yyyyMMdd'T'HHmmss'Z'}
DTEND:{eventEnd:yyyyMMdd'T'HHmmss'Z'}
SUMMARY:Daily Standup
STATUS:CONFIRMED
RRULE:FREQ=DAILY;INTERVAL=1
END:VEVENT
END:VCALENDAR";

        await storage.SetEventData(eventId, "rawData", rawICalendar);

        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        Assert.That(events.Count, Is.GreaterThan(0));
        foreach (var evt in events)
        {
            Assert.That(evt.Title, Is.EqualTo("Daily Standup"));
        }
    }

    [Test]
    public async Task GetCalendarEvents_CalDavNonRecurringEvent_ReturnsSingleEvent()
    {
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var accountId = Guid.NewGuid().ToString();
        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "CalDAV" });

        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            Name = "Test Calendar",
            CalendarId = "",
            Enabled = 1
        };
        await storage.CreateOrUpdateCalendarAsync(calendar);

        var eventStart = weekStart.AddHours(10);
        var eventEnd = weekStart.AddHours(11);
        var eventId = Guid.NewGuid().ToString();

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = eventId,
            ExternalId = "uid_456",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventEnd).ToUnixTimeSeconds(),
            Title = "One-time Event",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        var rawICalendar = $@"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:uid_456
DTSTART:{eventStart:yyyyMMdd'T'HHmmss'Z'}
DTEND:{eventEnd:yyyyMMdd'T'HHmmss'Z'}
SUMMARY:One-time Event
STATUS:CONFIRMED
END:VEVENT
END:VCALENDAR";

        await storage.SetEventData(eventId, "rawData", rawICalendar);

        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Title, Is.EqualTo("One-time Event"));
        Assert.That(events[0].StartTime, Is.EqualTo(eventStart));
        Assert.That(events[0].EndTime, Is.EqualTo(eventEnd));
    }

    [Test]
    public async Task GetCalendarEvents_RecurringEventOutsideRange_ReturnsEmptyList()
    {
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var calendarId = await CreateCalendar(storage);

        var eventStart = weekStart.AddDays(-14);
        var eventEnd = weekStart.AddDays(-14).AddHours(1);
        var eventId = Guid.NewGuid().ToString();

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            EventId = eventId,
            ExternalId = "past_recurring",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventStart.AddDays(7)).ToUnixTimeSeconds(),
            Title = "Past Event",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        var googleEvent = new GoogleEvent
        {
            Id = "past_recurring",
            Summary = "Past Event",
            Status = "confirmed",
            Start = CreateGoogleEventDateTime(eventStart),
            End = CreateGoogleEventDateTime(eventEnd),
            Recurrence = new List<string> { "RRULE:FREQ=WEEKLY;COUNT=3" }
        };

        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await storage.SetEventDataJson(eventId, "rawData", rawEventJson);

        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        Assert.That(events, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task GetCalendarEvents_UnknownAccountType_ReturnsFallbackEvent()
    {
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var accountId = Guid.NewGuid().ToString();
        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "Unknown" });

        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            Name = "Test Calendar",
            CalendarId = "",
            Enabled = 1
        };
        await storage.CreateOrUpdateCalendarAsync(calendar);

        var eventStart = weekStart.AddHours(10);
        var eventEnd = weekStart.AddHours(11);
        var eventId = Guid.NewGuid().ToString();

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = eventId,
            ExternalId = "unknown_event",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventEnd).ToUnixTimeSeconds(),
            Title = "Unknown Type Event",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Title, Is.EqualTo("Unknown Type Event"));
    }

    [Test]
    public async Task GetCalendarEvents_GoogleRecurringEventWithTimezone_ReturnsOccurrencesWithCorrectTimestamps()
    {
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var calendarId = await CreateCalendar(storage);

        var eventStart = weekStart.AddHours(10);
        var eventEnd = weekStart.AddHours(11);
        var eventId = Guid.NewGuid().ToString();

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            EventId = eventId,
            ExternalId = "recurring_with_tz",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekEnd.AddDays(30)).ToUnixTimeSeconds(),
            Title = "Weekly Meeting with TZ",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        var googleEvent = new GoogleEvent
        {
            Id = "recurring_with_tz",
            Summary = "Weekly Meeting with TZ",
            Status = "confirmed",
            Start = CreateGoogleEventDateTime(eventStart),
            End = CreateGoogleEventDateTime(eventEnd),
            Recurrence = new List<string> { "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR" }
        };

        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        await storage.SetEventDataJson(eventId, "rawData", rawEventJson);

        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        Assert.That(events.Count, Is.GreaterThan(0));
        foreach (var evt in events)
        {
            Assert.That(evt.Title, Is.EqualTo("Weekly Meeting with TZ"));
        }
    }

    [Test]
    public async Task GetCalendarEvents_FullDayRecurringGoogleEvent()
    {
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);
        var calendarId = await CreateCalendar(storage, accountType: AccountType.Google);

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            ExternalId = "fullday_recurring",
            StartTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
            Title = "Weekly Meeting with TZ",
            ChangedAt = new DateTimeOffset().ToUnixTimeSeconds()
        };
        var eventId = await storage.CreateOrUpdateEventAsync(eventDbo);
        await storage.SetEventDataJson(eventId, "rawData",
            """
            {
                "created": "2021-12-16T14:30:52.000Z",
                "creator":
                {
                    "email": "example@localhost.localdomain",
                    "self": true
                },
                "end":
                {
                    "date": "2021-12-22"
                },
                "etag": "\"3412196577778000\"",
                "eventType": "workingLocation",
                "htmlLink": "https://www.google.com/calendar/event?eid=someid",
                "iCalUID": "someid@google.com",
                "id": "someid",
                "kind": "calendar#event",
                "organizer":
                {
                    "email": "example@localhost.localdomain",
                    "self": true
                },
                "recurrence":
                [
                    "EXDATE;VALUE=DATE:20221122",
                    "RRULE:FREQ=WEEKLY;BYDAY=TU"
                ],
                "reminders":
                {
                    "useDefault": false
                },
                "sequence": 0,
                "start":
                {
                    "date": "2021-12-21"
                },
                "status": "confirmed",
                "summary": "Homeoffice",
                "transparency": "transparent",
                "updated": "2024-01-24T12:11:28.889Z",
                "visibility": "public",
                "workingLocationProperties":
                {
                    "homeOffice":
                    {},
                    "type": "homeOffice"
                }
            }
            """);
        
        var events = calendarSource.GetCalendarEvents(new DateTime(2022, 11, 10), new DateTime(2022, 12, 01));
        Assert.That(events.Count, Is.EqualTo(2));
        
        var event1 = events[0];
        var event2 = events[1];
        Assert.Multiple(() =>
        {
            Assert.That(event1.StartTime, Is.EqualTo(new DateTime(2022, 11, 15, 0, 0, 0)));
            Assert.That(event1.EndTime, Is.EqualTo(new DateTime(2022, 11, 16, 0, 0, 0)));

            Assert.That(event2.StartTime, Is.EqualTo(new DateTime(2022, 11, 29, 0, 0, 0)));
            Assert.That(event2.EndTime, Is.EqualTo(new DateTime(2022, 11, 30, 0, 0, 0)));
        });
    }

    #endregion

    #region Event Shadowing Tests

    [Test]
    public async Task GetCalendarEvents_GoogleRecurringEventWithCancelledInstance_ExcludesCancelledOccurrence()
    {
        // Arrange: A weekly recurring event with one cancelled instance
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var weekStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc); // Monday
        var weekEnd = weekStart.AddDays(7);

        var calendarId = await CreateCalendar(storage, AccountType.Google);

        // Create the parent recurring event (every day for a week)
        var parentStart = weekStart.AddHours(10);
        var parentEnd = weekStart.AddHours(11);

        var parentEventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            ExternalId = "parent_recurring",
            StartTime = new DateTimeOffset(parentStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekEnd.AddDays(30)).ToUnixTimeSeconds(),
            Title = "Daily Standup",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var parentEventId = await storage.CreateOrUpdateEventAsync(parentEventDbo);

        
        var parentGoogleEvent = new GoogleEvent
        {
            Id = "parent_recurring",
            Summary = "Daily Standup",
            Status = "confirmed",
            Start = CreateGoogleEventDateTime(parentStart),
            End = CreateGoogleEventDateTime(parentEnd),
            Recurrence = new List<string> { "RRULE:FREQ=DAILY;COUNT=7" }
        };
        await storage.SetEventDataJson(parentEventId, "rawData",
            NewtonsoftJsonSerializer.Instance.Serialize(parentGoogleEvent));

        // Create a cancelled instance for the third occurrence (Wednesday)
        var cancelledStart = weekStart.AddDays(2).AddHours(10);

        var cancelledEventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            ExternalId = "parent_recurring_20240103T100000Z",
            StartTime = new DateTimeOffset(cancelledStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(cancelledStart.AddHours(1)).ToUnixTimeSeconds(),
            Title = "Daily Standup",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var cancelledEventId = await storage.CreateOrUpdateEventAsync(cancelledEventDbo);

        var cancelledGoogleEvent = new GoogleEvent
        {
            Id = "parent_recurring_20240103T100000Z",
            Summary = "Daily Standup",
            Status = "cancelled",
            RecurringEventId = "parent_recurring",
            OriginalStartTime = CreateGoogleEventDateTime(cancelledStart),
            Start = CreateGoogleEventDateTime(cancelledStart),
            End = CreateGoogleEventDateTime(cancelledStart.AddHours(1))
        };
        await storage.SetEventDataJson(cancelledEventId, "rawData",
            NewtonsoftJsonSerializer.Instance.Serialize(cancelledGoogleEvent));

        // Act
        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        // Assert: Should have 6 occurrences, not 7 (one was cancelled)
        Assert.That(events.Count, Is.EqualTo(6));
        
        // Verify the cancelled date is not in the results
        var cancelledDate = cancelledStart.Date;
        var eventDates = events.Select(e => e.StartTime.Date).ToList();
        Assert.That(eventDates, Does.Not.Contain(cancelledDate));
    }

    [Test]
    public async Task GetCalendarEvents_GoogleRecurringEventWithModifiedInstance_ShowsModifiedVersion()
    {
        // Arrange: A weekly recurring event with one modified instance (different time)
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var weekStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc); // Monday
        var weekEnd = weekStart.AddDays(7);

        var calendarId = await CreateCalendar(storage, AccountType.Google);

        // Create the parent recurring event (every day for a week)
        var parentStart = weekStart.AddHours(10);
        var parentEnd = weekStart.AddHours(11);

        var parentEventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            ExternalId = "parent_recurring",
            StartTime = new DateTimeOffset(parentStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekEnd.AddDays(30)).ToUnixTimeSeconds(),
            Title = "Daily Standup",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var parentEventId = await storage.CreateOrUpdateEventAsync(parentEventDbo);

        var parentGoogleEvent = new GoogleEvent
        {
            Id = "parent_recurring",
            Summary = "Daily Standup",
            Status = "confirmed",
            Start = CreateGoogleEventDateTime(parentStart),
            End = CreateGoogleEventDateTime(parentEnd),
            Recurrence = new List<string> { "RRULE:FREQ=DAILY;COUNT=7" }
        };
        await storage.SetEventDataJson(parentEventId, "rawData",
            NewtonsoftJsonSerializer.Instance.Serialize(parentGoogleEvent));

        // Create a modified instance for the third occurrence (Wednesday) - moved to 2pm
        var originalStart = weekStart.AddDays(2).AddHours(10);
        var modifiedStart = weekStart.AddDays(2).AddHours(14); // Moved to 2pm
        var modifiedEnd = weekStart.AddDays(2).AddHours(15);

        var modifiedEventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            ExternalId = "parent_recurring_20240103T100000Z",
            StartTime = new DateTimeOffset(modifiedStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(modifiedEnd).ToUnixTimeSeconds(),
            Title = "Daily Standup (Rescheduled)",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var modifiedEventId = await storage.CreateOrUpdateEventAsync(modifiedEventDbo);

        var modifiedGoogleEvent = new GoogleEvent
        {
            Id = "parent_recurring_20240103T100000Z",
            Summary = "Daily Standup (Rescheduled)",
            Status = "confirmed",
            RecurringEventId = "parent_recurring",
            OriginalStartTime = CreateGoogleEventDateTime(originalStart),
            Start = CreateGoogleEventDateTime(modifiedStart),
            End = CreateGoogleEventDateTime(modifiedEnd)
        };
        await storage.SetEventDataJson(modifiedEventId, "rawData",
            NewtonsoftJsonSerializer.Instance.Serialize(modifiedGoogleEvent));

        // Act
        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        // Assert: Should still have 7 occurrences
        Assert.That(events.Count, Is.EqualTo(7));

        // Verify the modified instance appears with the new time (2pm UTC, not 10am UTC)
        // Note: The returned time is in local timezone, so we compare in UTC
        var wednesdayEvents = events.Where(e => e.StartTime.Date == weekStart.AddDays(2).Date).ToList();
        Assert.That(wednesdayEvents.Count, Is.EqualTo(1));
        Assert.That(wednesdayEvents[0].StartTime.ToUniversalTime().Hour, Is.EqualTo(14)); // 2pm UTC
        Assert.That(wednesdayEvents[0].Title, Is.EqualTo("Daily Standup (Rescheduled)"));
    }

    [Test]
    public async Task GetCalendarEvents_StandaloneEventWithCancelledStatus_IsExcluded()
    {
        // Arrange: A cancelled exception instance with no parent in the query range
        // This can happen when the parent is outside the query range
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var weekStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var calendarId = await CreateCalendar(storage, AccountType.Google);

        // Create only the cancelled instance (parent is outside query range)
        var cancelledStart = weekStart.AddDays(2).AddHours(10);

        var cancelledEventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            ExternalId = "parent_recurring_20240103T100000Z",
            StartTime = new DateTimeOffset(cancelledStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(cancelledStart.AddHours(1)).ToUnixTimeSeconds(),
            Title = "Daily Standup",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var cancelledEventId = await storage.CreateOrUpdateEventAsync(cancelledEventDbo);

        var cancelledGoogleEvent = new GoogleEvent
        {
            Id = "parent_recurring_20240103T100000Z",
            Summary = "Daily Standup",
            Status = "cancelled",
            RecurringEventId = "parent_outside_range",
            OriginalStartTime = CreateGoogleEventDateTime(cancelledStart),
            Start = CreateGoogleEventDateTime(cancelledStart),
            End = CreateGoogleEventDateTime(cancelledStart.AddHours(1))
        };
        await storage.SetEventDataJson(cancelledEventId, "rawData",
            NewtonsoftJsonSerializer.Instance.Serialize(cancelledGoogleEvent));

        // Act
        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        // Assert: Cancelled events should not appear
        Assert.That(events.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task GetCalendarEvents_ModifiedInstanceWithoutParentInRange_IsIncluded()
    {
        // Arrange: A modified exception instance where the parent is outside the query range
        using var disposable = CreateTestSetup(out var calendarSource, out var storage);

        var weekStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var calendarId = await CreateCalendar(storage, AccountType.Google);

        // Create only the modified instance (parent is outside query range)
        var modifiedStart = weekStart.AddDays(2).AddHours(14);
        var modifiedEnd = weekStart.AddDays(2).AddHours(15);

        var modifiedEventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            ExternalId = "parent_recurring_20240103T100000Z",
            StartTime = new DateTimeOffset(modifiedStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(modifiedEnd).ToUnixTimeSeconds(),
            Title = "Daily Standup (Rescheduled)",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var modifiedEventId = await storage.CreateOrUpdateEventAsync(modifiedEventDbo);

        var modifiedGoogleEvent = new GoogleEvent
        {
            Id = "parent_recurring_20240103T100000Z",
            Summary = "Daily Standup (Rescheduled)",
            Status = "confirmed",
            RecurringEventId = "parent_outside_range",
            OriginalStartTime = CreateGoogleEventDateTime(weekStart.AddDays(2).AddHours(10)),
            Start = CreateGoogleEventDateTime(modifiedStart),
            End = CreateGoogleEventDateTime(modifiedEnd)
        };
        await storage.SetEventDataJson(modifiedEventId, "rawData",
            NewtonsoftJsonSerializer.Instance.Serialize(modifiedGoogleEvent));

        // Act
        var events = calendarSource.GetCalendarEvents(weekStart, weekEnd);

        // Assert: Modified (non-cancelled) instances should appear
        Assert.That(events.Count, Is.EqualTo(1));
        Assert.That(events[0].Title, Is.EqualTo("Daily Standup (Rescheduled)"));
        // Note: The returned time is in local timezone, so we compare in UTC
        Assert.That(events[0].StartTime.ToUniversalTime().Hour, Is.EqualTo(14)); // 2pm UTC
    }

    #endregion
}