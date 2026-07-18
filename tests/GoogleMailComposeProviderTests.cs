using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CredentialStore;
using perinma.Models;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage.Models;

namespace tests;

[TestFixture]
public class GoogleMailComposeProviderTests
{
    [Test]
    public async Task GetComposeCapabilitiesAndSenderIdentitiesAsync_ReturnsGmailComposeSupportAndMappedAliases()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/gmail/v1/users/me/settings/sendAs")
            {
                return JsonResponse(
                    new
                    {
                        sendAs = new object[]
                        {
                            new
                            {
                                sendAsEmail = "primary@example.com",
                                displayName = "Primary Sender",
                                isPrimary = true,
                                isDefault = true,
                                verificationStatus = "accepted"
                            },
                            new
                            {
                                sendAsEmail = "pending@example.com",
                                displayName = "Pending Alias",
                                isPrimary = false,
                                isDefault = false,
                                verificationStatus = "pending"
                            }
                        }
                    });
            }

            throw new AssertionException($"Unexpected request {request.Method} {request.RequestUri}");
        });

        var provider = CreateProvider(handler, out var accountId);

        var capabilities = await provider.GetComposeCapabilitiesAsync(accountId);
        var identities = await provider.GetSenderIdentitiesAsync(accountId);

        Assert.Multiple(() =>
        {
            Assert.That(capabilities.SupportsDrafts, Is.True);
            Assert.That(capabilities.SupportsRemoteDrafts, Is.True);
            Assert.That(capabilities.SupportsSend, Is.True);
            Assert.That(capabilities.SupportsSenderIdentities, Is.True);
            Assert.That(capabilities.SupportsInlineAttachments, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(identities, Has.Count.EqualTo(2));
        });

        var primary = identities[0];
        var pending = identities[1];

        Assert.Multiple(() =>
        {
            Assert.That(primary.Id, Is.EqualTo("primary@example.com"));
            Assert.That(primary.Address, Is.EqualTo("primary@example.com"));
            Assert.That(primary.DisplayName, Is.EqualTo("Primary Sender"));
            Assert.That(primary.IsPrimary, Is.True);
            Assert.That(primary.CanSend, Is.True);
            Assert.That(pending.Id, Is.EqualTo("pending@example.com"));
            Assert.That(pending.CanSend, Is.False);
        });
    }

    [Test]
    public async Task SaveDraftUpdateDraftAndDeleteDraftAsync_UseDraftEndpointsAndBuildMultipartMime()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "perinma-google-compose-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var textAttachmentPath = Path.Combine(rootDirectory, "notes.txt");
            var inlineAttachmentPath = Path.Combine(rootDirectory, "inline.png");
            await File.WriteAllTextAsync(textAttachmentPath, "draft attachment");
            await File.WriteAllBytesAsync(inlineAttachmentPath, [1, 2, 3, 4, 5, 6]);

            var handler = new RecordingHttpMessageHandler((request, _) =>
            {
                var path = request.RequestUri?.AbsolutePath;
                if (request.Method == HttpMethod.Post && path == "/gmail/v1/users/me/drafts")
                {
                    return JsonResponse(new { id = "draft-1", message = new { id = "draft-message-1", threadId = "thread-123", historyId = "10" } });
                }

                if (request.Method == HttpMethod.Put && path == "/gmail/v1/users/me/drafts/draft-1")
                {
                    return JsonResponse(new { id = "draft-1", message = new { id = "draft-message-2", threadId = "thread-123", historyId = "11" } });
                }

                if (request.Method == HttpMethod.Delete && path == "/gmail/v1/users/me/drafts/draft-1")
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                throw new AssertionException($"Unexpected request {request.Method} {request.RequestUri}");
            });

            var provider = CreateProvider(handler, out var accountId);
            var message = CreateMessage(textAttachmentPath, inlineAttachmentPath, threadExternalId: "thread-123");

            var createdDraft = await provider.SaveDraftAsync(accountId, message);
            var updatedDraft = await provider.SaveDraftAsync(accountId, message, createdDraft);
            await provider.DeleteDraftAsync(accountId, updatedDraft);

            Assert.Multiple(() =>
            {
                Assert.That(createdDraft.ProviderDraftId, Is.EqualTo("draft-1"));
                Assert.That(createdDraft.MessageExternalId, Is.EqualTo("draft-message-1"));
                Assert.That(createdDraft.ThreadExternalId, Is.EqualTo("thread-123"));
                Assert.That(createdDraft.IdentityId, Is.EqualTo("sender@example.com"));
                Assert.That(updatedDraft.MessageExternalId, Is.EqualTo("draft-message-2"));
                Assert.That(handler.Requests, Has.Count.EqualTo(3));
                Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(handler.Requests[0].Path, Is.EqualTo("/gmail/v1/users/me/drafts"));
                Assert.That(handler.Requests[1].Method, Is.EqualTo(HttpMethod.Put));
                Assert.That(handler.Requests[1].Path, Is.EqualTo("/gmail/v1/users/me/drafts/draft-1"));
                Assert.That(handler.Requests[2].Method, Is.EqualTo(HttpMethod.Delete));
                Assert.That(handler.Requests[2].Path, Is.EqualTo("/gmail/v1/users/me/drafts/draft-1"));
            });

            using var createPayload = JsonDocument.Parse(handler.Requests[0].Body!);
            Assert.That(createPayload.RootElement.TryGetProperty("id", out _), Is.False);
            var createMessagePayload = createPayload.RootElement.GetProperty("message");
            Assert.That(createMessagePayload.GetProperty("threadId").GetString(), Is.EqualTo("thread-123"));

            var rawMime = DecodeBase64UrlToString(createMessagePayload.GetProperty("raw").GetString()!);
            Assert.Multiple(() =>
            {
                Assert.That(rawMime, Does.Contain("In-Reply-To: <reply@example.com>"));
                Assert.That(rawMime, Does.Contain("References: <root@example.com> <reply@example.com>"));
                Assert.That(rawMime, Does.Contain("Content-Type: multipart/mixed;"));
                Assert.That(rawMime, Does.Contain("Content-Type: multipart/related;"));
                Assert.That(rawMime, Does.Contain("Content-ID: <inline-image@example.com>"));
                Assert.That(rawMime, Does.Contain("filename=\"notes.txt\""));
                Assert.That(rawMime, Does.Contain("filename=\"inline.png\""));
                Assert.That(rawMime, Does.Contain("cid:inline-image@example.com"));
            });
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
                Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Test]
    public async Task SendAsync_WithoutExistingDraft_UsesMessagesSendEndpoint()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/gmail/v1/users/me/messages/send")
            {
                return JsonResponse(new { id = "sent-1", threadId = "thread-999", historyId = "77" });
            }

            throw new AssertionException($"Unexpected request {request.Method} {request.RequestUri}");
        });

        var provider = CreateProvider(handler, out var accountId);
        var message = CreateMessage();

        var result = await provider.SendAsync(accountId, message);

        Assert.Multiple(() =>
        {
            Assert.That(result.SentMessageExternalId, Is.EqualTo("sent-1"));
            Assert.That(result.SentThreadExternalId, Is.EqualTo("thread-999"));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(handler.Requests[0].Path, Is.EqualTo("/gmail/v1/users/me/messages/send"));
        });

        using var payload = JsonDocument.Parse(handler.Requests[0].Body!);
        var messagePayload = payload.RootElement;
        var rawMime = DecodeBase64UrlToString(messagePayload.GetProperty("raw").GetString()!);
        Assert.Multiple(() =>
        {
            Assert.That(messagePayload.GetProperty("threadId").GetString(), Is.EqualTo("thread-999"));
            Assert.That(rawMime, Does.Contain("Subject: Compose test"));
            Assert.That(rawMime, Does.Contain("Content-Type: multipart/alternative;"));
        });
    }

    [Test]
    public async Task SendAsync_WithExistingDraft_UsesDraftSendEndpointAndIncludesDraftId()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/gmail/v1/users/me/drafts/send")
            {
                return JsonResponse(new { id = "sent-2", threadId = "thread-123", historyId = "78" });
            }

            throw new AssertionException($"Unexpected request {request.Method} {request.RequestUri}");
        });

        var provider = CreateProvider(handler, out var accountId);
        var message = CreateMessage(threadExternalId: null);
        var existingDraft = new ProviderDraftReference
        {
            ProviderDraftId = "draft-1",
            ThreadExternalId = "thread-123",
            RawDataJson = JsonSerializer.Serialize(new { draftId = "draft-1", threadId = "thread-123" })
        };

        var result = await provider.SendAsync(accountId, message, existingDraft);

        Assert.Multiple(() =>
        {
            Assert.That(result.SentMessageExternalId, Is.EqualTo("sent-2"));
            Assert.That(result.SentThreadExternalId, Is.EqualTo("thread-123"));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(handler.Requests[0].Path, Is.EqualTo("/gmail/v1/users/me/drafts/send"));
        });

        using var payload = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.That(payload.RootElement.GetProperty("id").GetString(), Is.EqualTo("draft-1"));
        var messagePayload = payload.RootElement.GetProperty("message");
        Assert.That(messagePayload.GetProperty("threadId").GetString(), Is.EqualTo("thread-123"));
    }

    private static GoogleMailProvider CreateProvider(RecordingHttpMessageHandler handler, out string accountId)
    {
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        accountId = Guid.NewGuid().ToString();
        credentialManager.StoreGoogleCredentials(
            accountId,
            new GoogleCredentials
            {
                Type = "Google",
                AccessToken = "test-token",
                RefreshToken = "test-refresh-token",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                TokenType = "Bearer"
            });

        var service = new GoogleMailService(() => new HttpClient(handler, disposeHandler: false));
        return new GoogleMailProvider(service, credentialManager);
    }

    private static ProviderComposedMessage CreateMessage(
        string? textAttachmentPath = null,
        string? inlineAttachmentPath = null,
        string? threadExternalId = "thread-999")
    {
        var attachments = new List<ProviderComposeAttachment>();
        if (!string.IsNullOrWhiteSpace(textAttachmentPath))
        {
            attachments.Add(
                new ProviderComposeAttachment
                {
                    AttachmentId = "notes-1",
                    FileName = "notes.txt",
                    MimeType = "text/plain",
                    ContentPath = textAttachmentPath,
                    Size = new FileInfo(textAttachmentPath).Length,
                    IsInline = false
                });
        }

        if (!string.IsNullOrWhiteSpace(inlineAttachmentPath))
        {
            attachments.Add(
                new ProviderComposeAttachment
                {
                    AttachmentId = "inline-1",
                    FileName = "inline.png",
                    MimeType = "image/png",
                    ContentPath = inlineAttachmentPath,
                    Size = new FileInfo(inlineAttachmentPath).Length,
                    IsInline = true,
                    ContentId = "inline-image@example.com"
                });
        }

        return new ProviderComposedMessage
        {
            Kind = MailComposeKind.Reply,
            SenderIdentity = new MailIdentity
            {
                Id = "sender@example.com",
                Address = "sender@example.com",
                DisplayName = "Sender Name",
                IsPrimary = true,
                CanSend = true
            },
            To =
            [
                new MailAddress
                {
                    Name = "Recipient",
                    Address = "recipient@example.com"
                }
            ],
            Cc = [],
            Bcc = [],
            Subject = "Compose test",
            PlainTextBody = "Plain body",
            HtmlBody = "<p>HTML body <img src=\"cid:inline-image@example.com\" /></p>",
            InReplyTo = "<reply@example.com>",
            References = ["<root@example.com>", "<reply@example.com>"],
            ThreadExternalId = threadExternalId,
            SourceMessageExternalId = "source-1",
            Attachments = attachments
        };
    }

    private static HttpResponseMessage JsonResponse(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

    private static string DecodeBase64UrlToString(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var remainder = normalized.Length % 4;
        if (remainder != 0)
            normalized = normalized.PadRight(normalized.Length + (4 - remainder), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri?.AbsolutePath ?? string.Empty, body));
            return responder(request, body);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Body);
}
