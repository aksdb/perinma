using System.Threading.Tasks;
using CredentialStore;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Json;
using NodaTime;
using NodaTime.Text;
using perinma.Models;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage.Models;
using tests.Fakes;
using tests.Helpers;

namespace tests;

[TestFixture]
public class GoogleCalendarProviderTests
{
    private CredentialManagerService _credentialManager = null!;
    private GoogleCalendarServiceStub _serviceStub = null!;
    private GoogleCalendarProvider _provider = null!;
    private string _accountId = null!;

    [SetUp]
    public void SetUp()
    {
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        _serviceStub = new GoogleCalendarServiceStub();
        _provider = new GoogleCalendarProvider(_serviceStub, _credentialManager);
        _accountId = Guid.NewGuid().ToString();

        // Store default credentials
        var credentials = new GoogleCredentials
        {
            Type = "Google",
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer",
            Scope = "calendar.readonly"
        };
        _credentialManager.StoreGoogleCredentials(_accountId, credentials);
    }

    #region GetCalendarsAsync Tests

    [Test]
    public async Task GetCalendarsAsync_WithValidCredentials_ReturnsCalendars()
    {
        // Arrange
        var cal1Data = TestDataHelpers.CreateGoogleCalendar("cal1", "Work Calendar", selected: true, color: "#ff0000");
        var cal2Data = TestDataHelpers.CreateGoogleCalendar("cal2", "Personal Calendar", selected: false, color: "#00ff00");
        var cal1Json = new Google.Apis.Json.NewtonsoftJsonSerializer().Serialize(cal1Data);
        var cal2Json = new Google.Apis.Json.NewtonsoftJsonSerializer().Serialize(cal2Data);
        _serviceStub.SetRawCalendars(cal1Json, cal2Json);

        // Act
        var result = await _provider.GetCalendarsAsync(_accountId);

        // Assert
        Assert.That(result.Calendars, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result.Calendars[0].ExternalId, Is.EqualTo("cal1"));
            Assert.That(result.Calendars[0].Name, Is.EqualTo("Work Calendar"));
            Assert.That(result.Calendars[0].Color, Is.EqualTo("#ff0000"));
            Assert.That(result.Calendars[0].Selected, Is.True);
            Assert.That(result.Calendars[1].ExternalId, Is.EqualTo("cal2"));
            Assert.That(result.Calendars[1].Selected, Is.False);
        });
    }

    [Test]
    public async Task GetCalendarsAsync_WithDeletedCalendar_MarksAsDeleted()
    {
        // Arrange
        var cal1Data = TestDataHelpers.CreateGoogleCalendar("cal1", "Active Calendar");
        var cal2Data = TestDataHelpers.CreateGoogleCalendar("cal2", "Deleted Calendar", deleted: true);
        var cal1Json = new Google.Apis.Json.NewtonsoftJsonSerializer().Serialize(cal1Data);
        var cal2Json = new Google.Apis.Json.NewtonsoftJsonSerializer().Serialize(cal2Data);
        _serviceStub.SetRawCalendars(cal1Json, cal2Json);

        // Act
        var result = await _provider.GetCalendarsAsync(_accountId);

        // Assert
        Assert.That(result.Calendars, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result.Calendars[0].Deleted, Is.False);
            Assert.That(result.Calendars[1].Deleted, Is.True);
        });
    }

    [Test]
    public async Task GetCalendarsAsync_WithSyncToken_PassesSyncToken()
    {
        // Arrange
        var cal1Data = TestDataHelpers.CreateGoogleCalendar("cal1", "Calendar 1");
        var cal1Json = new Google.Apis.Json.NewtonsoftJsonSerializer().Serialize(cal1Data);
        _serviceStub.SetRawCalendars(cal1Json);

        // Act
        var result = await _provider.GetCalendarsAsync(_accountId, syncToken: "test-sync-token");

        // Assert
        Assert.That(result.SyncToken, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void GetCalendarsAsync_WithMissingCredentials_ThrowsInvalidOperationException()
    {
        // Arrange
        var unknownAccountId = Guid.NewGuid().ToString();

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _provider.GetCalendarsAsync(unknownAccountId));
        Assert.That(ex!.Message, Does.Contain("No Google credentials found"));
    }

    [Test]
    public async Task GetCalendarsAsync_WithUnnamedCalendar_UsesDefaultName()
    {
        // Arrange
        var cal1Data = new Google.Apis.Calendar.v3.Data.CalendarListEntry
        {
            Id = "cal1",
            Summary = null, // No name
            Selected = true
        };
        var cal1Json = new Google.Apis.Json.NewtonsoftJsonSerializer().Serialize(cal1Data);
        _serviceStub.SetRawCalendars(cal1Json);

        // Act
        var result = await _provider.GetCalendarsAsync(_accountId);

        // Assert
        Assert.That(result.Calendars[0].Name, Is.EqualTo("Unnamed Calendar"));
    }

    #endregion

    #region GetEventsAsync Tests

    [Test]
    public async Task GetEventsAsync_WithValidEvents_ReturnsEvents()
    {
        // Arrange
        var start = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 15, 11, 0, 0, DateTimeKind.Utc);
        var eventData = TestDataHelpers.CreateGoogleEventRaw("event1", "Team Meeting", start, end);
        _serviceStub.SetRawEvents("cal1", eventData);

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "cal1");

        // Assert
        Assert.That(result.Events, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.Events[0].ExternalId, Is.EqualTo("event1"));
            Assert.That(result.Events[0].Title, Is.EqualTo("Team Meeting"));
            // Compare UTC times (parsing may convert to local time)
            Assert.That(result.Events[0].StartTime!.Value.ToDateTimeUtc(), Is.EqualTo(start));
            Assert.That(result.Events[0].EndTime!.Value.ToDateTimeUtc(), Is.EqualTo(end));
            Assert.That(result.Events[0].Deleted, Is.False);
        });
    }

    [Test]
    public async Task GetEventsAsync_WithCancelledEvent_MarksAsDeleted()
    {
        // Arrange
        var eventData = TestDataHelpers.CreateCancelledGoogleEvent("event1");
        _serviceStub.SetRawEvents("cal1", eventData);

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "cal1");

        // Assert
        Assert.That(result.Events, Has.Count.EqualTo(1));
        Assert.That(result.Events[0].Deleted, Is.True);
    }

    [Test]
    public async Task GetEventsAsync_WithRecurringEvent_CalculatesRecurrenceEndTime()
    {
        // Arrange
        var start = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        var eventData = TestDataHelpers.CreateRecurringGoogleEvent(
            "recurring1",
            "Weekly Meeting",
            start,
            end,
            "RRULE:FREQ=DAILY;COUNT=5"
        );
        _serviceStub.SetRawEvents("cal1", eventData);

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "cal1");

        // Assert
        Assert.That(result.Events, Has.Count.EqualTo(1));
        var evt = result.Events[0];
        Assert.Multiple(() =>
        {
            Assert.That(evt.StartTime!.Value.ToDateTimeUtc(), Is.EqualTo(start));
            // End time should be calculated from recurrence (5 daily occurrences)
            Assert.That(evt.EndTime!.Value.ToDateTimeUtc(), Is.EqualTo(new DateTime(2025, 1, 5, 11, 0, 0, DateTimeKind.Utc)));
        });
    }

    [Test]
    public async Task GetEventsAsync_WithModifiedOverride_ExpandsBoundsToIncludeOriginalStart()
    {
        // Arrange
        var originalStart = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newStart = new DateTime(2025, 1, 8, 9, 0, 0, DateTimeKind.Utc); // Earlier
        var newEnd = new DateTime(2025, 1, 8, 10, 30, 0, DateTimeKind.Utc);

        var eventData = TestDataHelpers.CreateModifiedGoogleEventOverride(
            "override1",
            "recurring1",
            "Rescheduled Meeting",
            originalStart,
            newStart,
            newEnd
        );
        _serviceStub.SetRawEvents("cal1", eventData);

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "cal1");

        // Assert
        Assert.That(result.Events, Has.Count.EqualTo(1));
        var evt = result.Events[0];
        Assert.Multiple(() =>
        {
            Assert.That(evt.RecurringEventId, Is.EqualTo("recurring1"));
            Assert.That(evt.OriginalStartTime!.Value.ToDateTimeUtc(), Is.EqualTo(originalStart));
            Assert.That(evt.StartTime!.Value.ToDateTimeUtc(), Is.EqualTo(newStart)); // Earlier start preserved
        });
    }

    [Test]
    public async Task GetEventsAsync_WithCancelledOverride_UsesOriginalStartForBothTimes()
    {
        // Arrange
        var originalStart = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);

        var eventData = TestDataHelpers.CreateCancelledGoogleEventOverride(
            "override1",
            "recurring1",
            originalStart
        );
        _serviceStub.SetRawEvents("cal1", eventData);

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "cal1");

        // Assert
        Assert.That(result.Events, Has.Count.EqualTo(1));
        var evt = result.Events[0];
        Assert.Multiple(() =>
        {
            Assert.That(evt.StartTime!.Value.ToDateTimeUtc(), Is.EqualTo(originalStart));
            Assert.That(evt.EndTime!.Value.ToDateTimeUtc(), Is.EqualTo(originalStart));
            Assert.That(evt.RecurringEventId, Is.EqualTo("recurring1"));
        });
    }

    [Test]
    public async Task GetEventsAsync_WithEventMissingStartEnd_SkipsEvent()
    {
        // Arrange
        var evt = new Google.Apis.Calendar.v3.Data.Event
        {
            Id = "event1",
            Summary = "No Times",
            Status = "confirmed"
        };
        var eventData = new Google.Apis.Json.NewtonsoftJsonSerializer().Serialize(evt);
        _serviceStub.SetRawEvents("cal1", eventData);

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "cal1");

        // Assert
        Assert.That(result.Events, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task GetEventsAsync_WithUntitledEvent_UsesDefaultTitle()
    {
        // Arrange
        var start = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 15, 11, 0, 0, DateTimeKind.Utc);
        var evt = new Google.Apis.Calendar.v3.Data.Event
        {
            Id = "event1",
            Summary = null, // No title
            Status = "confirmed",
            Start = new EventDateTime { DateTimeRaw = start.ToString("o") },
            End = new EventDateTime { DateTimeRaw = end.ToString("o") }
        };
        var eventData = new Google.Apis.Json.NewtonsoftJsonSerializer().Serialize(evt);
        _serviceStub.SetRawEvents("cal1", eventData);

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "cal1");

        // Assert
        Assert.That(result.Events[0].Title, Is.EqualTo("Untitled Event"));
    }

    #endregion

    #region Full-Day Event Tests

    [Test]
    public void ParseCalendarEvents_MultiDayFullDayEvent_CalculatesCorrectEndTimes()
    {
        // Arrange - Create a 3-day full-day event (March 7-9, 2025)
        // Google sends End.Date as day after the event's last day
        var startDate = new DateTime(2025, 3, 7);
        var eventData = TestDataHelpers.CreateMultiDayFullDayEvent(
            "fullday1",
            "Conference",
            startDate,
            daysSpan: 3 // Means March 7, 8, 9 (Google sends End.Date = "2025-03-10")
        );

        var timeRange = new Interval(
            Instant.FromUtc(startDate.Year, startDate.Month, startDate.Day, 0, 0),
            Instant.FromUtc(startDate.Year, startDate.Month, startDate.Day + 10, 0, 0)
        );

        var calendar = new perinma.Models.Calendar
        {
            Account = new Account { Id = Guid.NewGuid(), Name = "Test Account", Type = AccountType.Google },
            Id = Guid.NewGuid(),
            ExternalId = "cal1",
            Name = "Test Calendar"
        };

        var rawEvent = new RawEvent
        {
            Reference = new EventReference
            {
                Calendar = calendar,
                Id = Guid.NewGuid(),
                ExternalId = "fullday1"
            },
            RawData = eventData
        };

        // Act
        var calendarEvents = _provider.ParseCalendarEvents(
            new List<RawEvent> { rawEvent },
            timeRange
        );

        // Assert
        Assert.That(calendarEvents, Has.Count.EqualTo(1));
        var evt = calendarEvents[0];
        using (Assert.EnterMultipleScope())
        {
            // Event should be marked as full-day
            Assert.That(evt.Extensions.Get(CalendarEventExtensions.FullDay), Is.True);
            // Start should be at midnight of the first day
            Assert.That(evt.StartTime, Is.EqualTo(new LocalDateTime(2025, 3, 7, 0, 0)));
            // End should be at midnight of the day after the last day (Google convention)
            // For a March 7-9 event, Google sends End.Date = "2025-03-10"
            Assert.That(evt.EndTime, Is.EqualTo(new LocalDateTime(2025, 3, 10, 0, 0)));
        }
    }

    [Test]
    public void ParseCalendarEvents_SingleDayFullDayEvent_CalculatesCorrectEndTimes()
    {
        // Arrange - Create a single-day full-day event (March 7, 2025)
        var startDate = new DateTime(2025, 3, 7);
        var eventData = TestDataHelpers.CreateMultiDayFullDayEvent(
            "fullday1",
            "Birthday",
            startDate,
            daysSpan: 1 // Means March 7 only (Google sends End.Date = "2025-03-08")
        );

        var timeRange = new Interval(
            Instant.FromUtc(startDate.Year, startDate.Month, startDate.Day, 0, 0),
            Instant.FromUtc(startDate.Year, startDate.Month, startDate.Day + 2, 0, 0)
        );

        var calendar = new perinma.Models.Calendar
        {
            Account = new Account { Id = Guid.NewGuid(), Name = "Test Account", Type = AccountType.Google },
            Id = Guid.NewGuid(),
            ExternalId = "cal1",
            Name = "Test Calendar"
        };

        var rawEvent = new RawEvent
        {
            Reference = new EventReference
            {
                Calendar = calendar,
                Id = Guid.NewGuid(),
                ExternalId = "fullday1"
            },
            RawData = eventData
        };

        // Act
        var calendarEvents = _provider.ParseCalendarEvents(
            new List<RawEvent> { rawEvent },
            timeRange
        );

        // Assert
        Assert.That(calendarEvents, Has.Count.EqualTo(1));
        var evt = calendarEvents[0];
        Assert.Multiple(() =>
        {
            Assert.That(evt.Extensions.Get(CalendarEventExtensions.FullDay), Is.True);
            Assert.That(evt.StartTime, Is.EqualTo(new LocalDateTime(2025, 3, 7, 0, 0)));
            Assert.That(evt.EndTime, Is.EqualTo(new LocalDateTime(2025, 3, 8, 0, 0)));
        });
    }

    #endregion

    #region TestConnectionAsync Tests

    [Test]
    public async Task TestConnectionAsync_WithValidCredentials_ReturnsTrue()
    {
        // Arrange
        var cal1Data = TestDataHelpers.CreateGoogleCalendar("cal1", "Test Calendar");
        var cal1Json = new Google.Apis.Json.NewtonsoftJsonSerializer().Serialize(cal1Data);
        _serviceStub.SetRawCalendars(cal1Json);

        // Act
        var result = await _provider.TestConnectionAsync(_accountId);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task TestConnectionAsync_WithMissingCredentials_ReturnsFalse()
    {
        // Arrange
        var unknownAccountId = Guid.NewGuid().ToString();

        // Act
        var result = await _provider.TestConnectionAsync(unknownAccountId);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region GetReminderMinutesAsync Tests

    [Test]
    public async Task GetReminderMinutesAsync_WithEventOverrides_ReturnsOverrideMinutes()
    {
        // Arrange - Event with custom reminders
        var rawEventData = @"{
            ""id"": ""event1"",
            ""summary"": ""Test Event"",
            ""reminders"": {
                ""useDefault"": false,
                ""overrides"": [
                    { ""method"": ""popup"", ""minutes"": 10 },
                    { ""method"": ""popup"", ""minutes"": 30 },
                    { ""method"": ""email"", ""minutes"": 60 }
                ]
            }
        }";

        // Act
        var result = _provider.GetReminderMinutes(rawEventData);

        // Assert - Only popup reminders should be returned
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(10));
            Assert.That(result, Does.Contain(30));
        });
    }

    [Test]
    public async Task GetReminderMinutesAsync_WithUseDefault_ReturnsCalendarDefaults()
    {
        // Arrange
        var rawEventData = @"{
            ""id"": ""event1"",
            ""summary"": ""Test Event"",
            ""reminders"": {
                ""useDefault"": true
            }
        }";

        var rawCalendarData = @"{
            ""id"": ""cal1"",
            ""summary"": ""Work Calendar"",
            ""defaultReminders"": [
                { ""method"": ""popup"", ""minutes"": 15 },
                { ""method"": ""popup"", ""minutes"": 60 }
            ]
        }";

        // Act
        var result = _provider.GetReminderMinutes(rawEventData, rawCalendarData);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(15));
            Assert.That(result, Does.Contain(60));
        });
    }

    [Test]
    public async Task GetReminderMinutesAsync_WithNoReminders_ReturnsEmptyList()
    {
        // Arrange
        var rawEventData = @"{
            ""id"": ""event1"",
            ""summary"": ""Test Event"",
            ""reminders"": null
        }";

        // Act
        var result = _provider.GetReminderMinutes(rawEventData);

        // Assert
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region RespondToEventAsync Tests

    [Test]
    public async Task RespondToEventAsync_WithValidCredentials_CallsService()
    {
        // Arrange - No specific setup needed, fake service accepts any call

        // Act & Assert - Should not throw
        await _provider.RespondToEventAsync(
            _accountId,
            "cal1",
            "event1",
            "{}",
            "accepted"
        );
    }

    [Test]
    public void RespondToEventAsync_WithMissingCredentials_ThrowsInvalidOperationException()
    {
        // Arrange
        var unknownAccountId = Guid.NewGuid().ToString();

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _provider.RespondToEventAsync(
                unknownAccountId,
                "cal1",
                "event1",
                "{}",
                "accepted"
            ));
        Assert.That(ex!.Message, Does.Contain("No Google credentials found"));
    }

    #endregion

    #region RawData Serialization Tests

    [Test]
    public async Task GetCalendarsAsync_StoresRawDataAsJson()
    {
        // Arrange
        var cal1Data = TestDataHelpers.CreateGoogleCalendar("cal1", "Work Calendar", selected: true, color: "#ff0000");
        var cal1Json = new Google.Apis.Json.NewtonsoftJsonSerializer().Serialize(cal1Data);
        _serviceStub.SetRawCalendars(cal1Json);

        // Act
        var result = await _provider.GetCalendarsAsync(_accountId);

        // Assert
        Assert.That(result.Calendars[0].Data.ContainsKey("rawData"), Is.True);
        Assert.That(result.Calendars[0].Data["rawData"], Is.InstanceOf<DataAttribute.JsonText>());
        var rawDataAttr = (DataAttribute.JsonText)result.Calendars[0].Data["rawData"];
        Assert.Multiple(() =>
        {
            Assert.That(rawDataAttr.value, Does.Contain("cal1"));
            Assert.That(rawDataAttr.value, Does.Contain("Work Calendar"));
        });
    }

    [Test]
    public async Task GetEventsAsync_StoresRawDataAsJson()
    {
        // Arrange
        var start = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 15, 11, 0, 0, DateTimeKind.Utc);
        var eventData = TestDataHelpers.CreateGoogleEventRaw("event1", "Team Meeting", start, end);
        _serviceStub.SetRawEvents("cal1", eventData);

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "cal1");

        // Assert
        var rawData = result.Events[0].Data["rawData"] as  DataAttribute.JsonText;
        Assert.That(rawData, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rawData.value, Does.Contain("event1"));
            Assert.That(rawData.value, Does.Contain("Team Meeting"));
        }
    }

    #endregion

    #region Timezone Tests

    [Test]
    public void GetNextReminderOccurrencesAsync_RecurringEventAcrossDSTBoundary_PreservesCorrectTime()
    {
        // Arrange - Create a recurring weekly event in Europe/Berlin timezone
        // Event starts in January 2025 (winter, CET +0100) at 10:00 local time
        // We query in July 2025 (summer, CEST +0200)
        // The summer occurrence should be at 10:00 CEST, which is 08:00 UTC
        // (vs winter at 10:00 CET = 09:00 UTC)

        var timeZone = DateTimeZoneProviders.Tzdb["Europe/Berlin"];
        var eventStart = new LocalDateTime(2025, 1, 13, 10, 0, 0).InZoneLeniently(timeZone);
        var summerReference = new LocalDate(2025, 7, 15).AtStartOfDayInZone(timeZone);
        
        var googleEvent = new Event
        {
            Id = "event1",
            Summary = "Recurring Meeting",
            Status = "confirmed",
            // Weekly recurring event starting January 13, 2025 at 10:00 Europe/Berlin (a Monday)
            Start = new EventDateTime
            {
                DateTimeRaw = OffsetDateTimePattern.Rfc3339.Format(eventStart.ToOffsetDateTime()), // CET winter time
                TimeZone = timeZone.Id,
            },
            End = new EventDateTime
            {
                DateTimeRaw = OffsetDateTimePattern.Rfc3339.Format(eventStart.ToOffsetDateTime().PlusHours(1)),
                TimeZone = timeZone.Id,
            },
            Recurrence = ["RRULE:FREQ=WEEKLY;BYDAY=MO"], // Every Monday
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = [new EventReminder { Minutes = 30, Method = "popup" }]
            }
        };

        var serializer = NewtonsoftJsonSerializer.Instance;
        var rawEventData = serializer.Serialize(googleEvent);

        // Act - Query during summer time
        var result = _provider.GetNextReminderOccurrences(
            rawEventData,
            null,
            referenceTime: summerReference.ToInstant()
        );

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var occurrence = result[0].Occurrence;
        var triggerTime = result[0].TriggerTime;

        // Find the first Monday on or after July 15, 2025 (which is a Tuesday)
        // First Monday is July 21, 2025
        var expectedOccurrence = new LocalDateTime(2025, 7, 21, 10, 0, 0).InZoneLeniently(timeZone);
        var expectedTrigger = expectedOccurrence.PlusMinutes(-30);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(occurrence.InZone(timeZone), Is.EqualTo(expectedOccurrence),
                $"Expected occurrence at {expectedOccurrence:O}, got {occurrence:O}");
            Assert.That(triggerTime.InZone(timeZone), Is.EqualTo(expectedTrigger),
                $"Expected trigger at {expectedTrigger:O}, got {triggerTime:O}");
        }
    }

    #endregion
}
