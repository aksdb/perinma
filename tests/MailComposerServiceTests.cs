using System;
using System.IO;
using NUnit.Framework;
using perinma.Models;
using perinma.Services;

namespace tests;

[TestFixture]
public class MailComposerServiceTests
{
    [Test]
    public void CreateDraft_ReplyAll_ExcludesKnownIdentities()
    {
        var service = new MailComposerService();
        var identities = new[]
        {
            new MailIdentity { Id = "me", Address = "me@example.com", DisplayName = "Me", IsPrimary = true }
        };
        var source = new MailComposeSourceMessage
        {
            AccountId = Guid.NewGuid(),
            AccountType = AccountType.Google,
            Subject = "Project Update",
            Sender = new MailAddress { Name = "Alice", Address = "alice@example.com" },
            To = [new MailAddress { Name = "Me", Address = "me@example.com" }, new MailAddress { Name = "Bob", Address = "bob@example.com" }],
            Cc = [new MailAddress { Name = "Carol", Address = "carol@example.com" }],
            ReplyTo = [new MailAddress { Name = "Reply", Address = "reply@example.com" }],
            PlainTextBody = "Hello"
        };

        var draft = service.CreateDraft(source.AccountId, source.AccountType, MailComposeKind.ReplyAll, identities, source);

        Assert.Multiple(() =>
        {
            Assert.That(draft.Subject, Is.EqualTo("Re: Project Update"));
            Assert.That(draft.ToText, Is.EqualTo("Reply <reply@example.com>"));
            Assert.That(draft.CcText, Does.Contain("bob@example.com"));
            Assert.That(draft.CcText, Does.Contain("carol@example.com"));
            Assert.That(draft.CcText, Does.Not.Contain("me@example.com"));
        });
    }

    [Test]
    public void BuildProviderMessage_RewritesInlineFileSourcesToCid()
    {
        var service = new MailComposerService();
        var imagePath = Path.Combine(Path.GetTempPath(), $"compose-inline-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(imagePath, [1, 2, 3, 4]);

        try
        {
            var draft = new MailComposeDraft
            {
                Id = Guid.NewGuid(),
                AccountId = Guid.NewGuid(),
                Kind = MailComposeKind.Reply,
                SelectedIdentityId = "sender",
                SelectedIdentityDisplayName = "Sender",
                SelectedIdentityAddress = "sender@example.com",
                Subject = "Hello",
                ToText = "to@example.com",
                HtmlBody = $"<p>Hello</p><img src=\"{new Uri(imagePath).AbsoluteUri}\">",
                PlainTextBody = "Hello",
                SourceInternetMessageId = "<reply@example.com>",
                SourceMessageExternalId = "message-1",
                SourceThreadExternalId = "thread-1",
                Attachments =
                [
                    new MailComposeAttachment
                    {
                        Id = Guid.NewGuid(),
                        FileName = "inline.png",
                        MimeType = "image/png",
                        Size = 4,
                        IsInline = true,
                        ContentId = "inline-image",
                        ContentPath = imagePath,
                        SortOrder = 0
                    }
                ]
            };

            var result = service.BuildProviderMessage(draft);

            Assert.Multiple(() =>
            {
                Assert.That(result.HtmlBody, Does.Contain("cid:inline-image"));
                Assert.That(result.Attachments, Has.Count.EqualTo(1));
                Assert.That(result.Attachments[0].IsInline, Is.True);
                Assert.That(result.InReplyTo, Is.EqualTo("<reply@example.com>"));
                Assert.That(result.ThreadExternalId, Is.EqualTo("thread-1"));
            });
        }
        finally
        {
            if (File.Exists(imagePath))
                File.Delete(imagePath);
        }
    }
}
