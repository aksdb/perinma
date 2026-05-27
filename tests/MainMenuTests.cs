using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using CredentialStore;
using Microsoft.Extensions.DependencyInjection;
using perinma.Models;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Views.Calendar;
using perinma.Views.Main;
using tests.Fakes;

namespace tests;

[TestFixture]
public class MainMenuTests
{
    [AvaloniaTest]
    public void AtomMenu_SubmenuOpensInsideAtomWindow()
    {
        var fileMenu = new AtomUI.Desktop.Controls.MenuItem
        {
            Header = "_File",
            ItemsSource = new object[]
            {
                new AtomUI.Desktop.Controls.MenuItem { Header = "_Settings" }
            }
        };

        var menu = new AtomUI.Desktop.Controls.Menu
        {
            ItemsSource = new object[] { fileMenu }
        };

        var window = new AtomUI.Desktop.Controls.Window { Content = menu };
        window.Show();

        try
        {
            Assert.DoesNotThrow(() => fileMenu.Open());
            Assert.That(fileMenu.IsSubMenuOpen, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void MainWindow_UsesAccessTextHeadersForAtomMenuItems()
    {
        var window = new MainWindow();

        var menu = window.FindControl<AtomUI.Desktop.Controls.Menu>("MainMenu");
        Assert.That(menu, Is.Not.Null);

        var menuItems = menu!.Items.OfType<AtomUI.Desktop.Controls.MenuItem>().ToArray();
        Assert.That(menuItems, Has.Length.EqualTo(4));
        Assert.That(menuItems.Select(item => item.Header?.GetType().Name),
            Is.EqualTo(new[] { "AccessText", "AccessText", "AccessText", "AccessText" }));

        var settingsItem = menuItems[0].Items.OfType<AtomUI.Desktop.Controls.MenuItem>().First();
        Assert.That(settingsItem.Header?.GetType().Name, Is.EqualTo("AccessText"));
    }

    [AvaloniaTest]
    public void MainWindow_UsesAtomSplitterForCalendarShell()
    {
        var window = new MainWindow();

        var splitter = window.FindControl<AtomUI.Desktop.Controls.Splitter>("CalendarViewSplitter");
        Assert.That(splitter, Is.Not.Null);
        Assert.That(splitter!.Orientation, Is.EqualTo(Orientation.Vertical));

        var calendarListPane = window.FindControl<Border>("CalendarListPane");
        Assert.That(calendarListPane, Is.Not.Null);
        Assert.That(AtomUI.Desktop.Controls.Splitter.GetDefaultSize(calendarListPane!), Is.EqualTo(new AtomUI.Dimension(250)));
        Assert.That(AtomUI.Desktop.Controls.Splitter.GetMinSize(calendarListPane!), Is.EqualTo(new AtomUI.Dimension(200)));
    }

    [AvaloniaTest]
    public void MainWindow_UsesSegmentedMainModeSwitcher()
    {
        var window = new MainWindow();

        var segmented = window.FindControl<AtomUI.Desktop.Controls.Segmented>("MainModeGroup");
        Assert.That(segmented, Is.Not.Null);

        var calendarItem = window.FindControl<AtomUI.Desktop.Controls.SegmentedItem>("CalendarModeItem");
        var contactsItem = window.FindControl<AtomUI.Desktop.Controls.SegmentedItem>("ContactsModeItem");
        var mailItem = window.FindControl<AtomUI.Desktop.Controls.SegmentedItem>("MailModeItem");
        Assert.That(calendarItem, Is.Not.Null);
        Assert.That(contactsItem, Is.Not.Null);
        Assert.That(mailItem, Is.Not.Null);
        Assert.That(segmented!.Items.Count, Is.EqualTo(3));
    }

    [AvaloniaTest]
    public void MainWindow_ToolsMenuContainsDebugToggleAndDebugWindowItem()
    {
        var window = new MainWindow();

        var enableDebuggingItem = window.FindControl<AtomUI.Desktop.Controls.MenuItem>("EnableDebuggingMenuItem");
        var openDebugWindowItem = window.FindControl<AtomUI.Desktop.Controls.MenuItem>("OpenDebugWindowMenuItem");

        Assert.Multiple(() =>
        {
            Assert.That(enableDebuggingItem, Is.Not.Null);
            Assert.That(openDebugWindowItem, Is.Not.Null);
            Assert.That(enableDebuggingItem!.Header?.GetType().Name, Is.EqualTo("AccessText"));
            Assert.That(openDebugWindowItem!.Header?.GetType().Name, Is.EqualTo("AccessText"));
        });
    }

    [AvaloniaTest]
    public async Task MainWindow_DebugToggleClick_UpdatesMenuVisibilityAndPersistsSetting()
    {
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        using var storage = new SqliteStorage(database, credentialManager);
        var settingsService = new SettingsService(storage);
        var debugFeatures = new DebugFeaturesService(settingsService);
        var themeService = new ThemeService(settingsService);
        var calendarSource = new TestCalendarSource();
        var providers = new Dictionary<AccountType, ICalendarProvider>();
        var reminderService = new ReminderService(storage, calendarSource, providers);
        var syncService = new SyncService(storage, credentialManager, providers, reminderService);
        var contactSyncService = new ContactSyncService(storage, new Dictionary<AccountType, IContactProvider>());
        var mailSyncService = new MailSyncService(storage, new Dictionary<AccountType, IMailProvider>());

        perinma.App.Services = new ServiceCollection()
            .AddSingleton(storage)
            .AddSingleton(syncService)
            .AddSingleton(contactSyncService)
            .AddSingleton(mailSyncService)
            .AddSingleton(reminderService)
            .AddSingleton(debugFeatures)
            .BuildServiceProvider();

        try
        {
            var viewModel = new MainWindowViewModel(
                database,
                credentialManager,
                syncService,
                contactSyncService,
                mailSyncService,
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
            var window = new MainWindow
            {
                DataContext = viewModel
            };

            var enableDebuggingItem = window.FindControl<AtomUI.Desktop.Controls.MenuItem>("EnableDebuggingMenuItem");
            var openDebugWindowItem = window.FindControl<AtomUI.Desktop.Controls.MenuItem>("OpenDebugWindowMenuItem");
            var clickHandler = typeof(MainWindow).GetMethod("EnableDebuggingMenuItem_OnClick", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.Multiple(() =>
            {
                Assert.That(enableDebuggingItem, Is.Not.Null);
                Assert.That(openDebugWindowItem, Is.Not.Null);
                Assert.That(debugFeatures.IsDebuggingEnabled, Is.False);
                Assert.That(enableDebuggingItem!.IsChecked, Is.False);
                Assert.That(openDebugWindowItem!.IsVisible, Is.False);
                Assert.That(clickHandler, Is.Not.Null);
            });
            Assert.That(await settingsService.GetDebuggingEnabledAsync(), Is.False);

            clickHandler!.Invoke(window, new object?[] { enableDebuggingItem, new RoutedEventArgs() });
            await Task.Delay(20);

            Assert.That(debugFeatures.IsDebuggingEnabled, Is.True);
            Assert.That(enableDebuggingItem!.IsChecked, Is.True);
            Assert.That(openDebugWindowItem!.IsVisible, Is.True);
            Assert.That(await settingsService.GetDebuggingEnabledAsync(), Is.True);
        }
        finally
        {
            perinma.App.Services = null;
        }
    }
}
