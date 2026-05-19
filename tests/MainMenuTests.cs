using Avalonia.Headless.NUnit;

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
}
