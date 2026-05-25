using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using CredentialStore;
using Microsoft.Extensions.DependencyInjection;
using perinma.Models;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Views.Calendar;
using perinma.Views.CalendarList;
using tests.Fakes;

namespace tests;

[TestFixture]
public class CalendarListViewTests
{
    [AvaloniaTest]
    public void CalendarListView_UsesAtomSidebarCalendar()
    {
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        using var storage = new SqliteStorage(database, credentialManager);
        perinma.App.Services = new ServiceCollection()
            .AddSingleton(storage)
            .BuildServiceProvider();

        var viewModel = new CalendarListViewModel(
            storage,
            new TestCalendarSource(),
            new GoogleCalendarServiceStub(),
            credentialManager);
        var view = new CalendarListView
        {
            DataContext = viewModel
        };

        AssertAtomControl(view, "SidebarCalendar", "Calendar");
    }

    [AvaloniaTest]
    public void CalendarListView_SyncsActiveViewRangeIntoSidebarCalendarSelection()
    {
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        using var storage = new SqliteStorage(database, credentialManager);
        perinma.App.Services = new ServiceCollection()
            .AddSingleton(storage)
            .BuildServiceProvider();

        var viewModel = new CalendarListViewModel(
            storage,
            new TestCalendarSource(),
            new GoogleCalendarServiceStub(),
            credentialManager);
        var view = new CalendarListView
        {
            DataContext = viewModel
        };
        var host = new AtomUI.Desktop.Controls.Window
        {
            Width = 400,
            Height = 700,
            Content = view
        };
        host.Show();

        try
        {
            var activeView = new SidebarRangeCalendarViewModel();
            viewModel.ActiveCalendarViewModel = activeView;
            activeView.SetRange(
                viewStart: new DateTime(2026, 6, 8),
                highlightStart: new DateTime(2026, 6, 8),
                highlightEnd: new DateTime(2026, 6, 12));

            var sidebarCalendar = view.FindControl<AtomUI.Desktop.Controls.Calendar>("SidebarCalendar");
            Assert.That(sidebarCalendar, Is.Not.Null);

            var selectedDates = sidebarCalendar!.SelectedDates.ToList();
            Assert.Multiple(() =>
            {
                Assert.That(selectedDates, Has.Count.EqualTo(5));
                Assert.That(selectedDates.First(), Is.EqualTo(new DateTime(2026, 6, 8)));
                Assert.That(selectedDates.Last(), Is.EqualTo(new DateTime(2026, 6, 12)));
                Assert.That(sidebarCalendar.SelectionMode, Is.EqualTo(AtomUI.Desktop.Controls.CalendarSelectionMode.SingleRange));
                Assert.That(sidebarCalendar.DisplayDate, Is.EqualTo(new DateTime(2026, 6, 8)));
                Assert.That(sidebarCalendar.SelectedDate, Is.EqualTo(new DateTime(2026, 6, 8)));
            });
        }
        finally
        {
            host.Close();
        }
    }

    private static void AssertAtomControl(Control root, string name, string typeName)
    {
        var control = root.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().Name, Is.EqualTo(typeName), $"Control '{name}' should use AtomUI {typeName}.");
        Assert.That(control.GetType().Namespace, Does.StartWith("AtomUI."));
    }

    private sealed class SidebarRangeCalendarViewModel : CalendarViewModelBase
    {
        public SidebarRangeCalendarViewModel()
            : base(new TestCalendarSource())
        {
        }

        public override string DateRangeDisplay => string.Empty;

        public override void Load()
        {
        }

        public override IReadOnlyList<CalendarEvent> GetEventsInCurrentRange() => [];

        public void SetRange(DateTime viewStart, DateTime? highlightStart, DateTime? highlightEnd)
        {
            HighlightStart = highlightStart;
            HighlightEnd = highlightEnd;
            OnPropertyChanged(nameof(HighlightStart));
            OnPropertyChanged(nameof(HighlightEnd));
            ViewStart = viewStart;
        }

        protected override void PerformNavigationNext()
        {
        }

        protected override void PerformNavigationPrevious()
        {
        }

        protected override void PerformNavigationToday()
        {
        }
    }
}
