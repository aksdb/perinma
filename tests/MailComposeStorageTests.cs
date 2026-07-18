using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CredentialStore;
using NUnit.Framework;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;

namespace tests;

[TestFixture]
public class MailComposeStorageTests
{
    [Test]
    public async Task MailComposeDraftStorage_PersistsAndReplacesRecipientsAndAttachments()
    {
        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));

        var account = new AccountDbo
        {
            AccountId = Guid.NewGuid().ToString(),
            Name = "Mail",
            Type = AccountType.Jmap.ToString(),
            Capabilities = (int)AccountCapability.Mail
        };
        await storage.CreateAccountAsync(account);

        var draft = new MailComposeDraftDbo
        {
            DraftId = string.Empty,
            AccountId = account.AccountId,
            ComposeKind = MailComposeKind.ReplyAll.ToString(),
            Subject = "First subject",
            HtmlBody = "<p>Hello</p>",
            PlainTextBody = "Hello",
            Status = MailComposeDraftStatus.LocalOnly.ToString(),
            LastLocalSaveAt = 100,
            UpdatedAt = 100
        };

        var draftId = await storage.CreateOrUpdateMailComposeDraftAsync(
            draft,
            [
                new MailComposeRecipientDbo
                {
                    DraftId = string.Empty,
                    RecipientId = string.Empty,
                    RecipientKind = MailRecipientKind.To.ToString(),
                    DisplayName = "Receiver",
                    Address = "receiver@example.com",
                    SortOrder = 0
                }
            ],
            [
                new MailComposeAttachmentDbo
                {
                    DraftId = string.Empty,
                    AttachmentId = string.Empty,
                    FileName = "inline.png",
                    MimeType = "image/png",
                    Size = 12,
                    IsInline = 1,
                    ContentId = "inline@perinma.local",
                    StagedFilePath = "/tmp/inline.png",
                    ContentHash = "abc",
                    SortOrder = 0
                }
            ]);

        draft.Subject = "Updated subject";
        draft.Status = MailComposeDraftStatus.PendingRemoteSave.ToString();
        draft.LastRemoteSaveAt = 200;
        draft.UpdatedAt = 200;

        await storage.CreateOrUpdateMailComposeDraftAsync(
            draft,
            [
                new MailComposeRecipientDbo
                {
                    DraftId = draftId,
                    RecipientId = string.Empty,
                    RecipientKind = MailRecipientKind.Cc.ToString(),
                    DisplayName = "Copy",
                    Address = "copy@example.com",
                    SortOrder = 0
                }
            ],
            [
                new MailComposeAttachmentDbo
                {
                    DraftId = draftId,
                    AttachmentId = string.Empty,
                    FileName = "document.pdf",
                    MimeType = "application/pdf",
                    Size = 42,
                    IsInline = 0,
                    StagedFilePath = "/tmp/document.pdf",
                    ContentHash = "def",
                    ProviderAttachmentReferenceJson = "{\"blobId\":\"blob-1\"}",
                    SortOrder = 0
                }
            ]);

        var persistedDraft = await storage.GetMailComposeDraftByIdAsync(draftId);
        var drafts = (await storage.GetMailComposeDraftsAsync(account.AccountId)).ToList();
        var recipients = (await storage.GetMailComposeRecipientsAsync(draftId)).ToList();
        var attachments = (await storage.GetMailComposeAttachmentsAsync(draftId)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(draftId, Is.Not.Empty);
            Assert.That(persistedDraft, Is.Not.Null);
            Assert.That(persistedDraft!.Subject, Is.EqualTo("Updated subject"));
            Assert.That(persistedDraft.StatusEnum, Is.EqualTo(MailComposeDraftStatus.PendingRemoteSave));
            Assert.That(drafts, Has.Count.EqualTo(1));
            Assert.That(recipients, Has.Count.EqualTo(1));
            Assert.That(recipients[0].RecipientKindEnum, Is.EqualTo(MailRecipientKind.Cc));
            Assert.That(recipients[0].Address, Is.EqualTo("copy@example.com"));
            Assert.That(attachments, Has.Count.EqualTo(1));
            Assert.That(attachments[0].FileName, Is.EqualTo("document.pdf"));
            Assert.That(attachments[0].ProviderAttachmentReferenceJson, Does.Contain("blob-1"));
        });
    }

    [Test]
    public async Task MailComposeDraftStorage_DeleteRemovesRecipientsAndAttachments()
    {
        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));

        var account = new AccountDbo
        {
            AccountId = Guid.NewGuid().ToString(),
            Name = "Mail",
            Type = AccountType.Google.ToString(),
            Capabilities = (int)AccountCapability.Mail
        };
        await storage.CreateAccountAsync(account);

        var draftId = await storage.CreateOrUpdateMailComposeDraftAsync(
            new MailComposeDraftDbo
            {
                DraftId = string.Empty,
                AccountId = account.AccountId,
                Subject = "Draft",
                HtmlBody = "<p>Draft</p>",
                PlainTextBody = "Draft",
                UpdatedAt = 123
            },
            [
                new MailComposeRecipientDbo
                {
                    DraftId = string.Empty,
                    RecipientId = string.Empty,
                    RecipientKind = MailRecipientKind.To.ToString(),
                    Address = "person@example.com",
                    SortOrder = 0
                }
            ],
            [
                new MailComposeAttachmentDbo
                {
                    DraftId = string.Empty,
                    AttachmentId = string.Empty,
                    FileName = "draft.txt",
                    MimeType = "text/plain",
                    Size = 5,
                    IsInline = 0,
                    StagedFilePath = "/tmp/draft.txt",
                    SortOrder = 0
                }
            ]);

        var deleted = await storage.DeleteMailComposeDraftAsync(draftId);
        var persistedDraft = await storage.GetMailComposeDraftByIdAsync(draftId);
        var recipients = (await storage.GetMailComposeRecipientsAsync(draftId)).ToList();
        var attachments = (await storage.GetMailComposeAttachmentsAsync(draftId)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(persistedDraft, Is.Null);
            Assert.That(recipients, Is.Empty);
            Assert.That(attachments, Is.Empty);
        });
    }
}
