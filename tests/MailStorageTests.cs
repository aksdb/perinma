using System.Linq;
using System.Threading.Tasks;
using CredentialStore;
using NUnit.Framework;
using perinma.Models;
using perinma.Storage;
using perinma.Storage.Models;
using perinma.Services;

namespace tests;

[TestFixture]
public class MailStorageTests
{
    [Test]
    public async Task CreateAccountAsync_DefaultsGoogleCapabilities_WhenUnset()
    {
        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));

        var account = new AccountDbo
        {
            AccountId = System.Guid.NewGuid().ToString(),
            Name = "Google",
            Type = AccountType.Google.ToString()
        };

        var created = await storage.CreateAccountAsync(account);
        var persisted = await storage.GetAccountByIdAsync(account.AccountId);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.AccountCapabilities, Is.EqualTo(AccountCapability.Calendar | AccountCapability.Contacts));
        });
    }

    [Test]
    public async Task MailStorage_PersistsMailboxThreadMessageAndAttachmentData()
    {
        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));

        var account = new AccountDbo
        {
            AccountId = System.Guid.NewGuid().ToString(),
            Name = "Inbox",
            Type = AccountType.Jmap.ToString(),
            Capabilities = (int)AccountCapability.Mail
        };
        await storage.CreateAccountAsync(account);

        var mailbox = new MailboxDbo
        {
            AccountId = account.AccountId,
            MailboxId = string.Empty,
            ExternalId = "mbox-1",
            Name = "Inbox",
            Role = "inbox",
            Enabled = 1,
            LastSync = 100
        };
        await storage.CreateOrUpdateMailboxAsync(mailbox);
        await storage.SetMailboxDataAsync(mailbox.MailboxId, "syncToken", "token-1");

        var thread = new MailThreadDbo
        {
            AccountId = account.AccountId,
            ThreadId = string.Empty,
            ExternalId = "thread-1",
            Subject = "Hello",
            ParticipantsSummary = "Sender <sender@example.com>",
            Preview = "Preview",
            LatestMessageReceivedAt = 200,
            MessageCount = 1,
            UnreadCount = 1,
            HasAttachments = 1
        };
        var threadId = await storage.CreateOrUpdateMailThreadAsync(thread);

        var message = new MailMessageDbo
        {
            AccountId = account.AccountId,
            ThreadId = threadId,
            MessageId = string.Empty,
            ExternalId = "msg-1",
            Subject = "Hello",
            SenderName = "Sender",
            SenderAddress = "sender@example.com",
            Preview = "Preview",
            PlainTextBody = "Plain",
            HtmlBody = "<p>Html</p>",
            BodyFetchedAt = 300,
            HasPlainTextBody = 1,
            HasHtmlBody = 1,
            HasAttachments = 1,
            IsUnread = 1,
            ChangedAt = 300
        };
        var messageId = await storage.CreateOrUpdateMailMessageAsync(message, new[] { mailbox.MailboxId });
        await storage.SetMailMessageDataJsonAsync(messageId, "to", "[{\"Name\":\"Receiver\",\"Address\":\"receiver@example.com\"}]");

        await storage.ReplaceAttachmentsAsync(messageId,
        [
            new MailAttachmentDbo
            {
                MessageId = messageId,
                AttachmentId = string.Empty,
                ExternalId = "att-1",
                FileName = "hello.txt",
                MimeType = "text/plain",
                Size = 5,
                IsInline = 0
            }
        ]);

        var mailboxes = (await storage.GetAllMailboxesAsync()).ToList();
        var threads = (await storage.GetMailThreadsByMailboxAsync(mailbox.MailboxId)).ToList();
        var messages = (await storage.GetMailMessagesByThreadAsync(threadId)).ToList();
        var attachments = (await storage.GetAttachmentsByMessageAsync(messageId)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(mailboxes, Has.Count.EqualTo(1));
            Assert.That(mailboxes[0].Role, Is.EqualTo("inbox"));
            Assert.That(threads, Has.Count.EqualTo(1));
            Assert.That(threads[0].Subject, Is.EqualTo("Hello"));
            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0].PlainTextBody, Is.EqualTo("Plain"));
            Assert.That(messages[0].HtmlBody, Does.Contain("Html"));
            Assert.That(attachments, Has.Count.EqualTo(1));
            Assert.That(attachments[0].FileName, Is.EqualTo("hello.txt"));
        });
    }
}
