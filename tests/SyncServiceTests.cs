using Dapper;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Storage.Models;
using perinma.Tests.Fakes;
using tests.Base;

namespace tests;

public class SyncServiceTests : SyncTestBase
{
    [Test]
    public async Task WithNewCalendars_SavesCalendarsToDatabase()
    {
        // Arrange
        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true, color: "#ff0000"),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Personal Calendar", selected: true, color: "#00ff00"),
            FakeGoogleCalendarService.CreateCalendar("cal3", "Disabled Calendar", selected: false)
        );

        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
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
        var account = await CreateGoogleAccountAsync();

        // Create 3 existing calendars from a "previous sync"
        await CreateExistingCalendarsAsync(account.AccountId,
            ("cal1", "Work Calendar", "#ff0000"),
            ("cal2", "Personal Calendar", "#00ff00"),
            ("cal3", "Old Calendar", "#0000ff")
        );

        StoreGoogleCredentials(account.AccountId);

        // Store an invalid/expired sync token to simulate a previous sync
        await Storage.SetAccountData(account, "calendarSyncToken", "invalid-expired-token");

        // Set up fake service to return only 2 calendars:
        // - cal1 unchanged
        // - cal2 with updated name and disabled
        // - cal3 not returned (deleted remotely)
        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true, color: "#ff0000"),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Personal Calendar - Updated", selected: false, color: "#00ff00")
        );

        // Simulate invalid sync token on first call, then succeed on retry with full sync
        FakeGoogleService.SetInvalidSyncTokenBehavior(true);

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendarList = calendars.ToList();

        // Should have only 2 calendars now (cal3 deleted)
        Assert.That(calendarList, Has.Count.EqualTo(2));

        // cal1 should still exist unchanged
        var resultCal1 = calendarList.FirstOrDefault(c => c.ExternalId == "cal1");
        Assert.That(resultCal1, Is.Not.Null);
        Assert.That(resultCal1!.Name, Is.EqualTo("Work Calendar"));
        Assert.That(resultCal1.Enabled, Is.EqualTo(1));

        // cal2 should exist with updated data
        var resultCal2 = calendarList.FirstOrDefault(c => c.ExternalId == "cal2");
        Assert.That(resultCal2, Is.Not.Null);
        Assert.That(resultCal2!.Name, Is.EqualTo("Personal Calendar - Updated"));
        Assert.That(resultCal2.Enabled, Is.EqualTo(0)); // Changed to disabled

        // cal3 should be deleted
        var resultCal3 = calendarList.FirstOrDefault(c => c.ExternalId == "cal3");
        Assert.That(resultCal3, Is.Null);
    }

    [Test]
    public async Task FullSync_DeletesRemovedCalendars()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        // First sync: 3 calendars
        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Calendar 1"),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Calendar 2"),
            FakeGoogleCalendarService.CreateCalendar("cal3", "Calendar 3")
        );

        // Perform first sync
        await SyncService.SyncAllAccountsAsync();

        // Verify all 3 calendars were created
        var calendarsAfterFirstSync = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        Assert.That(calendarsAfterFirstSync.Count(), Is.EqualTo(3));

        // Clear the sync token to force a full sync (simulating token expiration or manual refresh)
        await Storage.SetAccountData(account, "calendarSyncToken", "");

        // Wait 1 second to ensure the second sync has a different timestamp
        await Task.Delay(1000);

        // Second sync: Only 2 calendars (cal3 removed remotely)
        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Calendar 1"),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Calendar 2")
        );

        // Act - Perform second full sync (no sync token means full sync)
        await SyncService.SyncAllAccountsAsync();

        // Assert - Verify cal3 was deleted from database
        var calendarsAfterSecondSync = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendarList = calendarsAfterSecondSync.ToList();

        Assert.That(calendarList, Has.Count.EqualTo(2));
        Assert.That(calendarList.Any(c => c.ExternalId == "cal1"), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal2"), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal3"), Is.False);
    }

    [Test]
    public async Task CalDavCalendar_PreservesEnabledStatusAcrossSyncs()
    {
        // Arrange
        var account = await CreateCalDavAccountAsync();
        StoreCalDavCredentials(account.AccountId);

        // First sync: Create calendars with default enabled status (CalDAV always returns selected=true)
        FakeCalDavService.SetCalendars(
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/work",
                DisplayName = "Work Calendar",
                Deleted = false
            },
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/personal",
                DisplayName = "Personal Calendar",
                Deleted = false
            }
        );

        // Act - First sync
        await SyncService.SyncAllAccountsAsync();

        // Assert - Both calendars should be enabled by default
        var calendarsAfterFirstSync = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendarListAfterFirstSync = calendarsAfterFirstSync.ToList();

        Assert.That(calendarListAfterFirstSync, Has.Count.EqualTo(2));
        Assert.That(calendarListAfterFirstSync.Any(c => c.ExternalId == "https://caldav.example.com/calendars/work" && c.Enabled == 1), Is.True);
        Assert.That(calendarListAfterFirstSync.Any(c => c.ExternalId == "https://caldav.example.com/calendars/personal" && c.Enabled == 1), Is.True);

        // Disable one calendar (simulating user action in UI)
        var workCalendar = calendarListAfterFirstSync.First(c => c.ExternalId == "https://caldav.example.com/calendars/work");
        await Storage.UpdateCalendarEnabledAsync(workCalendar.CalendarId, false);

        // Verify calendar is disabled
        var calendarsAfterDisable = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var workCalendarAfterDisable = calendarsAfterDisable.First(c => c.ExternalId == "https://caldav.example.com/calendars/work");
        Assert.That(workCalendarAfterDisable.Enabled, Is.EqualTo(0));

        // Act - Second sync (CalDAV still returns selected=true for all calendars)
        await SyncService.SyncAllAccountsAsync();

        // Assert - The disabled status should be preserved, not overwritten
        var calendarsAfterSecondSync = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendarListAfterSecondSync = calendarsAfterSecondSync.ToList();

        Assert.That(calendarListAfterSecondSync, Has.Count.EqualTo(2));
        Assert.That(calendarListAfterSecondSync.Any(c => c.ExternalId == "https://caldav.example.com/calendars/work" && c.Enabled == 0), Is.True);
        Assert.That(calendarListAfterSecondSync.Any(c => c.ExternalId == "https://caldav.example.com/calendars/personal" && c.Enabled == 1), Is.True);
    }

    [Test]
    public async Task WithDeletedFlag_SkipsDeletedCalendars()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        // Set up fake service with one active calendar and one deleted calendar
        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Active Calendar"),
            FakeGoogleCalendarService.CreateDeletedCalendar("cal2")
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert - Only cal1 should be saved, cal2 should be skipped
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendarList = calendars.ToList();

        Assert.That(calendarList, Has.Count.EqualTo(1));
        Assert.That(calendarList.Any(c => c.ExternalId == "cal1"), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal2"), Is.False);
    }

    [Test]
    public async Task SyncsEventsForEnabledCalendars()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        // Set up fake service with calendar and events
        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var eventStart = DateTime.UtcNow.AddHours(1);
        var eventEnd = DateTime.UtcNow.AddHours(2);
        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateEvent("event1", "Team Meeting", eventStart, eventEnd),
            FakeGoogleCalendarService.CreateEvent("event2", "Lunch Break", eventStart.AddHours(2), eventEnd.AddHours(2))
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert - Verify calendar was created
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        Assert.That(calendar.ExternalId, Is.EqualTo("cal1"));

        // Verify events were synced
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(2));
        Assert.That(eventList.Any(e => e.ExternalId == "event1" && e.Title == "Team Meeting"), Is.True);
        Assert.That(eventList.Any(e => e.ExternalId == "event2" && e.Title == "Lunch Break"), Is.True);

        // Verify raw event data is stored
        var event1 = eventList.First(e => e.ExternalId == "event1");
        var rawData = await Storage.GetEventData(event1.EventId, "rawData");
        Assert.That(rawData, Is.Not.Null.And.Not.Empty);
        Assert.That(rawData, Does.Contain("Team Meeting"));
        Assert.That(rawData, Does.Contain("event1"));
    }

    #region Recurrence Handling Tests

    [Test]
    public async Task GoogleRecurringEvent_WithUntilClause_StoresRecurrenceEndTime()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        // Create a recurring event with UNTIL clause
        // Weekly meeting starting Jan 1, 2025 at 10:00 UTC, ending March 31, 2025
        var eventStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring1",
                "Weekly Team Sync",
                eventStart,
                eventEnd,
                "RRULE:FREQ=WEEKLY;UNTIL=20250331T235959Z"
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc, Is.EqualTo(new DateTime(2025, 3, 26, 11, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task GoogleRecurringEvent_WithCountClause_StoresCalculatedEndTime()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        // Create a daily recurring event with COUNT=5
        // Starting Jan 15, 2025 at 14:00 UTC, 2 hours duration
        var eventStart = new DateTime(2025, 1, 15, 14, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 15, 16, 0, 0, DateTimeKind.Utc);
        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring2",
                "Daily Standup",
                eventStart,
                eventEnd,
                "RRULE:FREQ=DAILY;COUNT=5"
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // 5 daily occurrences starting Jan 15: Jan 15, 16, 17, 18, 19
        // Last occurrence ends Jan 19 at 16:00 UTC
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(1));
        Assert.That(endTimeUtc.Day, Is.EqualTo(19));
        Assert.That(endTimeUtc.Hour, Is.EqualTo(16));
    }

    [Test]
    public async Task GoogleRecurringEvent_WithTimezone_HandlesTimezoneCorrectly()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        // Create a recurring event with timezone (America/New_York = UTC-5 in winter)
        // Monthly event at 9:00 AM New York time for 3 months
        var eventStart = new DateTime(2025, 2, 1, 9, 0, 0, DateTimeKind.Utc); // 9 AM in UTC for test
        var eventEnd = new DateTime(2025, 2, 1, 10, 0, 0, DateTimeKind.Utc);
        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEventWithTimezone(
                "recurring3",
                "Monthly Review",
                eventStart,
                eventEnd,
                "America/New_York",
                "RRULE:FREQ=MONTHLY;COUNT=3"
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // 3 monthly occurrences: Feb 1, Mar 1, Apr 1
        // Last occurrence ends Apr 1 at 10:00 UTC
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(4));
        Assert.That(endTimeUtc.Day, Is.EqualTo(1));
    }

    [Test]
    public async Task GoogleRecurringEvent_WithNoEndClause_SetsMaximumEndTime()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        // Create an infinite recurring event (no UNTIL or COUNT)
        var eventStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring4",
                "Weekly Infinite Meeting",
                eventStart,
                eventEnd,
                "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR"
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // For infinite recurrence, EndTime should be the max available value, since there is no
        // theoretical end.
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime.Date;
        Assert.That(endTimeUtc, Is.EqualTo(DateTimeOffset.MaxValue.UtcDateTime.Date));
    }

    [Test]
    public async Task CalDavRecurringEvent_WithUntilClause_StoresRecurrenceEndTime()
    {
        // Arrange
        var account = await CreateCalDavAccountAsync();
        StoreCalDavCredentials(account.AccountId);

        FakeCalDavService.SetCalendars(new CalDavCalendar
        {
            Url = "https://caldav.example.com/calendars/work",
            DisplayName = "Work Calendar",
            Deleted = false
        });

        // Create a recurring event with UNTIL clause via raw iCalendar
        var eventStart = new DateTime(2025, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 3, 1, 10, 30, 0, DateTimeKind.Utc);
        FakeCalDavService.SetEvents(
            "https://caldav.example.com/calendars/work",
            FakeCalDavService.CreateRecurringEvent(
                "caldav-recurring1",
                "https://caldav.example.com/calendars/work/event1.ics",
                "Weekly Planning",
                eventStart,
                eventEnd,
                "RRULE:FREQ=WEEKLY;UNTIL=20250601T235959Z"
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // The last occurrence should end on 31.05. at 10:30.
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc, Is.EqualTo(new DateTime(2025, 5, 31, 10, 30, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task CalDavRecurringEvent_WithCountClause_StoresCalculatedEndTime()
    {
        // Arrange
        var account = await CreateCalDavAccountAsync();
        StoreCalDavCredentials(account.AccountId);

        FakeCalDavService.SetCalendars(new CalDavCalendar
        {
            Url = "https://caldav.example.com/calendars/personal",
            DisplayName = "Personal Calendar",
            Deleted = false
        });

        // Create a recurring event with COUNT clause
        // Daily for 10 occurrences, starting May 1, 2025
        var eventStart = new DateTime(2025, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 5, 1, 8, 30, 0, DateTimeKind.Utc);
        FakeCalDavService.SetEvents(
            "https://caldav.example.com/calendars/personal",
            FakeCalDavService.CreateRecurringEvent(
                "caldav-recurring2",
                "https://caldav.example.com/calendars/personal/event2.ics",
                "Morning Routine",
                eventStart,
                eventEnd,
                "RRULE:FREQ=DAILY;COUNT=10"
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // 10 daily occurrences starting May 1: May 1-10
        // Last occurrence ends May 10 at 8:30 UTC
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(5));
        Assert.That(endTimeUtc.Day, Is.EqualTo(10));
        Assert.That(endTimeUtc.Hour, Is.EqualTo(8));
        Assert.That(endTimeUtc.Minute, Is.EqualTo(30));
    }

    [Test]
    public async Task CalDavRecurringEvent_WithTimezone_HandlesTimezoneCorrectly()
    {
        // Arrange
        var account = await CreateCalDavAccountAsync();
        StoreCalDavCredentials(account.AccountId);

        FakeCalDavService.SetCalendars(new CalDavCalendar
        {
            Url = "https://caldav.example.com/calendars/europe",
            DisplayName = "Europe Calendar",
            Deleted = false
        });

        // Create a recurring event with Europe/Berlin timezone
        // Weekly for 4 occurrences, starting June 1, 2025 at 15:00 local time
        var eventStart = new DateTime(2025, 6, 1, 15, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 6, 1, 16, 0, 0, DateTimeKind.Utc);
        FakeCalDavService.SetEvents(
            "https://caldav.example.com/calendars/europe",
            FakeCalDavService.CreateRecurringEventWithTimezone(
                "caldav-recurring3",
                "https://caldav.example.com/calendars/europe/event3.ics",
                "European Team Call",
                eventStart,
                eventEnd,
                "Europe/Berlin",
                "RRULE:FREQ=WEEKLY;COUNT=4"
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var recurringEvent = eventList.First();

        // 4 weekly occurrences: June 1, 8, 15, 22
        // Last occurrence ends June 22 at 16:00 UTC
        Assert.That(recurringEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(recurringEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(6));
        Assert.That(endTimeUtc.Day, Is.EqualTo(22));
    }

    [Test]
    public async Task NonRecurringEvent_KeepsOriginalEndTime()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        // Create a non-recurring event
        var eventStart = new DateTime(2025, 7, 15, 14, 0, 0, DateTimeKind.Utc);
        var eventEnd = new DateTime(2025, 7, 15, 15, 30, 0, DateTimeKind.Utc);
        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateEvent(
                "single-event",
                "One-time Meeting",
                eventStart,
                eventEnd
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(1));
        var singleEvent = eventList.First();

        // EndTime should be the original event end time
        Assert.That(singleEvent.EndTime, Is.Not.Null);
        var endTimeUtc = DateTimeOffset.FromUnixTimeSeconds(singleEvent.EndTime!.Value).UtcDateTime;
        Assert.That(endTimeUtc.Year, Is.EqualTo(2025));
        Assert.That(endTimeUtc.Month, Is.EqualTo(7));
        Assert.That(endTimeUtc.Day, Is.EqualTo(15));
        Assert.That(endTimeUtc.Hour, Is.EqualTo(15));
        Assert.That(endTimeUtc.Minute, Is.EqualTo(30));
    }

    #endregion

    #region Force Resync Tests

    [Test]
    public async Task ForceResync_ClearsAllDataAndPerformsFullSync()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        // Initial sync with 3 calendars and events
        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Calendar 1", selected: true),
            FakeGoogleCalendarService.CreateCalendar("cal2", "Calendar 2", selected: true),
            FakeGoogleCalendarService.CreateCalendar("cal3", "Calendar 3", selected: true)
        );

        var eventStart = DateTime.UtcNow.AddHours(1);
        var eventEnd = DateTime.UtcNow.AddHours(2);
        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateEvent("event1", "Event 1", eventStart, eventEnd)
        );
        FakeGoogleService.SetEvents("cal2",
            FakeGoogleCalendarService.CreateEvent("event2", "Event 2", eventStart, eventEnd)
        );

        // Perform initial sync
        await SyncService.SyncAllAccountsAsync();

        // Verify initial state
        var calendarsBeforeResync = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        Assert.That(calendarsBeforeResync.Count(), Is.EqualTo(3));

        var cal1 = calendarsBeforeResync.First(c => c.ExternalId == "cal1");
        var eventsBeforeResync = await Storage.GetEventsByCalendarAsync(cal1.CalendarId);
        Assert.That(eventsBeforeResync.Count(), Is.EqualTo(1));

        // Store a sync token to verify it gets cleared
        await Storage.SetAccountData(account, "calendarSyncToken", "some-sync-token");
        var tokenBefore = await Storage.GetAccountData(account, "calendarSyncToken");
        Assert.That(tokenBefore, Is.EqualTo("some-sync-token"));

        // Now simulate remote changes - only 2 calendars exist now
        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Calendar 1 - Updated", selected: true),
            FakeGoogleCalendarService.CreateCalendar("cal4", "New Calendar", selected: true)
        );
        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateEvent("event1", "Event 1 - Updated", eventStart, eventEnd),
            FakeGoogleCalendarService.CreateEvent("event3", "New Event", eventStart.AddHours(3), eventEnd.AddHours(3))
        );
        FakeGoogleService.SetEvents("cal4",
            FakeGoogleCalendarService.CreateEvent("event4", "Event in New Calendar", eventStart, eventEnd)
        );

        // Act - Force resync
        var result = await SyncService.ForceResyncAccountAsync(account.AccountId);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.SyncedAccounts, Is.EqualTo(1));
        Assert.That(result.FailedAccounts, Is.EqualTo(0));

        // Verify sync token was cleared (or has a new value from the fresh sync)
        var tokenAfter = await Storage.GetAccountData(account, "calendarSyncToken");
        Assert.That(tokenAfter, Is.Not.EqualTo("some-sync-token"));

        // Verify calendars reflect the new remote state
        var calendarsAfterResync = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendarList = calendarsAfterResync.ToList();

        Assert.That(calendarList, Has.Count.EqualTo(2));
        Assert.That(calendarList.Any(c => c.ExternalId == "cal1" && c.Name == "Calendar 1 - Updated"), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal4" && c.Name == "New Calendar"), Is.True);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal2"), Is.False);
        Assert.That(calendarList.Any(c => c.ExternalId == "cal3"), Is.False);

        // Verify events reflect the new remote state
        var updatedCal1 = calendarList.First(c => c.ExternalId == "cal1");
        var eventsAfterResync = await Storage.GetEventsByCalendarAsync(updatedCal1.CalendarId);
        var eventList = eventsAfterResync.ToList();

        Assert.That(eventList, Has.Count.EqualTo(2));
        Assert.That(eventList.Any(e => e.ExternalId == "event1" && e.Title == "Event 1 - Updated"), Is.True);
        Assert.That(eventList.Any(e => e.ExternalId == "event3" && e.Title == "New Event"), Is.True);
    }

    [Test]
    public async Task ForceResync_WithInvalidAccountId_ReturnsError()
    {
        // Act
        var result = await SyncService.ForceResyncAccountAsync("non-existent-account-id");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(result.Errors[0], Does.Contain("not found"));
    }

    [Test]
    public async Task ForceResync_CalDavAccount_ClearsDataAndResyncs()
    {
        // Arrange
        var account = await CreateCalDavAccountAsync();
        StoreCalDavCredentials(account.AccountId);

        FakeCalDavService.SetCalendars(new CalDavCalendar
        {
            Url = "https://caldav.example.com/calendars/work",
            DisplayName = "Work Calendar",
            Deleted = false
        });

        // Perform initial sync
        await SyncService.SyncAllAccountsAsync();

        // Verify initial state
        var calendarsBeforeResync = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        Assert.That(calendarsBeforeResync.Count(), Is.EqualTo(1));

        // Store a sync token
        await Storage.SetAccountData(account, "calendarSyncToken", "caldav-sync-token");

        // Change remote state
        FakeCalDavService.SetCalendars(
            new CalDavCalendar
            {
                Url = "https://caldav.example.com/calendars/personal",
                DisplayName = "Personal Calendar",
                Deleted = false
            }
        );

        // Act - Force resync
        var result = await SyncService.ForceResyncAccountAsync(account.AccountId);

        // Assert
        Assert.That(result.Success, Is.True);

        var calendarsAfterResync = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendarList = calendarsAfterResync.ToList();

        Assert.That(calendarList, Has.Count.EqualTo(1));
        Assert.That(calendarList[0].Name, Is.EqualTo("Personal Calendar"));
        Assert.That(calendarList[0].ExternalId, Is.EqualTo("https://caldav.example.com/calendars/personal"));
    }

    [Test]
    public async Task ClearAccountSyncData_RemovesCalendarsEventsAndSyncToken()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();

        // Create calendars
        var cal1 = new CalendarDbo
        {
            AccountId = account.AccountId,
            CalendarId = Guid.NewGuid().ToString(),
            ExternalId = "cal1",
            Name = "Calendar 1",
            Enabled = 1,
            LastSync = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await Storage.CreateOrUpdateCalendarAsync(cal1);

        // Create events
        var event1 = new CalendarEventDbo
        {
            CalendarId = cal1.CalendarId,
            EventId = Guid.NewGuid().ToString(),
            ExternalId = "event1",
            Title = "Event 1",
            StartTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            EndTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            ChangedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await Storage.CreateOrUpdateEventAsync(event1);

        // Store sync token
        await Storage.SetAccountData(account, "calendarSyncToken", "test-sync-token");

        // Verify data exists
        var calendarsBeforeClear = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        Assert.That(calendarsBeforeClear.Count(), Is.EqualTo(1));

        var eventsBeforeClear = await Storage.GetEventsByCalendarAsync(cal1.CalendarId);
        Assert.That(eventsBeforeClear.Count(), Is.EqualTo(1));

        var tokenBeforeClear = await Storage.GetAccountData(account, "calendarSyncToken");
        Assert.That(tokenBeforeClear, Is.EqualTo("test-sync-token"));

        // Act
        await Storage.ClearAccountSyncDataAsync(account.AccountId);

        // Assert - All data should be cleared
        var calendarsAfterClear = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        Assert.That(calendarsAfterClear.Count(), Is.EqualTo(0));

        // Events are cascade-deleted with calendars, but verify by checking the account still exists
        var accountAfterClear = await Storage.GetAccountByIdAsync(account.AccountId);
        Assert.That(accountAfterClear, Is.Not.Null);

        // Sync token should be cleared (empty JSON object means key won't exist)
        var tokenAfterClear = await Storage.GetAccountData(account, "calendarSyncToken");
        Assert.That(string.IsNullOrEmpty(tokenAfterClear), Is.True);
    }

    #endregion

    #region Google Calendar Override Tests

    [Test]
    public async Task GoogleCancelledOverride_StoresWithOriginalStartAsStartAndEnd()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var recurringStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var recurringEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);

        var overrideTime = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);

        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring1",
                "Weekly Meeting",
                recurringStart,
                recurringEnd,
                "RRULE:FREQ=WEEKLY;BYDAY=WE"
            ),
            FakeGoogleCalendarService.CreateCancelledOverride(
                "override1",
                "recurring1",
                overrideTime
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(2));

        var overrideEvent = eventList.FirstOrDefault(e => e.ExternalId == "override1");
        Assert.That(overrideEvent, Is.Not.Null);

        // For cancelled override, start and end should both be the original start time
        var startTimestamp = overrideEvent!.StartTime!.Value;
        var endTimestamp = overrideEvent.EndTime!.Value;
        var startUtc = DateTimeOffset.FromUnixTimeSeconds(startTimestamp).UtcDateTime;
        var endUtc = DateTimeOffset.FromUnixTimeSeconds(endTimestamp).UtcDateTime;

        Assert.That(startUtc, Is.EqualTo(overrideTime));
        Assert.That(endUtc, Is.EqualTo(overrideTime));
    }

    [Test]
    public async Task GoogleModifiedOverride_WithTimeOutsideBounds_ExpandsBounds()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var recurringStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var recurringEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);

        var originalStartTime = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newStart = new DateTime(2025, 1, 8, 9, 0, 0, DateTimeKind.Utc); // Earlier than original
        var newEnd = new DateTime(2025, 1, 8, 10, 30, 0, DateTimeKind.Utc);

        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring1",
                "Weekly Meeting",
                recurringStart,
                recurringEnd,
                "RRULE:FREQ=WEEKLY;BYDAY=WE"
            ),
            FakeGoogleCalendarService.CreateModifiedOverride(
                "override1",
                "recurring1",
                "Extended Meeting",
                originalStartTime,
                newStart,
                newEnd
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(2));

        var overrideEvent = eventList.FirstOrDefault(e => e.ExternalId == "override1");
        Assert.That(overrideEvent, Is.Not.Null);

        // Bounds should be expanded to include original start time (10:00)
        var startTimestamp = overrideEvent!.StartTime!.Value;
        var startUtc = DateTimeOffset.FromUnixTimeSeconds(startTimestamp).UtcDateTime;

        Assert.That(startUtc, Is.EqualTo(newStart)); // Starts at 9:00 (original new start)
        Assert.That(startUtc, Is.LessThan(originalStartTime)); // Includes original start time
    }

    [Test]
    public async Task GoogleOverride_WithExistingParent_CreatesRelation()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var recurringStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var recurringEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        var overrideTime = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newStart = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newEnd = new DateTime(2025, 1, 8, 11, 30, 0, DateTimeKind.Utc);

        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring1",
                "Weekly Meeting",
                recurringStart,
                recurringEnd,
                "RRULE:FREQ=WEEKLY;BYDAY=WE"
            ),
            FakeGoogleCalendarService.CreateModifiedOverride(
                "override1",
                "recurring1",
                "Rescheduled Meeting",
                overrideTime,
                newStart,
                newEnd
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();

        Assert.That(eventList, Has.Count.EqualTo(2));

        var parentEvent = eventList.FirstOrDefault(e => e.ExternalId == "recurring1");
        var overrideEvent = eventList.FirstOrDefault(e => e.ExternalId == "override1");
        Assert.That(parentEvent, Is.Not.Null);
        Assert.That(overrideEvent, Is.Not.Null);

        // Verify relation was created
        var relation = await GetEventRelationAsync(parentEvent!.EventId, overrideEvent!.EventId);
        Assert.That(relation, Is.Not.Null);
        Assert.That(relation!.Value.ParentEventId, Is.EqualTo(parentEvent.EventId));
        Assert.That(relation.Value.ChildEventId, Is.EqualTo(overrideEvent.EventId));
    }

    [Test]
    public async Task GoogleOverride_WithParentAfterOverride_CreatesRelationAfterBacklogProcessing()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var recurringStart = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var recurringEnd = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        var overrideTime = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newStart = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newEnd = new DateTime(2025, 1, 8, 11, 30, 0, DateTimeKind.Utc);

        // First sync: Override arrives before parent
        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateModifiedOverride(
                "override1",
                "recurring1",
                "Rescheduled Meeting",
                overrideTime,
                newStart,
                newEnd
            )
        );

        // Act - First sync
        await SyncService.SyncAllAccountsAsync();

        // Assert - Override exists but no parent yet
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();
        var eventsAfterFirstSync = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventListAfterFirstSync = eventsAfterFirstSync.ToList();

        Assert.That(eventListAfterFirstSync, Has.Count.EqualTo(1));
        Assert.That(eventListAfterFirstSync[0].ExternalId, Is.EqualTo("override1"));

        // Second sync: Parent arrives
        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateRecurringEvent(
                "recurring1",
                "Weekly Meeting",
                recurringStart,
                recurringEnd,
                "RRULE:FREQ=WEEKLY;BYDAY=WE"
            ),
            FakeGoogleCalendarService.CreateModifiedOverride(
                "override1",
                "recurring1",
                "Rescheduled Meeting",
                overrideTime,
                newStart,
                newEnd
            )
        );

        // Act - Second sync
        await SyncService.SyncAllAccountsAsync();

        // Assert - Both events exist with relation
        var eventsAfterSecondSync = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventListAfterSecondSync = eventsAfterSecondSync.ToList();

        Assert.That(eventListAfterSecondSync, Has.Count.EqualTo(2));

        var parentEvent = eventListAfterSecondSync.FirstOrDefault(e => e.ExternalId == "recurring1");
        var overrideEvent = eventListAfterSecondSync.FirstOrDefault(e => e.ExternalId == "override1");
        Assert.That(parentEvent, Is.Not.Null);
        Assert.That(overrideEvent, Is.Not.Null);

        var relation = await GetEventRelationAsync(parentEvent!.EventId, overrideEvent!.EventId);
        Assert.That(relation, Is.Not.Null);
        Assert.That(relation!.Value.ParentEventId, Is.EqualTo(parentEvent.EventId));
        Assert.That(relation.Value.ChildEventId, Is.EqualTo(overrideEvent.EventId));
    }

    [Test]
    public async Task GoogleOverride_WithParentNeverArrived_StaysInBacklog()
    {
        // Arrange
        var account = await CreateGoogleAccountAsync();
        StoreGoogleCredentials(account.AccountId);

        FakeGoogleService.SetCalendars(
            FakeGoogleCalendarService.CreateCalendar("cal1", "Work Calendar", selected: true)
        );

        var overrideTime = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newStart = new DateTime(2025, 1, 8, 10, 0, 0, DateTimeKind.Utc);
        var newEnd = new DateTime(2025, 1, 8, 11, 30, 0, DateTimeKind.Utc);

        // Sync with only override, parent never arrives
        FakeGoogleService.SetEvents("cal1",
            FakeGoogleCalendarService.CreateModifiedOverride(
                "override1",
                "recurring1",
                "Rescheduled Meeting",
                overrideTime,
                newStart,
                newEnd
            )
        );

        // Act
        await SyncService.SyncAllAccountsAsync();

        // Assert
        var calendars = await Storage.GetCalendarsByAccountAsync(account.AccountId);
        var calendar = calendars.First();

        // Override should be stored
        var events = await Storage.GetEventsByCalendarAsync(calendar.CalendarId);
        var eventList = events.ToList();
        Assert.That(eventList, Has.Count.EqualTo(1));
        Assert.That(eventList[0].ExternalId, Is.EqualTo("override1"));

        // Backlog should contain the pending relation
        var backlogItems = await GetRelationBacklogAsync(calendar.CalendarId);
        Assert.That(backlogItems, Has.Length.EqualTo(1));
        Assert.That(backlogItems[0].ParentExternalId, Is.EqualTo("recurring1"));
        Assert.That(backlogItems[0].ChildExternalId, Is.EqualTo("override1"));

        // No relation should exist yet
        using var connection = Database!.GetConnection();
        var relations = await connection.QueryAsync<string>(
            "SELECT child_event_id FROM calendar_event_relation WHERE parent_event_id = @EventId OR child_event_id = @EventId",
            new { EventId = eventList[0].EventId }
        );

        Assert.That(relations.Any(), Is.False);
    }

    #endregion
}
