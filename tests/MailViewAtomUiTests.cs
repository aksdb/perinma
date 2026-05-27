using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using perinma.Views.Mail;

namespace tests;

[TestFixture]
public class MailViewAtomUiTests
{
    [AvaloniaTest]
    public void MailView_UsesAtomMailControls()
    {
        var view = new MailView();

        AssertAtomControl(view, "MailboxListBox", "ListBox");
        AssertAtomControl(view, "ThreadListBox", "ListBox");
        AssertAtomControl(view, "RefreshMailButton", "Button");
        AssertAtomControl(view, "MarkReadButton", "Button");
        AssertAtomControl(view, "ArchiveMailButton", "Button");
        AssertAtomControl(view, "DeleteMailButton", "Button");
        AssertAtomControl(view, "BodyModeComboBox", "ComboBox");

        var htmlView = view.FindControl<SecureMailHtmlView>("SecureMailHtmlPreview");
        Assert.That(htmlView, Is.Not.Null, "Missing secure mail HTML preview control.");
    }

    private static void AssertAtomControl(Control root, string name, string typeName)
    {
        var control = root.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().Name, Is.EqualTo(typeName), $"Control '{name}' should use AtomUI {typeName}.");
        Assert.That(control.GetType().Namespace, Does.StartWith("AtomUI."));
    }
}
