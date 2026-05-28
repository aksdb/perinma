using System.Linq;
using Avalonia.VisualTree;

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
        AssertAtomControl(view, "ComposeMailButton", "Button");
        AssertAtomControl(view, "LocalDraftsButton", "Button");
        AssertAtomControl(view, "RefreshMailButton", "Button");
        AssertAtomControl(view, "ReplyMailButton", "Button");
        AssertAtomControl(view, "ReplyAllMailButton", "Button");
        AssertAtomControl(view, "ForwardMailButton", "Button");
        AssertAtomControl(view, "EditDraftMailButton", "Button");
        AssertAtomControl(view, "MarkReadButton", "Button");
        AssertAtomControl(view, "ArchiveMailButton", "Button");
        AssertAtomControl(view, "DeleteMailButton", "Button");
        AssertAtomControl(view, "BodyModeComboBox", "ComboBox");

        var htmlView = view.FindControl<SecureMailHtmlView>("SecureMailHtmlPreview");
        Assert.That(htmlView, Is.Not.Null, "Missing secure mail HTML preview control.");
    }

    [AvaloniaTest]
    public void MailView_HtmlPreview_IsNotNestedInsideScrollViewer()
    {
        var view = new MailView();

        var htmlView = view.FindControl<SecureMailHtmlView>("SecureMailHtmlPreview");
        var bodyScrollViewer = view.FindControl<Control>("MessageBodyScrollViewer");
        Assert.That(htmlView, Is.Not.Null);
        Assert.That(bodyScrollViewer, Is.Not.Null);
        Assert.That(bodyScrollViewer!.GetType().Namespace, Does.StartWith("AtomUI."));

        var scrollViewerAncestor = htmlView!.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        Assert.That(scrollViewerAncestor, Is.Null, "Native HTML preview must not be hosted inside a ScrollViewer.");
    }

    private static void AssertAtomControl(Control root, string name, string typeName)
    {
        var control = root.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().Name, Is.EqualTo(typeName), $"Control '{name}' should use AtomUI {typeName}.");
        Assert.That(control.GetType().Namespace, Does.StartWith("AtomUI."));
    }
}
