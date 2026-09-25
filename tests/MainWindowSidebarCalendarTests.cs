using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CredentialStore;
using Microsoft.Extensions.DependencyInjection;
using perinma.Models;
using perinma.Services;
using perinma.Services.CardDAV;
using perinma.Services.CalDAV;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Views.CalendarList;
using perinma.Views.Main;
using tests.Fakes;

namespace tests;

[TestFixture]
public class MainWindowSidebarCalendarTests
{
    [AvaloniaTest]
    public void MainWindow_WeekView_SidebarHighlightsVisibleWeek()
    {
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        using var storage = new SqliteStorage(database, credentialManager);
        var settingsService = new SettingsService(storage);
        var themeService = new ThemeService(settingsService);
        var debugFeatures = new DebugFeaturesService(settingsService);
        var calendarSource = new TestCalendarSource();
        var providers = new Dictionary<AccountType, ICalendarProvider>();
        var reminderService = new ReminderService(storage, calendarSource, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);
        var contactSyncService = new ContactSyncService(storage, new Dictionary<AccountType, IContactProvider>());
        var mailSyncService = new MailSyncService(storage, new Dictionary<AccountType, IMailProvider>());
        var mailComposeService = new MailComposeAttachmentService(
            Path.Combine(Path.GetTempPath(), "perinma-compose-tests", Guid.NewGuid().ToString("N")));

        perinma.App.Services = new ServiceCollection()
            .AddSingleton(storage)
            .AddSingleton(syncService)
            .BuildServiceProvider();

        MainWindow window = new();
        try
        {
            var viewModel = new MainWindowViewModel(
                database,
                credentialManager,
                syncService,
                contactSyncService,
                mailSyncService,
                new MailComposeService(
                    storage,
                    mailComposeService,
                    new MailComposerService(),
                    new Dictionary<AccountType, IMailComposeProvider>(),
                    new Dictionary<AccountType, IMailProvider>()),
                reminderService,
                new CalDavServiceStub(),
                new CardDavServiceStub(),
                themeService,
                settingsService,
                storage,
                new GoogleCalendarService(),
                new GoogleOAuthService(new GoogleCalendarService()),
                calendarSource,
                debugFeatures);
            window = new MainWindow { DataContext = viewModel };
            window.Show();
            viewModel.AfterLoad();
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }
            var listView = window.GetVisualDescendants().OfType<CalendarListView>().Single();
            var sidebar = listView.FindControl<AtomUI.Desktop.Controls.Calendar>("SidebarCalendar")!;
            var calView = sidebar.GetVisualDescendants().FirstOrDefault(v => v.GetType().Name == "CalendarView");
            var cultureProp = (Avalonia.AvaloniaProperty)calView!.GetType().GetField("CultureProperty")!.GetValue(null)!;
            var finalCulture = (System.Globalization.CultureInfo?)calView.GetValue(cultureProp);


            var monday = Today().AddDays(-(((int)Today().DayOfWeek + 6) % 7));
            var expected = Enumerable.Range(0, 7).Select(i => monday.AddDays(i)).ToList();
            var highlightedDays = sidebar.GetVisualDescendants()
                .OfType<Border>()
                .Where(b => b.Name == "VisibleRangeHighlight" && b.IsVisible && b.Bounds.Width > 0)
                .Select(b => ((AtomUI.Desktop.Controls.CalendarCellContext)((Grid)b.Parent!).DataContext).Value)
                .ToList();

            Assert.That(highlightedDays, Is.EqualTo(expected),
                "sidebar calendar should highlight exactly the visible week of the active week view");

            var cellGrids = sidebar.GetVisualDescendants()
                .OfType<Grid>()
                .Where(g => g.Children.OfType<Border>().Any(b => b.Name == "VisibleRangeHighlight"))
                .Select(g => (
                    Date: ((AtomUI.Desktop.Controls.CalendarCellContext)g.DataContext).Value,
                    Position: g.TranslatePoint(new Avalonia.Point(0, 0), sidebar)!.Value))
                .ToList();
            Assert.That(cellGrids, Is.Not.Empty);
            foreach (var weekRow in cellGrids.GroupBy(c => Math.Round(c.Position.Y, 0)).Where(g => g.Count() == 7))
            {
                var firstCell = weekRow.OrderBy(c => c.Position.X).First();
                Assert.That(firstCell.Date.DayOfWeek, Is.EqualTo(DayOfWeek.Monday),
                    "every sidebar calendar week row must start on Monday like the main view");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static DateTime Today() => DateTime.Today;
}
