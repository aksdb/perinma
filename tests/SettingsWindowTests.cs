using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using perinma.Views.Settings;

namespace tests;

[TestFixture]
public class SettingsWindowTests
{
    [AvaloniaTest]
    public void SettingsWindow_UsesAtomNavMenu()
    {
        var window = new SettingsWindow();

        Assert.That(window, Is.InstanceOf<AtomUI.Desktop.Controls.Window>());

        var navMenu = window.FindControl<AtomUI.Desktop.Controls.NavMenu>("SettingsNav");
        Assert.That(navMenu, Is.Not.Null);
        Assert.That(navMenu!.Mode, Is.EqualTo(AtomUI.Desktop.Controls.NavMenuMode.Vertical));
    }
}
