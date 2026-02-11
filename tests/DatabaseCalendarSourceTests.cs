using CredentialStore;
using Google.Apis.Json;
using NodaTime;
using perinma.Models;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Storage.Models;
using tests.Fakes;
using tests.Helpers;
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

        var googleServiceStub = new GoogleCalendarServiceStub();
        var calDavServiceStub = new CalDavServiceStub();
        var googleProvider = new GoogleCalendarProvider(googleServiceStub, credentialManager);
        var calDavProvider = new CalDavCalendarProvider(calDavServiceStub, credentialManager);
        var providers = new Dictionary<AccountType, ICalendarProvider>
        {
            { AccountType.Google, googleProvider },
            { AccountType.CalDav, calDavProvider }
        };

        calendarSource = new DatabaseCalendarSource(storage, providers);
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

    private static async Task<string> CreateTestEventAsync(SqliteStorage storage, string calendarId, string externalId, DateTime startTime, DateTime endTime, string title, string? rawData = null)
    {
        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            ExternalId = externalId,
            StartTime = new DateTimeOffset(startTime).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(endTime).ToUnixTimeSeconds(),
            Title = title,
            ChangedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        var eventId = await storage.CreateOrUpdateEventAsync(eventDbo);

        if (rawData != null)
        {
            await storage.SetEventDataJson(eventId, "rawData", rawData);
        }

        return eventId;
    }

    private static GoogleEventDateTime CreateGoogleEventDateTime(DateTime dateTime, TimeZoneInfo? timeZone = null)
    {
        var eventDateTime = new GoogleEventDateTime
        {
            DateTimeRaw = dateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffK"),
        };

        if (dateTime.Kind == DateTimeKind.Utc)
        {
            eventDateTime.TimeZone = null;
        }
        else
        {
            eventDateTime.TimeZone = timeZone?.Id ?? TimeZoneInfo.Local.Id;
        }

        return eventDateTime;
    }

    private static string BuildGoogleEventJson(string eventId, DateTime startTime, DateTime endTime, string title, string status = "confirmed", DateTime? updated = null)
    {
        var googleEvent = new GoogleEvent
        {
            Id = eventId,
            Summary = title,
            Status = status,
            Start = CreateGoogleEventDateTime(startTime),
            End = CreateGoogleEventDateTime(endTime)
        };
        var json = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);

        // Manually inject "updated" field if provided
        if (updated.HasValue)
        {
            var utcDateTime = DateTime.SpecifyKind(updated.Value, DateTimeKind.Utc);
            var updatedValue = utcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
            json = json.Insert(1, $"\"updated\":\"{updatedValue}\",");
        }

        return json;
    }

    private static string BuildCalDavICalendar(string uid, DateTime startTime, DateTime endTime, string title)
    {
        return $@"BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//Test//Test//EN
BEGIN:VEVENT
UID:{uid}
DTSTART:{FormatCalDavDateTime(startTime)}
DTEND:{FormatCalDavDateTime(endTime)}
SUMMARY:{title}
STATUS:CONFIRMED
DTSTAMP:{FormatCalDavDateTime(DateTime.UtcNow)}
END:VEVENT
END:VCALENDAR";
    }

    private static string FormatCalDavDateTime(DateTime dateTime)
    {
        return dateTime.Kind == DateTimeKind.Utc
            ? dateTime.ToString("yyyyMMdd'T'HHmmss'Z'")
            : dateTime.ToString("yyyyMMdd'T'HHmmss");
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

        // Create test event
        var eventStart = weekStart.AddHours(10);
        var eventEnd = weekStart.AddHours(11);
        var googleEvent = new GoogleEvent
        {
            Id = "event1",
            Summary = "Team Meeting",
            Status = "confirmed",
            Start = CreateGoogleEventDateTime(eventStart),
            End = CreateGoogleEventDateTime(eventEnd)
        };
        var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
        Console.WriteLine($"RawEventJson length: {rawEventJson.Length}");
        Console.WriteLine($"RawEventJson: {rawEventJson}");
        await CreateTestEventAsync(storage, calendar.CalendarId, "event1", eventStart, eventEnd, "Team Meeting", rawEventJson);

        // Debug: Check if events are in database
        var dbEvents = await storage.GetEventsByCalendarAsync(calendar.CalendarId);
        Console.WriteLine($"Database has {dbEvents.Count()} events in calendar {calendar.CalendarId}");
        if (dbEvents.Any())
        {
            var dbEvent = dbEvents.First();
            Console.WriteLine($"Event: start={dbEvent.StartTime}, end={dbEvent.EndTime}");
            var rawData = await storage.GetEventData(dbEvent.EventId, "rawData");
            Console.WriteLine($"RawData from GetEventData: {rawData?.Length ?? 0} chars");
            if (rawData != null && rawData.Length < 500)
            {
                Console.WriteLine($"RawData: {rawData}");
            }
        }

        // Act
        Console.WriteLine($"Getting events from {weekStart} to {weekEnd}");
        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);
        Console.WriteLine($"Got {events.Count} events");

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        var calendarEvent = events[0];
        Assert.That(calendarEvent.Title, Is.EqualTo("Team Meeting"));
        Assert.That(calendarEvent.Reference.ExternalId, Is.EqualTo("event1"));
        Assert.That(calendarEvent.Reference.Calendar.Id, Is.EqualTo(Guid.Parse(calendar.CalendarId)));
        Assert.That(calendarEvent.Reference.Calendar.Name, Is.EqualTo("Work Calendar"));
        Assert.That(calendarEvent.Reference.Calendar.Enabled, Is.True);
        Assert.That(calendarEvent.Reference.Calendar.Account.Id, Is.EqualTo(Guid.Parse(accountId)));
        Assert.That(calendarEvent.Reference.Calendar.Account.Name, Is.EqualTo("Test Account"));
        Assert.That(calendarEvent.Reference.Calendar.Account.Type, Is.EqualTo(AccountType.Google));
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
        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

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
        var beforeRangeEventId = await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            StartTime = new DateTimeOffset(weekStart.AddDays(-1)).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekStart).ToUnixTimeSeconds(),
            Title = "Before Range"
        });

        // Create event in time range
        var inRangeEventId = await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            StartTime = new DateTimeOffset(weekStart.AddHours(10)).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekStart.AddHours(11)).ToUnixTimeSeconds(),
            Title = "In Range"
        });

        // Create event after time range (starts exactly at weekEnd)
        var afterRangeEventId = await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            StartTime = new DateTimeOffset(weekEnd).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekEnd.AddHours(1)).ToUnixTimeSeconds(),
            Title = "After Range"
        });

        // Create event that spans the time range
        var spansRangeEventId = await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            StartTime = new DateTimeOffset(weekStart.AddDays(-1)).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekStart.AddDays(1)).ToUnixTimeSeconds(),
            Title = "Spans Range"
        });

        // Set up RawData for the events
        var eventsWithRawData = new[]
        {
            (beforeRangeEventId, weekStart.AddDays(-1), weekStart, "Before Range"),
            (inRangeEventId, weekStart.AddHours(10), weekStart.AddHours(11), "In Range"),
            (afterRangeEventId, weekEnd, weekEnd.AddHours(1), "After Range"),
            (spansRangeEventId, weekStart.AddDays(-1), weekStart.AddDays(1), "Spans Range")
        };

        foreach (var (eventId, start, end, title) in eventsWithRawData)
        {
            var googleEvent = new GoogleEvent
            {
                Id = eventId.ToString(),
                Summary = title,
                Status = "confirmed",
                Start = CreateGoogleEventDateTime(start),
                End = CreateGoogleEventDateTime(end)
            };
            var rawEventJson = NewtonsoftJsonSerializer.Instance.Serialize(googleEvent);
            await storage.SetEventDataJson(eventId, "rawData", rawEventJson);
        }

        // Act
        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

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
        var externalId = "event1";

        await storage.CreateAccountAsync(new AccountDbo { AccountId = accountId, Name = "Test", Type = "Google" });
        await storage.CreateOrUpdateCalendarAsync(calendar);

        var eventId = await storage.CreateOrUpdateEventAsync(new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = externalId,
            StartTime = new DateTimeOffset(startTime).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(endTime).ToUnixTimeSeconds(),
            Title = "Test Event",
            ChangedAt = new DateTimeOffset(changedAt).ToUnixTimeSeconds()
        });

        var rawEventJson = BuildGoogleEventJson(externalId, startTime, endTime, "Test Event", status: "confirmed", updated: changedAt);
        await storage.SetEventDataJson(eventId, "rawData", rawEventJson);

        // Act
        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        var calendarEvent = events[0];
        Assert.That(calendarEvent.StartTime, Is.EqualTo(LocalDateTime.FromDateTime(startTime)));
        Assert.That(calendarEvent.EndTime, Is.EqualTo(LocalDateTime.FromDateTime(endTime)));
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
        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

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
        var eventStart1 = weekStart.AddHours(10);
        var eventEnd1 = weekStart.AddHours(11);
        var eventStart2 = weekStart.AddHours(12);
        var eventEnd2 = weekStart.AddHours(13);

        var event1Dbo = new CalendarEventDbo
        {
            CalendarId = calendar1.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "work_event",
            StartTime = new DateTimeOffset(eventStart1).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventEnd1).ToUnixTimeSeconds(),
            Title = "Work Event"
        };
        var event1Id = await storage.CreateOrUpdateEventAsync(event1Dbo);

        var event2Dbo = new CalendarEventDbo
        {
            CalendarId = calendar2.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "personal_event",
            StartTime = new DateTimeOffset(eventStart2).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventEnd2).ToUnixTimeSeconds(),
            Title = "Personal Event"
        };
        var event2Id = await storage.CreateOrUpdateEventAsync(event2Dbo);

        var rawEvent1Json = BuildGoogleEventJson("work_event", eventStart1, eventEnd1, "Work Event");
        await storage.SetEventDataJson(event1Id, "rawData", rawEvent1Json);
        var rawEvent2Json = BuildGoogleEventJson("personal_event", eventStart2, eventEnd2, "Personal Event");
        await storage.SetEventDataJson(event2Id, "rawData", rawEvent2Json);

        // Act
        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

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

        var eventStart = weekStart.AddHours(10);
        var eventEnd = weekStart.AddHours(11);
        var externalId = "test_event";

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = externalId,
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventEnd).ToUnixTimeSeconds(),
            Title = "Test Event"
        };
        var eventId = await storage.CreateOrUpdateEventAsync(eventDbo);

        var rawEventJson = BuildGoogleEventJson(externalId, eventStart, eventEnd, "Test Event");
        await storage.SetEventDataJson(eventId, "rawData", rawEventJson);

        // Act
        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        var calendarData = events[0].Reference.Calendar;
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

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "recurring_event",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekEnd.AddDays(30)).ToUnixTimeSeconds(),
            Title = "Weekly Meeting",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var eventId = await storage.CreateOrUpdateEventAsync(eventDbo);

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

        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

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

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "single_event",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventEnd).ToUnixTimeSeconds(),
            Title = "One-time Meeting",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var eventId = await storage.CreateOrUpdateEventAsync(eventDbo);

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

        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Title, Is.EqualTo("One-time Meeting"));
        Assert.That(events[0].StartTime, Is.EqualTo(Instant.FromDateTimeUtc(eventStart).InUtc().LocalDateTime));
        Assert.That(events[0].EndTime, Is.EqualTo(Instant.FromDateTimeUtc(eventEnd).InUtc().LocalDateTime));
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

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "uid_123",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekEnd.AddDays(30)).ToUnixTimeSeconds(),
            Title = "Daily Standup",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var eventId = await storage.CreateOrUpdateEventAsync(eventDbo);

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

        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

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

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendar.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "uid_456",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventEnd).ToUnixTimeSeconds(),
            Title = "One-time Event",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var eventId = await storage.CreateOrUpdateEventAsync(eventDbo);

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

        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Title, Is.EqualTo("One-time Event"));
        Assert.That(events[0].StartTime, Is.EqualTo(LocalDateTime.FromDateTime(eventStart)));
        Assert.That(events[0].EndTime, Is.EqualTo(LocalDateTime.FromDateTime(eventEnd)));
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

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "past_recurring",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(eventStart.AddDays(7)).ToUnixTimeSeconds(),
            Title = "Past Event",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var eventId = await storage.CreateOrUpdateEventAsync(eventDbo);

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

        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

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

        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

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

        var eventDbo = new CalendarEventDbo
        {
            CalendarId = calendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "recurring_with_tz",
            StartTime = new DateTimeOffset(eventStart).ToUnixTimeSeconds(),
            EndTime = new DateTimeOffset(weekEnd.AddDays(30)).ToUnixTimeSeconds(),
            Title = "Weekly Meeting with TZ",
            ChangedAt = new DateTimeOffset(weekStart).ToUnixTimeSeconds()
        };
        var eventId = await storage.CreateOrUpdateEventAsync(eventDbo);

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

        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

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
        
        var events = calendarSource.GetCalendarEvents(
            new Interval(Instant.FromDateTimeUtc(new DateTime(2022, 11, 10, 0, 0, 0, DateTimeKind.Utc)),
            Instant.FromDateTimeUtc(new DateTime(2022, 12, 01, 0, 0, 0, DateTimeKind.Utc))));
        Assert.That(events.Count, Is.EqualTo(2));
        
        var event1 = events[0];
        var event2 = events[1];
        Assert.Multiple(() =>
        {
            Assert.That(event1.StartTime, Is.EqualTo(LocalDateTime.FromDateTime(new DateTime(2022, 11, 15, 0, 0, 0, DateTimeKind.Unspecified))));
            Assert.That(event1.EndTime, Is.EqualTo(LocalDateTime.FromDateTime(new DateTime(2022, 11, 16, 0, 0, 0, DateTimeKind.Unspecified))));

            Assert.That(event2.StartTime, Is.EqualTo(LocalDateTime.FromDateTime(new DateTime(2022, 11, 29, 0, 0, 0, DateTimeKind.Unspecified))));
            Assert.That(event2.EndTime, Is.EqualTo(LocalDateTime.FromDateTime(new DateTime(2022, 11, 30, 0, 0, 0, DateTimeKind.Unspecified))));
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
        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

        // Assert: Should have 6 occurrences, not 7 (one was cancelled)
        Assert.That(events.Count, Is.EqualTo(6));
        
        // Verify the cancelled date is not in the results
        var cancelledDate = LocalDate.FromDateTime(cancelledStart.Date);
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
        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

        // Assert: Should still have 7 occurrences
        Assert.That(events.Count, Is.EqualTo(7));

        // Verify the modified instance appears with the new time (2pm UTC, not 10am UTC)
        var wednesdayEvents = events.Where(e => e.StartTime.Date == LocalDate.FromDateTime(weekStart.AddDays(2).Date)).ToList();
        Assert.That(wednesdayEvents.Count, Is.EqualTo(1));
        Assert.That(wednesdayEvents[0].StartTime.Hour, Is.EqualTo(14)); // 2pm
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
        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

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
        var interval = new Interval(Instant.FromDateTimeUtc(weekStart), Instant.FromDateTimeUtc(weekEnd));
        var events = calendarSource.GetCalendarEvents(interval);

        // Assert: Modified (non-cancelled) instances should appear
        Assert.That(events.Count, Is.EqualTo(1));
        Assert.That(events[0].Title, Is.EqualTo("Daily Standup (Rescheduled)"));
        // Assert that the time is 2pm
        Assert.That(events[0].StartTime.Hour, Is.EqualTo(14)); // 2pm
    }

    #endregion
}