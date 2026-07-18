using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CredentialStore;
using NUnit.Framework;
using perinma.Models;
using perinma.Services;
using perinma.Storage;
using perinma.Storage.Models;

namespace tests;

[TestFixture]
public class MailComposeServiceTests
{
    [Test]
    public async Task SaveRemoteDraftAsync_ConflictMarksDraftAndKeepsLocalCopy()
    {
        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var account = new AccountDbo
        {
            AccountId = Guid.NewGuid().ToString(),
            Name = "Compose",
            Type = AccountType.Jmap.ToString(),
            Capabilities = (int)AccountCapability.Mail
        };
        await storage.CreateAccountAsync(account);

        var service = new MailComposeService(
            storage,
            new MailComposeAttachmentService(Path.Combine(Path.GetTempPath(), "perinma-compose-tests", Guid.NewGuid().ToString("N"))),
            new MailComposerService(),
            new Dictionary<AccountType, IMailComposeProvider>
            {
                [AccountType.Jmap] = new ConflictComposeProvider()
            },
            new Dictionary<AccountType, IMailProvider>());

        var draft = new MailComposeDraft
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.Parse(account.AccountId),
            Kind = MailComposeKind.New,
            SelectedIdentityId = "sender",
            SelectedIdentityAddress = "sender@example.com",
            Subject = "Conflict",
            ToText = "to@example.com",
            HtmlBody = "<p>Hello</p>",
            PlainTextBody = "Hello",
            RemoteDraftReferenceJson = "{\"providerDraftId\":\"draft-1\"}"
        };

        await service.SaveLocalDraftAsync(draft);
        var exception = Assert.ThrowsAsync<MailComposeConflictException>(() => service.SaveRemoteDraftAsync(draft));
        var persistedDraft = await service.GetDraftAsync(draft.Id.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Message, Does.Contain("Remote draft changed"));
            Assert.That(persistedDraft, Is.Not.Null);
            Assert.That(persistedDraft!.Status, Is.EqualTo(MailComposeDraftStatus.Conflict));
        });
    }

    private sealed class ConflictComposeProvider : IMailComposeProvider
    {
        public Task<MailComposeCapabilities> GetComposeCapabilitiesAsync(string accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(new MailComposeCapabilities
            {
                SupportsDrafts = true,
                SupportsRemoteDrafts = true,
                SupportsSend = true,
                SupportsSenderIdentities = true,
                SupportsInlineAttachments = true
            });

        public Task<IReadOnlyList<MailIdentity>> GetSenderIdentitiesAsync(string accountId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MailIdentity>>([
                new MailIdentity { Id = "sender", Address = "sender@example.com", DisplayName = "Sender", IsPrimary = true }
            ]);

        public Task<ProviderDraftReference> SaveDraftAsync(string accountId, ProviderComposedMessage message, ProviderDraftReference? existingDraft = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("JMAP update failed with error 'stateMismatch'.");

        public Task DeleteDraftAsync(string accountId, ProviderDraftReference draft, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ProviderSendResult> SendAsync(string accountId, ProviderComposedMessage message, ProviderDraftReference? existingDraft = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ProviderSendResult());
    }
}
