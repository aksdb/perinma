using Avalonia;
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
            Assert.That(((Button)button).Padding, Is.EqualTo(new Thickness(0)));
            Assert.That(((Button)button).MinWidth, Is.EqualTo(0));
            Assert.That(((Button)button).MinHeight, Is.EqualTo(0));
            Assert.That(((Button)button).HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Left));
        });
    }
}
