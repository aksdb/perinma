using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using CredentialStore;
using Microsoft.Extensions.DependencyInjection;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Views.Calendar;
using perinma.Views.Calendar.Availability;
using perinma.Views.Calendar.EventEdit;
using perinma.Views.Calendar.EventView;

namespace tests;

[TestFixture]
public class CalendarViewAtomUiTests
{
    [AvaloniaTest]
    public void CalendarMonthView_UsesAtomEventControls()
    {
        var services = CreateCalendarViewServices();
        try
        {
            var control = new CalendarMonthView
            {
                DataContext = CreateMonthViewModel()
            };

            var host = ShowInWindow(control);
            try
            {
                AssertContainsAtomControl(host, "ScrollViewer");
                AssertContainsAtomControl(host, "Button");
            }
            finally
            {
                host.Close();
            }
        }
        finally
        {
            services.Storage.Dispose();
            services.Database.Dispose();
        }
    }

    [AvaloniaTest]
    public void CalendarAgendaView_UsesAtomEventControls()
    {
        var services = CreateCalendarViewServices();
        try
        {
            var control = new CalendarAgendaView
            {
                DataContext = CreateAgendaViewModel()
            };

            var host = ShowInWindow(control);
            try
            {
                AssertContainsAtomControl(host, "Button");
            }
            finally
            {
                host.Close();
            }
        }
        finally
        {
            services.Storage.Dispose();
            services.Database.Dispose();
        }
    }

    [AvaloniaTest]
    public void CalendarWeekView_UsesAtomScrollViewers()
    {
        var control = new CalendarWeekView();

        AssertAtomControl(control, "TopView", "ScrollViewer");
        AssertAtomControl(control, "TimeRows", "ScrollViewer");
        AssertAtomControl(control, "CenterView", "ScrollViewer");
    }

    [AvaloniaTest]
    public void CalendarDialogs_UseAtomWindowsAndInputs()
    {
        var recurrenceDialog = new RecurrenceActionDialog
        {
            DataContext = new RecurrenceActionDialogViewModel(
                "Edit recurring event",
                "Standup",
                [new RecurrenceActionOption("This event", RecurringEventAction.EditOccurrence)])
        };

        var sendInvitesDialog = new SendInvitesDialog
        {
            DataContext = new SendInvitesDialogViewModel()
        };

        Assert.That(recurrenceDialog, Is.InstanceOf<AtomUI.Desktop.Controls.Window>());
        Assert.That(sendInvitesDialog, Is.InstanceOf<AtomUI.Desktop.Controls.Window>());
        AssertAtomControl(recurrenceDialog, "CancelButton", "Button");
        AssertAtomControl(sendInvitesDialog, "SendToAllButton", "RadioButton");
        AssertAtomControl(sendInvitesDialog, "SendToExternalOnlyButton", "RadioButton");
        AssertAtomControl(sendInvitesDialog, "SendToNoneButton", "RadioButton");
        AssertAtomControl(sendInvitesDialog, "OkButton", "Button");
        AssertAtomControl(sendInvitesDialog, "CancelButton", "Button");
    }

    [AvaloniaTest]
    public void EventParticipationViews_UseAtomControls()
    {
        var participationView = new ParticipationView
        {
            DataContext = new ParticipationViewModel(new Participation
            {
                CurrentState = EventResponseStatus.NeedsAction,
                Actions = new ParticipationActions
                {
                    Accept = () => Task.CompletedTask,
                    Decline = () => Task.CompletedTask,
                    Tentative = () => Task.CompletedTask
                }
            })
        };

        var participantsView = new ParticipantsView
        {
            DataContext = new ParticipantsViewModel(CreateSampleParticipants())
        };

        AssertAtomControl(participationView, "AcceptButton", "Button");
        AssertAtomControl(participationView, "DeclineButton", "Button");
        AssertAtomControl(participationView, "TentativeButton", "Button");
        AssertAtomControl(participantsView, "ParticipantsScrollViewer", "ScrollViewer");
    }

    [AvaloniaTest]
    public void AvailabilityWindow_UsesAtomWindowShell()
    {
        var window = new AvailabilityWindow();

        Assert.That(window, Is.InstanceOf<AtomUI.Desktop.Controls.Window>());
        AssertAtomControl(window, "UseSlotButton", "Button");
        AssertAtomControl(window, "CancelButton", "Button");
    }

    private static (DatabaseService Database, SqliteStorage Storage) CreateCalendarViewServices()
    {
        var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        var storage = new SqliteStorage(database, credentialManager);

        var services = new ServiceCollection();
        services.AddSingleton(storage);
        perinma.App.Services = services.BuildServiceProvider();

        return (database, storage);
    }

    private static CalendarMonthViewModel CreateMonthViewModel()
    {
        var viewModel = new CalendarMonthViewModel(new DummyCalendarSource(DateTime.Today));
        viewModel.Load();
        return viewModel;
    }

    private static CalendarAgendaViewModel CreateAgendaViewModel()
    {
        var viewModel = new CalendarAgendaViewModel(new DummyCalendarSource(DateTime.Today));
        viewModel.Load();
        return viewModel;
    }

    private static List<CalendarEventParticipant> CreateSampleParticipants()
    {
        return
        [
            new CalendarEventParticipant
            {
                Email = "alice@example.com",
                Name = "Alice Example",
                Status = EventResponseStatus.Accepted,
                IsOrganizer = true
            }
        ];
    }

    private static Window ShowInWindow(Control control)
    {
        var window = new Window
        {
            Width = 1200,
            Height = 800,
            Content = control
        };
        window.Show();
        return window;
    }

    private static void AssertAtomControl(Control root, string name, string typeName)
    {
        var control = root.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().Name, Is.EqualTo(typeName), $"Control '{name}' should use AtomUI {typeName}.");
        Assert.That(control.GetType().Namespace, Does.StartWith("AtomUI."));
    }

    private static void AssertContainsAtomControl(TopLevel root, string typeName)
    {
        var control = root.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(candidate => candidate.GetType().Name == typeName &&
                                         candidate.GetType().Namespace?.StartsWith("AtomUI.") == true);

        Assert.That(control, Is.Not.Null, $"Missing AtomUI {typeName} in visual tree.");
    }
}
