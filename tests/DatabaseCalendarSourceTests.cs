using System;
using System.Linq;
using CredentialStore;
using Google.Apis.Json;
using Google.Apis.Calendar.v3.Data;
using NUnit.Framework;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;

namespace tests;

public class DatabaseCalendarSourceTests
{
    [Test]
    public async Task GetCalendarEvents_ReturnsEventsFromEnabledCalendars()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = "Google"
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
        Assert.That(calendarEvent.ExternalId, Is.EqualTo("event1"));
        Assert.That(calendarEvent.Calendar.Id, Is.EqualTo(Guid.Parse(calendar.CalendarId)));
        Assert.That(calendarEvent.Calendar.Name, Is.EqualTo("Work Calendar"));
        Assert.That(calendarEvent.Calendar.Enabled, Is.True);
        Assert.That(calendarEvent.Calendar.Account.Id, Is.EqualTo(Guid.Parse(accountId)));
        Assert.That(calendarEvent.Calendar.Account.Name, Is.EqualTo("Test Account"));
        Assert.That(calendarEvent.Calendar.Account.Type, Is.EqualTo("Google"));
    }

    [Test]
    public async Task GetCalendarEvents_ExcludesEventsFromDisabledCalendars()
    {
        // Arrange
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        // Create test account
        var accountId = Guid.NewGuid().ToString();
        var account = new AccountDbo
        {
            AccountId = accountId,
            Name = "Test Account",
            Type = "Google"
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
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

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
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

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
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

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
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

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
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

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
        var calendarData = events[0].Calendar;
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
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var accountId = Guid.NewGuid().ToString();
        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "Google" });

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
            ExternalId = "recurring_event",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekEnd.AddDays(30)).ToUnixTimeSeconds(),
            Title = "Weekly Meeting",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        var googleEvent = new Event
        {
            Id = "recurring_event",
            Summary = "Weekly Meeting",
            Status = "confirmed",
            Start = new EventDateTime { DateTimeRaw = eventStart.ToString("o") },
            End = new EventDateTime { DateTimeRaw = eventEnd.ToString("o") },
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
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var accountId = Guid.NewGuid().ToString();
        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "Google" });

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
            ExternalId = "single_event",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventEnd).ToUnixTimeSeconds(),
            Title = "One-time Meeting",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        var googleEvent = new Event
        {
            Id = "single_event",
            Summary = "One-time Meeting",
            Status = "confirmed",
            Start = new EventDateTime { DateTimeRaw = eventStart.ToString("o") },
            End = new EventDateTime { DateTimeRaw = eventEnd.ToString("o") }
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
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

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
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

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
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

        var now = DateTime.UtcNow;
        var weekStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var weekEnd = weekStart.AddDays(7);

        var accountId = Guid.NewGuid().ToString();
        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "Google" });

        var calendar = new CalendarDbo
        {
            AccountId = accountId,
            Name = "Test Calendar",
            CalendarId = "",
            Enabled = 1
        };
        await storage.CreateOrUpdateCalendarAsync(calendar);

        var eventStart = weekStart.AddDays(-14);
        var eventEnd = weekStart.AddDays(-14).AddHours(1);
        var eventId = Guid.NewGuid().ToString();

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = eventId,
            ExternalId = "past_recurring",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventStart.AddDays(7)).ToUnixTimeSeconds(),
            Title = "Past Event",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        await storage.CreateOrUpdateEventAsync(eventDbo);

        var googleEvent = new Event
        {
            Id = "past_recurring",
            Summary = "Past Event",
            Status = "confirmed",
            Start = new EventDateTime { DateTimeRaw = eventStart.ToString("o") },
            End = new EventDateTime { DateTimeRaw = eventEnd.ToString("o") },
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
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);
        var calendarSource = new DatabaseCalendarSource(storage);

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

    #endregion
}
