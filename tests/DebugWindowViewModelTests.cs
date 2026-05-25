using System;
using System.Linq;
using System.Collections.Generic;
using CredentialStore;
using NodaTime;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Views.Debug;
using tests.Fakes;

namespace tests;

[TestFixture]
public class DebugWindowViewModelTests
{
    [Test]
    public void RefreshTriggerEvents_LoadsCurrentRangeEvents()
    {
        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var reminderService = new ReminderService(storage, new TestCalendarSource(), new Dictionary<AccountType, ICalendarProvider>());
        var laterEvent = CreateSampleCalendarEvent("Later", DateTime.Today.AddHours(12));
        var earlierEvent = CreateSampleCalendarEvent("Earlier", DateTime.Today.AddHours(9));
        var viewModel = new DebugWindowViewModel(
            reminderService,
            () => [laterEvent, earlierEvent],
            () => "Week range");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.TriggerRangeDescription, Is.EqualTo("Week range"));
            Assert.That(viewModel.TriggerEvents.Select(item => item.Title), Is.EqualTo(new[] { "Earlier", "Later" }));
            Assert.That(viewModel.HasTriggerEvents, Is.True);
            Assert.That(viewModel.HasNoTriggerEvents, Is.False);
        });

        viewModel.TriggerEvents[0].IsSelected = true;

        Assert.That(viewModel.SelectedTriggerEventCount, Is.EqualTo(1));
    }

    [Test]
    public async Task TriggerRemindersAsync_WithoutSelection_ShowsPrompt()
    {
        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var reminderService = new ReminderService(storage, new TestCalendarSource(), new Dictionary<AccountType, ICalendarProvider>());
        var viewModel = new DebugWindowViewModel(
            reminderService,
            () => [CreateSampleCalendarEvent("Standup", DateTime.Today.AddHours(9))],
            () => "Day range");

        await viewModel.TriggerRemindersCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.TriggerStatusText, Is.EqualTo("Select one or more events."));
            Assert.That(viewModel.HasTriggerErrors, Is.False);
            Assert.That(viewModel.IsTriggeringReminders, Is.False);
        });
    }

    private static CalendarEvent CreateSampleCalendarEvent(string title, DateTime start)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Debug",
            Type = AccountType.Google
        };
        var calendar = new perinma.Models.Calendar
        {
            Id = Guid.NewGuid(),
            Account = account,
            Name = "Work",
            Color = "#3366FF",
            Enabled = true
        };

        return new CalendarEvent
        {
            Reference = new EventReference
            {
                Id = Guid.NewGuid(),
                Calendar = calendar
            },
            Title = title,
            StartTime = LocalDateTime.FromDateTime(start),
            EndTime = LocalDateTime.FromDateTime(start.AddHours(1))
        };
    }
}
