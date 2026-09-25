using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
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

        var sidebarCalendar = view.FindControl<AtomUI.Desktop.Controls.Calendar>("SidebarCalendar");
        Assert.That(sidebarCalendar, Is.Not.Null);
        Assert.That(sidebarCalendar!.Fullscreen, Is.False, "sidebar calendar should use the compact month layout");
    }

    [AvaloniaTest]
    public void CalendarListView_HighlightsVisiblePeriodInSidebarCalendar()
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
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var sidebarCalendar = view.FindControl<AtomUI.Desktop.Controls.Calendar>("SidebarCalendar");
            Assert.That(sidebarCalendar, Is.Not.Null);
            Assert.That(sidebarCalendar!.Fullscreen, Is.False, "sidebar calendar should use the compact month layout");
            Assert.That(sidebarCalendar.Value, Is.EqualTo(new DateTime(2026, 6, 8)));

            var highlights = sidebarCalendar.GetVisualDescendants()
                .OfType<Border>()
                .Where(b => b.Name == "VisibleRangeHighlight")
                .ToList();
            Assert.That(highlights, Is.Not.Empty, "day cells with highlight badge should exist");
            var visibleDays = highlights
                .Where(b => b.IsVisible && b.Bounds.Width > 0 && b.Bounds.Height > 0)
                .Select(b => ((Grid)b.Parent!).Children.OfType<TextBlock>().Single().Text)
                .ToList();
            Assert.That(visibleDays, Is.EqualTo(new[] { "08", "09", "10", "11", "12" }));
            Assert.That(highlights.Where(b => b.IsVisible), Has.All.Property("Bounds").Property("Width").GreaterThan(0),
                "highlight badges must have non-zero rendered size");
        }
        finally
        {
            host.Close();
        }
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
