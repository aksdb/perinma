using System.Linq;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using perinma.Views.Main;

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
}
