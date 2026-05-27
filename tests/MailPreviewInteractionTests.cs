using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
public class MailPreviewInteractionTests
{
    [AvaloniaTest]
    public async Task MailView_SelectedMessageRemainsSelectedAfterHydration()
    {
        using var database = new DatabaseService(inMemory: true);
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        using var storage = new SqliteStorage(database, credentialManager);

        var account = new AccountDbo
        {
            AccountId = "account-1",
            Name = "Mail Test",
            Type = AccountType.Jmap.ToString(),
            Capabilities = (int)AccountCapability.Mail
        };
        await storage.CreateAccountAsync(account);

        var mailbox = new MailboxDbo
        {
            AccountId = account.AccountId,
            MailboxId = string.Empty,
            ExternalId = "mailbox-1",
            Name = "Inbox",
            Role = "inbox",
            Enabled = 1,
            LastSync = 100
        };
        await storage.CreateOrUpdateMailboxAsync(mailbox);

        var threadId = await storage.CreateOrUpdateMailThreadAsync(new MailThreadDbo
        {
            AccountId = account.AccountId,
            ThreadId = string.Empty,
            ExternalId = "thread-1",
            Subject = "Hydrate me",
            ParticipantsSummary = "Sender <sender@example.com>",
            Preview = "Preview text",
            LatestMessageReceivedAt = 200,
            MessageCount = 1,
            UnreadCount = 1,
            HasAttachments = 0
        });

        await storage.CreateOrUpdateMailMessageAsync(new MailMessageDbo
        {
            AccountId = account.AccountId,
            ThreadId = threadId,
            MessageId = string.Empty,
            ExternalId = "message-1",
            Subject = "Hydrate me",
            SenderName = "Sender",
            SenderAddress = "sender@example.com",
            Preview = "Preview text",
            HasPlainTextBody = 1,
            HasHtmlBody = 0,
            HasAttachments = 0,
            IsUnread = 1,
            ChangedAt = 200,
            ReceivedAt = 200
        },
        [mailbox.MailboxId]);

        var provider = new HydratingMailProvider();
        var mailSyncService = new MailSyncService(storage, new Dictionary<AccountType, IMailProvider>
        {
            [AccountType.Jmap] = provider
        });

        var viewModel = new MailViewModel(storage, mailSyncService);
        var view = new MailView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();

        try
        {
            await WaitUntilAsync(() => viewModel.Messages.Count == 1 && viewModel.SelectedMessage != null);

            viewModel.SelectedMessage = viewModel.Messages[0];
            await WaitUntilAsync(() => viewModel.SelectedMessage?.PlainTextBody == HydratingMailProvider.HydratedBody);
            await Task.Delay(100);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.HasSelectedMessage, Is.True);
                Assert.That(viewModel.SelectedMessage, Is.SameAs(viewModel.Messages[0]));
                Assert.That(viewModel.SelectedMessageBodyText, Is.EqualTo(HydratingMailProvider.HydratedBody));
                Assert.That(provider.HydrateCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(System.Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (condition())
                return;

            await Task.Delay(20);
        }

        Assert.Fail("Timed out waiting for condition.");
    }

    private sealed class HydratingMailProvider : IMailProvider
    {
        public const string HydratedBody = "Hydrated plain text body";

        public CredentialManagerService CredentialManager { get; } = new(new InMemoryCredentialStore());

        public int HydrateCalls { get; private set; }

        public Task<MailboxSyncResult> GetMailboxesAsync(string accountId, string? syncToken = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new MailboxSyncResult { Mailboxes = [] });

        public Task<MailMessageSyncResult> GetMessagesAsync(string accountId, string mailboxExternalId, string? syncToken = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new MailMessageSyncResult { Messages = [], MissingMessagesAreAuthoritative = false });

        public Task<HydratedMailMessage> HydrateMessageAsync(string accountId, string messageExternalId, CancellationToken cancellationToken = default)
        {
            HydrateCalls++;
            return Task.FromResult(new HydratedMailMessage
            {
                Message = new ProviderMailMessage
                {
                    ExternalId = messageExternalId,
                    ThreadExternalId = "thread-1",
                    Subject = "Hydrate me",
                    SenderName = "Sender",
                    SenderAddress = "sender@example.com",
                    Preview = "Preview text",
                    PlainTextBody = HydratedBody,
                    HasPlainTextBody = true,
                    HasHtmlBody = false,
                    HasAttachments = false,
                    HasExternalResources = false,
                    HasBlockedContent = false,
                    IsUnread = true,
                    IsStarred = false,
                    IsAnswered = false,
                    IsDraft = false,
                    ReceivedAtUnixTime = 200,
                    MailboxExternalIds = ["mailbox-1"]
                }
            });
        }

        public Task<DownloadedMailAttachment> DownloadAttachmentAsync(string accountId, string messageExternalId, string attachmentExternalId, CancellationToken cancellationToken = default)
            => throw new System.NotSupportedException();

        public Task SetReadStateAsync(string accountId, string messageExternalId, bool isRead, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetStarredStateAsync(string accountId, string messageExternalId, bool isStarred, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ArchiveMessageAsync(string accountId, string messageExternalId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteMessageAsync(string accountId, string messageExternalId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TestConnectionAsync(string accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
