using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using perinma.Views.Common;

namespace tests;

[TestFixture]
public class HyperlinkTests
{
    [AvaloniaTest]
    public void Hyperlink_UsesAtomLinkButtonWithBoundContent()
    {
        var control = new Hyperlink
        {
            DisplayText = "Open calendar",
            Uri = "https://example.com/calendar"
        };

        var button = control.FindControl<Control>("LinkButton");

        Assert.Multiple(() =>
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(button!.GetType().Name, Is.EqualTo("Button"));
            Assert.That(button.GetType().Namespace, Does.StartWith("AtomUI."));
            Assert.That(((ContentControl)button).Content, Is.EqualTo("Open calendar"));
            Assert.That(button.ContextMenu, Is.Not.Null);
        });
    }
}
