using System;
using System.Threading.Tasks;
using CredentialStore;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Storage.Models;
using perinma.Tests.Fakes;

namespace tests;

[TestFixture]
public class CalDavCalendarProviderTests
{
    private CredentialManagerService _credentialManager = null!;
    private FakeCalDavService _fakeService = null!;
    private CalDavCalendarProvider _provider = null!;
    private string _accountId = null!;

    [SetUp]
    public void SetUp()
    {
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        _fakeService = new FakeCalDavService();
        _provider = new CalDavCalendarProvider(_fakeService, _credentialManager);
        _accountId = Guid.NewGuid().ToString();

        // Store default credentials
        var credentials = new CalDavCredentials
        {
            Type = "CalDAV",
            ServerUrl = "https://caldav.example.com",
            Username = "testuser@example.com",
            Password = "testpass"
        };
        _credentialManager.StoreCalDavCredentials(_accountId, credentials);
    }

    #region GetCalendarsAsync Tests

    [Test]
    public async Task GetCalendarsAsync_WithValidCredentials_ReturnsCalendars()
    {
        // Arrange
        _fakeService.SetCalendars(
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/work",
                DisplayName = "Work Calendar",
                Color = "#ff0000",
                Deleted = false
            },
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/personal",
                DisplayName = "Personal Calendar",
                Color = "#00ff00",
                Deleted = false
            }
        );

        // Act
        var result = await _provider.GetCalendarsAsync(_accountId);

        // Assert
        Assert.That(result.Calendars, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result.Calendars[0].ExternalId, Is.EqualTo("https://caldav.example.com/calendars/work"));
            Assert.That(result.Calendars[0].Name, Is.EqualTo("Work Calendar"));
            Assert.That(result.Calendars[0].Color, Is.EqualTo("#ff0000"));
            Assert.That(result.Calendars[0].Selected, Is.True); // CalDAV defaults to selected
            Assert.That(result.Calendars[1].ExternalId, Is.EqualTo("https://caldav.example.com/calendars/personal"));
        });
    }

    [Test]
    public async Task GetCalendarsAsync_WithDeletedCalendar_MarksAsDeleted()
    {
        // Arrange
        _fakeService.SetCalendars(
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/active",
                DisplayName = "Active Calendar",
                Deleted = false
            },
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/deleted",
                DisplayName = "Deleted Calendar",
                Deleted = true
            }
        );

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
    public void GetCalendarsAsync_WithMissingCredentials_ThrowsInvalidOperationException()
    {
        // Arrange
        var unknownAccountId = Guid.NewGuid().ToString();

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _provider.GetCalendarsAsync(unknownAccountId));
        Assert.That(ex!.Message, Does.Contain("No CalDAV credentials found"));
    }

    [Test]
    public async Task GetCalendarsAsync_AlwaysDefaultsSelectedToTrue()
    {
        // Arrange - CalDAV doesn't have a "selected" concept
        _fakeService.SetCalendars(
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/test",
                DisplayName = "Test Calendar",
                Deleted = false
            }
        );

        // Act
        var result = await _provider.GetCalendarsAsync(_accountId);

        // Assert
        Assert.That(result.Calendars[0].Selected, Is.True);
    }

    [Test]
    public async Task GetCalendarsAsync_WithOwner_RawDataContainsOwner()
    {
        // Arrange - CalDAV calendar with owner (shared calendar)
        _fakeService.SetCalendars(
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/shared",
                DisplayName = "Shared Calendar",
                Deleted = false,
                Owner = "https://caldav.example.com/principals/otheruser/"
            }
        );

        // Act
        var result = await _provider.GetCalendarsAsync(_accountId);

        // Assert - RawData should contain owner information
        Assert.That(result.Calendars[0].RawData, Is.Not.Null);
        Assert.That(result.Calendars[0].RawData, Does.Contain("owner"));
        Assert.That(result.Calendars[0].RawData, Does.Contain("otheruser"));
    }

    [Test]
    public async Task GetCalendarsAsync_WithoutOwner_RawDataIsNull()
    {
        // Arrange - CalDAV calendar without owner (owned calendar or server doesn't support owner property)
        _fakeService.SetCalendars(
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/test",
                DisplayName = "Test Calendar",
                Deleted = false
            }
        );

        // Act
        var result = await _provider.GetCalendarsAsync(_accountId);

        // Assert
        Assert.That(result.Calendars[0].RawData, Is.Null);
    }

    [Test]
    public async Task GetCalendarsAsync_WithAcl_RawDataContainsAcl()
    {
        // Arrange - CalDAV calendar with ACL
        var aclXml = @"<D:acl xmlns:D=""DAV:"">
          <D:ace>
            <D:principal><D:all/></D:principal>
            <D:grant>
              <D:privilege><D:read/></D:privilege>
            </D:grant>
          </D:ace>
        </D:acl>";

        _fakeService.SetCalendars(
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/shared",
                DisplayName = "Shared Calendar",
                Deleted = false,
                Owner = "https://caldav.example.com/principals/otheruser/",
                AclXml = aclXml
            }
        );

        // Act
        var result = await _provider.GetCalendarsAsync(_accountId);

        // Assert - RawData should contain ACL information
        Assert.That(result.Calendars[0].RawData, Is.Not.Null);
        Assert.That(result.Calendars[0].RawData, Does.Contain("acl"));
    }

    [Test]
    public async Task GetCalendarsAsync_WithCurrentUserPrivilegeSet_RawDataContainsPrivileges()
    {
        // Arrange - CalDAV calendar with current user privileges
        var privilegeSetXml = @"<D:current-user-privilege-set xmlns:D=""DAV:"">
          <D:privilege><D:read/></D:privilege>
          <D:privilege><D:write/></D:privilege>
        </D:current-user-privilege-set>";

        _fakeService.SetCalendars(
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/test",
                DisplayName = "Test Calendar",
                Deleted = false,
                CurrentUserPrivilegeSetXml = privilegeSetXml
            }
        );

        // Act
        var result = await _provider.GetCalendarsAsync(_accountId);

        // Assert - RawData should contain privilege set
        Assert.That(result.Calendars[0].RawData, Is.Not.Null);
        Assert.That(result.Calendars[0].RawData, Does.Contain("currentUserPrivilegeSet"));
    }

    #endregion

    #region GetEventsAsync Tests

    [Test]
    public async Task GetEventsAsync_WithValidEvents_ReturnsEvents()
    {
        // Arrange
        var start = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 15, 11, 0, 0, DateTimeKind.Utc);
        _fakeService.SetEvents(
            "https://caldav.example.com/calendars/work",
            FakeCalDavService.CreateEvent(
                "event-uid-1",
                "https://caldav.example.com/calendars/work/event1.ics",
                "Team Meeting",
                start,
                end
            )
        );

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "https://caldav.example.com/calendars/work");

        // Assert
        Assert.That(result.Events, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.Events[0].ExternalId, Is.EqualTo("event-uid-1"));
            Assert.That(result.Events[0].Title, Is.EqualTo("Team Meeting"));
            Assert.That(result.Events[0].StartTime, Is.EqualTo(start));
            Assert.That(result.Events[0].EndTime, Is.EqualTo(end));
            Assert.That(result.Events[0].Deleted, Is.False);
        });
    }

    [Test]
    public async Task GetEventsAsync_WithCancelledEvent_MarksAsDeleted()
    {
        // Arrange
        var start = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 15, 11, 0, 0, DateTimeKind.Utc);
        _fakeService.SetEvents(
            "https://caldav.example.com/calendars/work",
            new CalDavEvent
            {
                Uid = "event-uid-1",
                Url = "https://caldav.example.com/calendars/work/event1.ics",
                Summary = "Cancelled Meeting",
                StartTime = start,
                EndTime = end,
                Status = "CANCELLED",
                Deleted = false
            }
        );

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "https://caldav.example.com/calendars/work");

        // Assert
        Assert.That(result.Events, Has.Count.EqualTo(1));
        Assert.That(result.Events[0].Deleted, Is.True);
    }

    [Test]
    public async Task GetEventsAsync_WithDeletedFlag_MarksAsDeleted()
    {
        // Arrange
        _fakeService.SetEvents(
            "https://caldav.example.com/calendars/work",
            new CalDavEvent
            {
                Uid = "event-uid-1",
                Url = "https://caldav.example.com/calendars/work/event1.ics",
                Summary = "Deleted Event",
                Deleted = true
            }
        );

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "https://caldav.example.com/calendars/work");

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
        _fakeService.SetEvents(
            "https://caldav.example.com/calendars/work",
            FakeCalDavService.CreateRecurringEvent(
                "recurring-uid-1",
                "https://caldav.example.com/calendars/work/recurring1.ics",
                "Daily Standup",
                start,
                end,
                "RRULE:FREQ=DAILY;COUNT=5"
            )
        );

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "https://caldav.example.com/calendars/work");

        // Assert
        Assert.That(result.Events, Has.Count.EqualTo(1));
        var evt = result.Events[0];
        Assert.Multiple(() =>
        {
            Assert.That(evt.StartTime, Is.EqualTo(start));
            // End time should be calculated from recurrence (5 daily occurrences)
            Assert.That(evt.EndTime, Is.EqualTo(new DateTime(2025, 1, 5, 11, 0, 0, DateTimeKind.Utc)));
        });
    }

    [Test]
    public async Task GetEventsAsync_WithUntitledEvent_UsesDefaultTitle()
    {
        // Arrange
        var start = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 15, 11, 0, 0, DateTimeKind.Utc);
        _fakeService.SetEvents(
            "https://caldav.example.com/calendars/work",
            new CalDavEvent
            {
                Uid = "event-uid-1",
                Url = "https://caldav.example.com/calendars/work/event1.ics",
                Summary = null, // No title
                StartTime = start,
                EndTime = end,
                Status = "CONFIRMED",
                Deleted = false
            }
        );

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "https://caldav.example.com/calendars/work");

        // Assert
        Assert.That(result.Events[0].Title, Is.EqualTo("Untitled Event"));
    }

    [Test]
    public async Task GetEventsAsync_StoresRawICalendarData()
    {
        // Arrange
        var start = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 15, 11, 0, 0, DateTimeKind.Utc);
        _fakeService.SetEvents(
            "https://caldav.example.com/calendars/work",
            FakeCalDavService.CreateEvent(
                "event-uid-1",
                "https://caldav.example.com/calendars/work/event1.ics",
                "Team Meeting",
                start,
                end
            )
        );

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "https://caldav.example.com/calendars/work");

        // Assert
        Assert.That(result.Events[0].RawData, Is.Not.Null.And.Not.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(result.Events[0].RawData, Does.Contain("BEGIN:VCALENDAR"));
            Assert.That(result.Events[0].RawData, Does.Contain("Team Meeting"));
        });
    }

    [Test]
    public async Task GetEventsAsync_CalDavDoesNotUseRecurringEventId()
    {
        // Arrange - CalDAV handles recurrence differently than Google
        var start = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 15, 11, 0, 0, DateTimeKind.Utc);
        _fakeService.SetEvents(
            "https://caldav.example.com/calendars/work",
            FakeCalDavService.CreateEvent(
                "event-uid-1",
                "https://caldav.example.com/calendars/work/event1.ics",
                "Team Meeting",
                start,
                end
            )
        );

        // Act
        var result = await _provider.GetEventsAsync(_accountId, "https://caldav.example.com/calendars/work");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Events[0].RecurringEventId, Is.Null);
            Assert.That(result.Events[0].OriginalStartTime, Is.Null);
        });
    }

    #endregion

    #region TestConnectionAsync Tests

    [Test]
    public async Task TestConnectionAsync_WithValidCredentials_ReturnsTrue()
    {
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
    public async Task GetReminderMinutesAsync_WithRelativeAlarm_ReturnsMinutes()
    {
        // Arrange - iCalendar with VALARM
        var rawEventData = "BEGIN:VCALENDAR\r\n" +
                           "VERSION:2.0\r\n" +
                           "PRODID:-//Test//Test//EN\r\n" +
                           "BEGIN:VEVENT\r\n" +
                           "UID:test-event\r\n" +
                           "DTSTART:20250115T100000Z\r\n" +
                           "DTEND:20250115T110000Z\r\n" +
                           "SUMMARY:Test Event\r\n" +
                           "BEGIN:VALARM\r\n" +
                           "ACTION:DISPLAY\r\n" +
                           "TRIGGER:-PT15M\r\n" +
                           "DESCRIPTION:Reminder\r\n" +
                           "END:VALARM\r\n" +
                           "BEGIN:VALARM\r\n" +
                           "ACTION:DISPLAY\r\n" +
                           "TRIGGER:-PT1H\r\n" +
                           "DESCRIPTION:Reminder\r\n" +
                           "END:VALARM\r\n" +
                           "END:VEVENT\r\n" +
                           "END:VCALENDAR";

        // Act
        var result = await _provider.GetReminderMinutesAsync(rawEventData);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(15));
            Assert.That(result, Does.Contain(60));
        });
    }

    [Test]
    public async Task GetReminderMinutesAsync_WithNoAlarms_ReturnsEmptyList()
    {
        // Arrange
        var rawEventData = "BEGIN:VCALENDAR\r\n" +
                           "VERSION:2.0\r\n" +
                           "PRODID:-//Test//Test//EN\r\n" +
                           "BEGIN:VEVENT\r\n" +
                           "UID:test-event\r\n" +
                           "DTSTART:20250115T100000Z\r\n" +
                           "DTEND:20250115T110000Z\r\n" +
                           "SUMMARY:Test Event\r\n" +
                           "END:VEVENT\r\n" +
                           "END:VCALENDAR";

        // Act
        var result = await _provider.GetReminderMinutesAsync(rawEventData);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetReminderMinutesAsync_WithInvalidICalendar_ReturnsEmptyList()
    {
        // Arrange
        var rawEventData = "invalid icalendar data";

        // Act
        var result = await _provider.GetReminderMinutesAsync(rawEventData);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetReminderMinutesAsync_WithDayAlarm_ConvertsToMinutes()
    {
        // Arrange - Alarm 1 day before
        var rawEventData = "BEGIN:VCALENDAR\r\n" +
                           "VERSION:2.0\r\n" +
                           "PRODID:-//Test//Test//EN\r\n" +
                           "BEGIN:VEVENT\r\n" +
                           "UID:test-event\r\n" +
                           "DTSTART:20250115T100000Z\r\n" +
                           "DTEND:20250115T110000Z\r\n" +
                           "SUMMARY:Test Event\r\n" +
                           "BEGIN:VALARM\r\n" +
                           "ACTION:DISPLAY\r\n" +
                           "TRIGGER:-P1D\r\n" +
                           "DESCRIPTION:Reminder\r\n" +
                           "END:VALARM\r\n" +
                           "END:VEVENT\r\n" +
                           "END:VCALENDAR";

        // Act
        var result = await _provider.GetReminderMinutesAsync(rawEventData);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(24 * 60), "1 day = 1440 minutes");
    }

    #endregion

    #region RespondToEventAsync Tests

    [Test]
    public async Task RespondToEventAsync_WithValidCredentials_CallsService()
    {
        // Arrange
        var rawEventData = "BEGIN:VCALENDAR\r\n" +
                           "VERSION:2.0\r\n" +
                           "PRODID:-//Test//Test//EN\r\n" +
                           "BEGIN:VEVENT\r\n" +
                           "UID:test-event\r\n" +
                           "DTSTART:20250115T100000Z\r\n" +
                           "DTEND:20250115T110000Z\r\n" +
                           "SUMMARY:Test Event\r\n" +
                           "END:VEVENT\r\n" +
                           "END:VCALENDAR";

        // Act & Assert - Should not throw
        await _provider.RespondToEventAsync(
            _accountId,
            "https://caldav.example.com/calendars/work",
            "https://caldav.example.com/calendars/work/event1.ics",
            rawEventData,
            "ACCEPTED"
        );
    }

    [Test]
    public void RespondToEventAsync_WithMissingCredentials_ThrowsInvalidOperationException()
    {
        // Arrange
        var unknownAccountId = Guid.NewGuid().ToString();

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await _provider.RespondToEventAsync(
            unknownAccountId,
            "https://caldav.example.com/calendars/work",
            "https://caldav.example.com/calendars/work/event1.ics",
            "{}",
            "ACCEPTED"
        ));
        Assert.That(ex!.Message, Does.Contain("No CalDAV credentials found"));
    }

    #endregion

    #region GetReminderOccurrencesAsync Tests

    [Test]
    public async Task GetReminderOccurrencesAsync_WithSingleEvent_ReturnsTriggerTime()
    {
        // Arrange - Event in the future with alarm
        var futureStart = DateTime.UtcNow.AddDays(1);
        var rawEventData = "BEGIN:VCALENDAR\r\n" +
                           "VERSION:2.0\r\n" +
                           "PRODID:-//Test//Test//EN\r\n" +
                           "BEGIN:VEVENT\r\n" +
                           $"UID:test-event\r\n" +
                           $"DTSTART:{futureStart:yyyyMMdd'T'HHmmss'Z'}\r\n" +
                           $"DTEND:{futureStart.AddHours(1):yyyyMMdd'T'HHmmss'Z'}\r\n" +
                           "SUMMARY:Future Event\r\n" +
                           "BEGIN:VALARM\r\n" +
                           "ACTION:DISPLAY\r\n" +
                           "TRIGGER:-PT30M\r\n" +
                           "DESCRIPTION:Reminder\r\n" +
                           "END:VALARM\r\n" +
                           "END:VEVENT\r\n" +
                           "END:VCALENDAR";

        // Act
        var result = await _provider.GetNextReminderOccurrencesAsync(rawEventData);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var (occurrence, triggerTime) = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(occurrence.Date, Is.EqualTo(futureStart.Date));
            Assert.That(triggerTime, Is.LessThan(occurrence));
        });
    }

    [Test]
    public async Task GetReminderOccurrencesAsync_WithPastEvent_ReturnsEmptyList()
    {
        // Arrange - Event in the past
        var pastStart = DateTime.UtcNow.AddDays(-1);
        var rawEventData = "BEGIN:VCALENDAR\r\n" +
                           "VERSION:2.0\r\n" +
                           "PRODID:-//Test//Test//EN\r\n" +
                           "BEGIN:VEVENT\r\n" +
                           $"UID:test-event\r\n" +
                           $"DTSTART:{pastStart:yyyyMMdd'T'HHmmss'Z'}\r\n" +
                           $"DTEND:{pastStart.AddHours(1):yyyyMMdd'T'HHmmss'Z'}\r\n" +
                           "SUMMARY:Past Event\r\n" +
                           "BEGIN:VALARM\r\n" +
                           "ACTION:DISPLAY\r\n" +
                           "TRIGGER:-PT30M\r\n" +
                           "DESCRIPTION:Reminder\r\n" +
                           "END:VALARM\r\n" +
                           "END:VEVENT\r\n" +
                           "END:VCALENDAR";

        // Act
        var result = await _provider.GetNextReminderOccurrencesAsync(rawEventData);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetReminderOccurrencesAsync_WithRecurringEvent_ReturnsTriggerTime()
    {
        var rawEventData =
            """
            BEGIN:VCALENDAR
            PRODID:-//SomeClient/1.2.3.4
            VERSION:2.0
            BEGIN:VTIMEZONE
            TZID:Europe/Berlin
            LAST-MODIFIED:20250324T091428Z
            X-LIC-LOCATION:Europe/Berlin
            BEGIN:DAYLIGHT
            TZNAME:CEST
            TZOFFSETFROM:+0100
            TZOFFSETTO:+0200
            DTSTART:19700329T020000
            RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU
            END:DAYLIGHT
            BEGIN:STANDARD
            TZNAME:CET
            TZOFFSETFROM:+0200
            TZOFFSETTO:+0100
            DTSTART:19701025T030000
            RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU
            END:STANDARD
            END:VTIMEZONE
            BEGIN:VEVENT
            UID:15E7-696A9000-3-739CD300
            SUMMARY:Testtermin
            LOCATION:Irgendwo sonst
            DESCRIPTION:Mal bissl description.\nMehrzeilig?\nOk.
            CLASS:PUBLIC
            X-SOGO-SEND-APPOINTMENT-NOTIFICATIONS:NO
            TRANSP:OPAQUE
            DTSTART;TZID=Europe/Berlin:20260116T130000
            DTEND;TZID=Europe/Berlin:20260116T140000
            CREATED:20260116T192252Z
            DTSTAMP:20260123T141142Z
            LAST-MODIFIED:20260123T141142Z
            RRULE:FREQ=WEEKLY
            SEQUENCE:1
            X-MICROSOFT-CDO-BUSYSTATUS:BUSY
            BEGIN:VALARM
            TRIGGER;RELATED=START:-PT5M
            ACTION:DISPLAY
            SUMMARY:Testtermin
            DESCRIPTION:Mal bissl description.\nMehrzeilig?\nOk.
            X-MOZ-LASTACK:20260130T115400Z
            ACKNOWLEDGED:20260130T115400Z
            X-WR-ALARMUID:cf31d062-0599-45ef-9f04-e272312ec155
            END:VALARM
            END:VEVENT
            END:VCALENDAR
            """;

        var referenceTime = new DateTime(2026, 01, 20);
        var result = await _provider.GetNextReminderOccurrencesAsync(rawEventData, referenceTime: referenceTime);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        /*var (occurrence, triggerTime) = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(occurrence.Date, Is.EqualTo(futureStart.Date));
            Assert.That(triggerTime, Is.LessThan(occurrence));
        });*/
    }

    #endregion
}