using Avalonia.Controls;
using Avalonia.Headless.NUnit;

namespace tests;

[TestFixture]
public class MainMenuTests
{
    [AvaloniaTest]
    public void AvaloniaMenu_SubmenuOpensWithoutPopupHostFailure()
    {
        var fileMenu = new MenuItem
        {
            Header = "_File",
            ItemsSource = new object[]
            {
                new MenuItem { Header = "_Settings" }
            }
        };

        var menu = new Menu
        {
            ItemsSource = new object[] { fileMenu }
        };

        var window = new Window { Content = menu };
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
