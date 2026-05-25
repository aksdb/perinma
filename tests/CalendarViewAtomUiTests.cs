using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
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
    public void EventItem_TrySetFlyoutContent_SupportsAtomFlyout()
    {
        var flyout = new AtomUI.Desktop.Controls.Flyout();
        var content = new object();
        var method = typeof(EventItem).GetMethod("TrySetFlyoutContent", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);

        var result = method!.Invoke(null, [flyout, content]);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(true));
            Assert.That(flyout.Content, Is.SameAs(content));
        });
    }

    [AvaloniaTest]
    public void EventItem_ConfigureContextMenu_WiresAtomMenuCommands()
    {
        var calendarEvent = CreateSampleCalendarEvent();
        var editCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<CalendarEvent?>(_ => { });
        var deleteCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<CalendarEvent?>(_ => { });
        var triggerReminderCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<CalendarEvent?>(_ => { });
        var contextMenu = new AtomUI.Desktop.Controls.ContextMenu
        {
            Items =
            {
                new AtomUI.Desktop.Controls.MenuItem { Header = "Edit" },
                new AtomUI.Desktop.Controls.MenuItem { Header = "Delete" },
                new AtomUI.Desktop.Controls.MenuItem { Header = "Trigger Reminder" }
            }
        };
        var method = typeof(EventItem).GetMethod("ConfigureContextMenu", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventItem = new EventItem
        {
            CalendarEvent = calendarEvent,
            EditEventCommand = editCommand,
            DeleteEventCommand = deleteCommand,
            TriggerReminderCommand = triggerReminderCommand
        };

        Assert.That(method, Is.Not.Null);

        method!.Invoke(eventItem, [contextMenu]);

        var menuItems = contextMenu.Items.OfType<Avalonia.Controls.MenuItem>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(menuItems[0].Command, Is.SameAs(editCommand));
            Assert.That(menuItems[0].CommandParameter, Is.SameAs(calendarEvent));
            Assert.That(menuItems[1].Command, Is.SameAs(deleteCommand));
            Assert.That(menuItems[1].CommandParameter, Is.SameAs(calendarEvent));
            Assert.That(menuItems[2].Command, Is.SameAs(triggerReminderCommand));
            Assert.That(menuItems[2].CommandParameter, Is.SameAs(calendarEvent));
        });
    }

    [AvaloniaTest]
    public async Task TriggerReminderCommand_WhenDebuggingDisabled_NoOps()
    {
        var services = CreateCalendarViewServices();
        try
        {
            var viewModel = new CalendarMonthViewModel(
                new DummyCalendarSource(DateTime.Today),
                debugFeatures: new DebugFeaturesService());

            Assert.DoesNotThrowAsync(async () => await viewModel.TriggerReminderCommand.ExecuteAsync(CreateSampleCalendarEvent()));
        }
        finally
        {
            services.Storage.Dispose();
            services.Database.Dispose();
        }
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
    public void ParticipationView_UsesPaletteStyledButtonsForCurrentState()
    {
        var participationView = new ParticipationView
        {
            DataContext = new ParticipationViewModel(new Participation
            {
                CurrentState = EventResponseStatus.Accepted,
                Actions = new ParticipationActions
                {
                    Accept = () => Task.CompletedTask,
                    Decline = () => Task.CompletedTask,
                    Tentative = () => Task.CompletedTask
                }
            })
        };

        var host = ShowInWindow(participationView);
        try
        {
            var acceptButton = participationView.FindControl<AtomUI.Desktop.Controls.Button>("AcceptButton");
            var declineButton = participationView.FindControl<AtomUI.Desktop.Controls.Button>("DeclineButton");
            var tentativeButton = participationView.FindControl<AtomUI.Desktop.Controls.Button>("TentativeButton");
            var acceptBrush = GetBrushColor(participationView, "ParticipationAcceptBrush");
            var declineBrush = GetBrushColor(participationView, "ParticipationDeclineBrush");
            var tentativeBrush = GetBrushColor(participationView, "ParticipationTentativeBrush");

            Assert.Multiple(() =>
            {
                Assert.That(acceptButton, Is.Not.Null);
                Assert.That(declineButton, Is.Not.Null);
                Assert.That(tentativeButton, Is.Not.Null);

                Assert.That(acceptButton!.Shape, Is.EqualTo(AtomUI.Desktop.Controls.ButtonShape.Round));
                Assert.That(acceptButton.Classes.Contains("active"), Is.True);
                Assert.That(declineButton!.Classes.Contains("active"), Is.False);
                Assert.That(tentativeButton!.Classes.Contains("active"), Is.False);

                Assert.That(GetSolidColor(acceptButton.BorderBrush), Is.EqualTo(acceptBrush));
                Assert.That(GetSolidColor(acceptButton.Background), Is.EqualTo(acceptBrush));
                Assert.That(GetSolidColor(declineButton.BorderBrush), Is.EqualTo(declineBrush));
                Assert.That(GetSolidColor(declineButton.Foreground), Is.EqualTo(declineBrush));
                Assert.That(GetSolidColor(tentativeButton.BorderBrush), Is.EqualTo(tentativeBrush));
                Assert.That(GetSolidColor(tentativeButton.Foreground), Is.EqualTo(tentativeBrush));
            });
        }
        finally
        {
            host.Close();
        }
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

    private static CalendarEvent CreateSampleCalendarEvent()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Test Account",
            Type = AccountType.Google
        };

        var calendar = new perinma.Models.Calendar
        {
            Account = account,
            Id = Guid.NewGuid(),
            Name = "Test Calendar",
            Color = "#3366FF",
            Enabled = true
        };

        return new CalendarEvent
        {
            Reference = new EventReference
            {
                Calendar = calendar,
                Id = Guid.NewGuid()
            },
            Title = "Test Event",
            StartTime = NodaTime.LocalDateTime.FromDateTime(DateTime.Today.AddHours(9)),
            EndTime = NodaTime.LocalDateTime.FromDateTime(DateTime.Today.AddHours(10))
        };
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

    private static Color GetBrushColor(Control root, string resourceKey)
    {
        var resource = root.Resources[resourceKey];
        Assert.That(resource, Is.InstanceOf<SolidColorBrush>(), $"Resource '{resourceKey}' should be a SolidColorBrush.");
        return ((SolidColorBrush)resource!).Color;
    }

    private static Color? GetSolidColor(IBrush? brush)
    {
        return brush is ISolidColorBrush solidColorBrush ? solidColorBrush.Color : null;
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
