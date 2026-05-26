using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using CredentialStore;
using Microsoft.Extensions.DependencyInjection;
using perinma.Models;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Storage.Models;
using perinma.Views.Calendar;
using tests.Fakes;
namespace tests;

[TestFixture]
public class EventEditWindowTests
{
    [AvaloniaTest]
    public void EventEditView_CalendarDropdownOpensInsideAtomWindow()
    {
        var window = new EventEditView();
        window.Show();

        try
        {
            var comboBox = window.FindControl<AtomUI.Desktop.Controls.ComboBox>("CalendarComboBox");
            Assert.That(comboBox, Is.Not.Null);

            Assert.DoesNotThrow(() => comboBox!.IsDropDownOpen = true);
            Assert.That(comboBox.IsDropDownOpen, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void EventEditView_FieldRowsUseAtomExpanders()
    {
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        using var storage = new SqliteStorage(database, credentialManager);
        var googleProvider = new GoogleCalendarProvider(new GoogleCalendarServiceStub(), credentialManager);
        var providers = new Dictionary<AccountType, ICalendarProvider>
        {
            [AccountType.Google] = googleProvider
        };
        var calendarSource = new DatabaseCalendarSource(storage, providers);
        var services = new ServiceCollection();
        services.AddSingleton(storage);
        services.AddSingleton<ICalendarSource>(calendarSource);
        services.AddSingleton(new SyncService(storage, credentialManager, providers, null!));
        perinma.App.Services = services.BuildServiceProvider();

        var accountId = Guid.NewGuid();
        var account = new Account
        {
            Id = accountId,
            Name = "Test Account",
            Type = AccountType.Google,
            SortOrder = 0
        };

        storage.CreateAccountAsync(new AccountDbo
        {
            AccountId = accountId.ToString(),
            Name = account.Name,
            Type = account.Type.ToString(),
            SortOrder = account.SortOrder
        }).Wait();

        var calendarDbo = new CalendarDbo
        {
            AccountId = accountId.ToString(),
            CalendarId = Guid.NewGuid().ToString(),
            ExternalId = "test-calendar",
            Name = "Test Calendar",
            Color = "#ff0000",
            Enabled = 1
        };
        storage.CreateOrUpdateCalendarAsync(calendarDbo).Wait();

        var calendar = new perinma.Models.Calendar
        {
            Account = account,
            Id = Guid.Parse(calendarDbo.CalendarId),
            ExternalId = calendarDbo.ExternalId,
            Name = calendarDbo.Name,
            Color = calendarDbo.Color,
            Enabled = true
        };

        var window = new EventEditView
        {
            DataContext = new EventEditViewModel(null, null, calendar, _ => { })
        };
        window.Show();

        try
        {
            var expander = window.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control => control.GetType().Name == "Expander" &&
                                           control.GetType().Namespace?.StartsWith("AtomUI.") == true);

            Assert.That(expander, Is.Not.Null, "Expected EventEditView field rows to render AtomUI expanders.");
        }
        finally
        {
            window.Close();
        }
    }
}
