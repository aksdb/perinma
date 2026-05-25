using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using CredentialStore;
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
    public void DebugWindow_UsesReminderTriggerControls()
    {
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        using var storage = new SqliteStorage(database, credentialManager);
        var reminderService = new ReminderService(storage, new TestCalendarSource(), new Dictionary<AccountType, ICalendarProvider>());
        var window = new DebugWindow
        {
            DataContext = new DebugWindowViewModel(reminderService)
        };

        AssertAtomControl(window, "TriggerReminderEventIdsInput", "TextBox");
        AssertAtomControl(window, "TriggerRemindersButton", "Button");
    }

    private static void AssertAtomControl(Control root, string name, string typeName)
    {
        var control = root.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().Name, Is.EqualTo(typeName), $"Control '{name}' should use AtomUI {typeName}.");
        Assert.That(control.GetType().Namespace, Does.StartWith("AtomUI."));
    }
}
