using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using CredentialStore;
using NUnit.Framework;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;
using perinma.Views.Mail;

namespace tests;

[TestFixture]
public class ComposeMailWindowAtomUiTests
{
    [AvaloniaTest]
    public void ComposeMailWindow_UsesExpectedControls()
    {
        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var composeService = new MailComposeService(
            storage,
            new MailComposeAttachmentService(Path.Combine(Path.GetTempPath(), "perinma-compose-tests", Guid.NewGuid().ToString("N"))),
            new MailComposerService(),
            new Dictionary<AccountType, IMailComposeProvider>(),
            new Dictionary<AccountType, IMailProvider>());
        var draft = new MailComposeDraft
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Subject = "Compose"
        };
        var viewModel = new ComposeMailViewModel(composeService, draft);
        var window = new ComposeMailWindow { DataContext = viewModel };

        Assert.Multiple(() =>
        {
            AssertAtomControl(window, "AddAttachmentButton", "Button");
            AssertAtomControl(window, "InsertImageButton", "Button");
            Assert.That(window.FindControl<ComposeMailEditorView>("EditorView"), Is.Not.Null);
        });
    }
    [Test]
    public void ComposeEditorView_QueuesFocusUntilWebViewIsReady()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "../../../../src/Views/Mail/ComposeMailEditorView.axaml.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private bool _focusPending;"));
            Assert.That(source, Does.Contain("_focusPending = true;"));
            Assert.That(source, Does.Contain("await FlushPendingFocusAsync();"));
            Assert.That(source, Does.Contain("if (!_focusPending || !_isReady || _webView == null)"));
        });
    }

    [Test]
    public void ComposeEditorAsset_DefinesVisibleCaretAndFocusCaretPlacement()
    {
        var assetPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "../../../../src/Assets/MailComposeEditor/editor.html"));
        var html = File.ReadAllText(assetPath);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("caret-color: transparent;"));
            Assert.That(html, Does.Contain("id=\"caretOverlay\""));
            Assert.That(html, Does.Contain("function updateCaret()"));
            Assert.That(html, Does.Contain("function requestCaretUpdate()"));
            Assert.That(html, Does.Contain("editor.addEventListener('focus', () =>"));
            Assert.That(html, Does.Contain("document.addEventListener('selectionchange', () =>"));
        });
    }

    private static void AssertAtomControl(Control root, string name, string typeName)
    {
        var control = root.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().Name, Is.EqualTo(typeName), $"Control '{name}' should use AtomUI {typeName}.");
        Assert.That(control.GetType().Namespace, Does.StartWith("AtomUI."));
    }
}