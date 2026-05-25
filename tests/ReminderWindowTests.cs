using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using CredentialStore;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;
using perinma.Views.Reminders;

namespace tests;

[TestFixture]
public class ReminderWindowTests
{
    [AvaloniaTest]
    public void ReminderNotificationWindow_UsesAtomWindowShell()
    {
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        using var storage = new SqliteStorage(database, credentialManager);
        var reminderService = new ReminderService(storage, new TestCalendarSource(), new Dictionary<AccountType, ICalendarProvider>());
        var window = new ReminderNotificationWindow(reminderService, [CreateReminder()]);

        Assert.That(window, Is.InstanceOf<AtomUI.Desktop.Controls.Window>());
        AssertAtomControl(window, "DismissAllButton", "Button");
        AssertAtomControl(window, "ReminderListScrollViewer", "ScrollViewer");

        window.Close();
    }

    [AvaloniaTest]
    public void ReminderItemControl_UsesAtomFlyoutAndDangerDismissButton()
    {
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        using var storage = new SqliteStorage(database, credentialManager);
        var reminderService = new ReminderService(storage, new TestCalendarSource(), new Dictionary<AccountType, ICalendarProvider>());
        var control = new ReminderItemControl
        {
            DataContext = new ReminderViewModel(reminderService, CreateReminder())
        };

        var host = new AtomUI.Desktop.Controls.Window
        {
            Width = 600,
            Height = 400,
            Content = control
        };
        host.Show();

        try
        {
            var snoozeButton = control.FindControl<AtomUI.Desktop.Controls.Button>("SnoozeButton");
            var dismissButton = control.FindControl<AtomUI.Desktop.Controls.Button>("DismissButton");

            Assert.That(snoozeButton, Is.Not.Null);
            Assert.That(dismissButton, Is.Not.Null);
            Assert.That(snoozeButton!.Flyout, Is.InstanceOf<AtomUI.Desktop.Controls.Flyout>());
            Assert.That(dismissButton!.IsDanger, Is.True);

            var flyoutContent = ((AtomUI.Desktop.Controls.Flyout)snoozeButton.Flyout!).Content as Control;
            Assert.That(flyoutContent, Is.Not.Null);

            var flyoutControls = new[] { flyoutContent! }
                .Concat(flyoutContent!.GetVisualDescendants().OfType<Control>())
                .ToList();
            var firstOption = flyoutControls.OfType<AtomUI.Desktop.Controls.Button>()
                .FirstOrDefault(control => control.Name == "SnoozeOneMinuteButton");
            var lastOption = flyoutControls.OfType<AtomUI.Desktop.Controls.Button>()
                .FirstOrDefault(control => control.Name == "SnoozeWhenItStartsButton");

            Assert.Multiple(() =>
            {
                Assert.That(firstOption, Is.Not.Null);
                Assert.That(lastOption, Is.Not.Null);
            });
        }
        finally
        {
            host.Close();
        }
    }

    private static ReminderWithEvent CreateReminder()
    {
        var now = DateTimeOffset.UtcNow;
        return new ReminderWithEvent
        {
            ReminderId = Guid.NewGuid().ToString(),
            TargetType = 0,
            TargetId = Guid.NewGuid().ToString(),
            TargetTime = now.AddMinutes(30).ToUnixTimeSeconds(),
            TriggerTime = now.ToUnixTimeSeconds(),
            EventTitle = "Team Standup",
            CalendarName = "Work",
            CalendarColor = "#3366FF",
            StartTime = now.AddMinutes(30).ToUnixTimeSeconds(),
            AccountType = AccountType.Google.ToString()
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
