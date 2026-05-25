using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using CredentialStore;
using NodaTime;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Views.Debug;
using tests.Fakes;

namespace tests;

[TestFixture]
public class DebugWindowTests
{
    [AvaloniaTest]
    public void DebugWindow_UsesReminderTriggerChecklistControls()
    {
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        using var storage = new SqliteStorage(database, credentialManager);
        var reminderService = new ReminderService(storage, new TestCalendarSource(), new Dictionary<AccountType, ICalendarProvider>());
        var window = new DebugWindow
        {
            DataContext = new DebugWindowViewModel(
                reminderService,
                () => [CreateSampleCalendarEvent()],
                () => "Current range")
        };

        AssertAtomControl(window, "TriggerReminderEventList", "ScrollViewer");
        AssertAtomControl(window, "TriggerRemindersButton", "Button");
        Assert.That(window.FindControl<TextBlock>("TriggerReminderEmptyState"), Is.Not.Null);
    }

    private static CalendarEvent CreateSampleCalendarEvent()
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
            Title = "Standup",
            StartTime = LocalDateTime.FromDateTime(DateTime.Today.AddHours(9)),
            EndTime = LocalDateTime.FromDateTime(DateTime.Today.AddHours(10))
        };
    }

    private static void AssertAtomControl(Control root, string name, string typeName)
    {
        var control = root.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().Name, Is.EqualTo(typeName), $"Control '{name}' should use AtomUI {typeName}.");
        Assert.That(control.GetType().Namespace, Does.StartWith("AtomUI."));
    }
}
