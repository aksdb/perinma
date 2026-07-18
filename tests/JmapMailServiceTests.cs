using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CredentialStore;
using NUnit.Framework;
using perinma.Models;
using perinma.Services;
using perinma.Services.Jmap;
using perinma.Storage.Models;

namespace tests;

[TestFixture]
public class JmapMailServiceTests
{
    [Test]
public async Task DiscoverSessionAsync_FallsBackToPersonalWritableAccount_WhenAccountCapabilitiesAreMissing()
{
    var sessionJson = """
        {
          "apiUrl": "https://mail.example.com/jmap/api/",
          "capabilities": {
            "urn:ietf:params:jmap:mail": {}
          },
          "accounts": {
            "shared": {
              "name": "shared@example.com",
              "isPersonal": false,
              "isReadOnly": true
            },
            "personal": {
              "name": "me@example.com",
              "isPersonal": true,
              "isReadOnly": false
            }
          }
        }
        """;

    var service = new JmapMailService(new HttpClient(new StaticSessionHttpMessageHandler(sessionJson)));

    var session = await service.DiscoverSessionAsync(CreateCredentials());

    Assert.That(session.AccountId, Is.EqualTo("personal"));
}

    [Test]
    public async Task DiscoverSessionAsync_PrefersExplicitMailCapableAccount_OverPersonalFallback()
    {
        var sessionJson = """
            {
              "apiUrl": "https://mail.example.com/jmap/api/",
              "capabilities": {
                "urn:ietf:params:jmap:mail": {}
              },
              "accounts": {
                "personal": {
                  "name": "me@example.com",
                  "isPersonal": true,
                  "isReadOnly": false,
                  "accountCapabilities": {
                    "urn:ietf:params:jmap:contacts": {}
                  }
                },
                "team-mail": {
                  "name": "team@example.com",
                  "isPersonal": false,
                  "isReadOnly": false,
                  "accountCapabilities": {
                    "urn:ietf:params:jmap:mail": {
                      "maxMailboxesPerEmail": null,
                      "maxMailboxDepth": 10,
                      "maxSizeMailboxName": 255,
                      "maxSizeAttachmentsPerEmail": 10485760,
                      "emailQuerySortOptions": ["receivedAt"]
                    }
                  }
                }
              }
            }
            """;

        var service = new JmapMailService(new HttpClient(new StaticSessionHttpMessageHandler(sessionJson)));

        var session = await service.DiscoverSessionAsync(CreateCredentials());

        Assert.That(session.AccountId, Is.EqualTo("team-mail"));
    }

    [Test]
    public async Task DiscoverSessionAsync_FollowsSessionRedirect_AndPreservesAuthentication()
    {
        var authenticatedSessionJson = CreateSessionJson(includeSubmission: false, primaryAccountId: "dh", accountId: "dh", username: "test@example.com");
        var unauthenticatedSessionJson = """
            {
              "apiUrl": "https://mail.example.com/jmap/api/",
              "capabilities": {
                "urn:ietf:params:jmap:mail": {}
              },
              "accounts": {},
              "primaryAccounts": {},
              "username": ""
            }
            """;
        var handler = new RedirectingStubHttpMessageHandler(authenticatedSessionJson, unauthenticatedSessionJson);
        var service = new JmapMailService(new HttpClient(handler));

        var session = await service.DiscoverSessionAsync(CreateCredentials());

        Assert.Multiple(() =>
        {
            Assert.That(session.AccountId, Is.EqualTo("dh"));
            Assert.That(handler.RequestCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task DiscoverSessionAsync_TracksSubmissionCapabilityAndUploadUrl()
    {
        var service = new JmapMailService(new HttpClient(new StaticSessionHttpMessageHandler(CreateSessionJson())));

        var session = await service.DiscoverSessionAsync(CreateCredentials());

        Assert.Multiple(() =>
        {
            Assert.That(session.SupportsSubmission, Is.True);
            Assert.That(session.UploadUrlTemplate, Is.EqualTo("https://mail.example.com/jmap/upload/{accountId}/"));
            Assert.That(session.Capabilities, Does.Contain("urn:ietf:params:jmap:submission"));
        });
    }

    [Test]
    public async Task DiscoverSessionAsync_DoesNotEnableSubmission_WhenAccountLacksSubmissionCapability()
    {
        var sessionJson = CreateSessionJson(accountCapabilities: """
            {
              "urn:ietf:params:jmap:mail": {
                "maxMailboxesPerEmail": null,
                "maxMailboxDepth": 10,
                "maxSizeMailboxName": 255,
                "maxSizeAttachmentsPerEmail": 10485760,
                "emailQuerySortOptions": ["receivedAt"]
              }
            }
            """);
        var service = new JmapMailService(new HttpClient(new StaticSessionHttpMessageHandler(sessionJson)));

        var session = await service.DiscoverSessionAsync(CreateCredentials());

        Assert.Multiple(() =>
        {
            Assert.That(session.SupportsSubmission, Is.False);
            Assert.That(session.Capabilities, Does.Not.Contain("urn:ietf:params:jmap:submission"));
        });
    }

    [Test]
    public async Task GetMessageAsync_RequestsBodyValues_AndParsesFullBodies()
    {
        var messageResponseJson = CreateMethodResponse(
            "Email/get",
            """
            {
              "list": [
                {
                  "id": "message-1",
                  "threadId": "thread-1",
                  "mailboxIds": { "mailbox-1": true },
                  "keywords": {},
                  "from": [ { "email": "sender@example.com", "name": "Sender" } ],
                  "subject": "Hydrated",
                  "preview": "Preview",
                  "textBody": [ { "partId": "text-1" } ],
                  "htmlBody": [ { "partId": "html-1" } ],
                  "bodyStructure": {
                    "type": "multipart/alternative",
                    "subParts": [
                      { "partId": "text-1", "type": "text/plain" },
                      { "partId": "html-1", "type": "text/html" }
                    ]
                  },
                  "bodyValues": {
                    "text-1": { "value": "Full plain text body" },
                    "html-1": { "value": "<html><body><p>Full html body</p></body></html>" }
                  },
                  "hasAttachment": false
                }
              ],
              "notFound": []
            }
            """);
        var handler = new MethodDispatchHttpMessageHandler(
            CreateSessionJson(includeSubmission: false),
            new Dictionary<string, IEnumerable<string>>
            {
                ["Email/get"] = [messageResponseJson]
            });
        var service = new JmapMailService(new HttpClient(handler));

        var message = await service.GetMessageAsync(CreateCredentials(), "message-1", fetchBodies: true);

        Assert.Multiple(() =>
        {
            var request = ParseMethodRequest(handler.RequestBodiesByMethod["Email/get"].Single());
            Assert.That(request.GetProperty("properties").EnumerateArray().Select(property => property.GetString()), Does.Contain("bodyValues"));
            Assert.That(message.PlainTextBody, Is.EqualTo("Full plain text body"));
            Assert.That(message.HtmlBody, Does.Contain("Full html body"));
            Assert.That(message.HasPlainTextBody, Is.True);
            Assert.That(message.HasHtmlBody, Is.True);
        });
    }

    [Test]
    public async Task JmapMailProvider_ReturnsComposeCapabilitiesAndSenderIdentities_WhenSubmissionIsAvailable()
    {
        var handler = new MethodDispatchHttpMessageHandler(
            CreateSessionJson(),
            new Dictionary<string, IEnumerable<string>>
            {
                ["Mailbox/get"] =
                [
                    CreateMethodResponse(
                        "Mailbox/get",
                        """
                        {
                          "list": [
                            { "id": "drafts-1", "role": "drafts" },
                            { "id": "sent-1", "role": "sent" }
                          ],
                          "notFound": []
                        }
                        """)
                ],
                ["Identity/get"] =
                [
                    CreateMethodResponse(
                        "Identity/get",
                        """
                        {
                          "list": [
                            { "id": "identity-1", "name": "Sender", "email": "sender@example.com" },
                            { "id": "identity-2", "name": "Alt", "email": "alt@example.com" }
                          ],
                          "notFound": []
                        }
                        """)
                ]
            });
        var credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        credentialManager.StoreJmapCredentials("account-1", CreateCredentials());
        var provider = new JmapMailProvider(new JmapMailService(new HttpClient(handler)), credentialManager);

        var capabilities = await provider.GetComposeCapabilitiesAsync("account-1");
        var identities = await provider.GetSenderIdentitiesAsync("account-1");

        Assert.Multiple(() =>
        {
            Assert.That(capabilities.SupportsDrafts, Is.True);
            Assert.That(capabilities.SupportsRemoteDrafts, Is.True);
            Assert.That(capabilities.SupportsSend, Is.True);
            Assert.That(capabilities.SupportsSenderIdentities, Is.True);
            Assert.That(capabilities.SupportsInlineAttachments, Is.True);
            Assert.That(identities.Select(identity => identity.Id), Is.EqualTo(new[] { "identity-1", "identity-2" }));
            Assert.That(identities.Single(identity => identity.Id == "identity-1").IsPrimary, Is.True);
        });
    }

    [Test]
    public async Task SaveDraftAsync_UploadsAttachments_ReusesBlobReferences_AndBuildsReplyDraftPayload()
    {
        var tempPath = CreateTempFile(new byte[] { 1, 2, 3, 4 });
        try
        {
            var handler = new MethodDispatchHttpMessageHandler(
                CreateSessionJson(),
                new Dictionary<string, IEnumerable<string>>
                {
                    ["Mailbox/get"] =
                    [
                        CreateMethodResponse(
                            "Mailbox/get",
                            """
                            {
                              "list": [
                                { "id": "drafts-1", "role": "drafts" },
                                { "id": "sent-1", "role": "sent" }
                              ],
                              "notFound": []
                            }
                            """)
                    ],
                    ["Email/set"] =
                    [
                        CreateMethodResponse(
                            "Email/set",
                            """
                            {
                              "created": {
                                "draft": {
                                  "id": "draft-1",
                                  "threadId": "thread-1",
                                  "blobId": "message-blob-1",
                                  "size": 1234
                                }
                              },
                              "notCreated": null,
                              "oldState": "email-state-0",
                              "newState": "email-state-1"
                            }
                            """)
                    ]
                },
                uploadResponses:
                [
                    """
                    {
                      "accountId": "mail",
                      "blobId": "uploaded-blob-1",
                      "type": "text/plain",
                      "size": 4
                    }
                    """
                ]);
            var service = new JmapMailService(new HttpClient(handler));

            var draft = await service.SaveDraftAsync(
                CreateCredentials(),
                CreateComposedMessage(
                    tempPath,
                    """
                    {
                      "accountId": "mail",
                      "blobId": "existing-blob-2",
                      "type": "image/png",
                      "size": 99
                    }
                    """));

            Assert.Multiple(() =>
            {
                Assert.That(handler.UploadedBodies, Has.Count.EqualTo(1));
                Assert.That(handler.UploadedBodies[0], Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
                Assert.That(handler.UploadedContentTypes[0], Is.EqualTo("text/plain"));
                Assert.That(draft.ProviderDraftId, Is.EqualTo("draft-1"));
                Assert.That(draft.ThreadExternalId, Is.EqualTo("thread-1"));
                Assert.That(draft.MailboxExternalId, Is.EqualTo("drafts-1"));
                Assert.That(draft.IdentityId, Is.EqualTo("identity-1"));
                Assert.That(draft.StateToken, Is.EqualTo("email-state-1"));
            });

            using var requestDocument = JsonDocument.Parse(handler.RequestBodiesByMethod["Email/set"].Single());
            var requestRoot = requestDocument.RootElement;
            var emailSetRequest = requestRoot.GetProperty("methodCalls")[0][1].Clone();
            var create = emailSetRequest.GetProperty("create").GetProperty("draft");
            var attachments = create.GetProperty("attachments").EnumerateArray().ToList();
            Assert.Multiple(() =>
            {
                Assert.That(requestRoot.GetProperty("using").EnumerateArray().Select(item => item.GetString()), Does.Contain("urn:ietf:params:jmap:submission"));
                Assert.That(create.GetProperty("mailboxIds").GetProperty("drafts-1").GetBoolean(), Is.True);
                Assert.That(create.GetProperty("keywords").GetProperty("$draft").GetBoolean(), Is.True);
                Assert.That(create.GetProperty("from")[0].GetProperty("email").GetString(), Is.EqualTo("sender@example.com"));
                Assert.That(create.GetProperty("to")[0].GetProperty("email").GetString(), Is.EqualTo("to@example.com"));
                Assert.That(create.GetProperty("cc")[0].GetProperty("email").GetString(), Is.EqualTo("cc@example.com"));
                Assert.That(create.GetProperty("bcc")[0].GetProperty("email").GetString(), Is.EqualTo("bcc@example.com"));
                Assert.That(create.GetProperty("inReplyTo").EnumerateArray().Select(item => item.GetString()), Is.EqualTo(new[] { "<reply@example.com>" }));
                Assert.That(create.GetProperty("references").EnumerateArray().Select(item => item.GetString()), Is.EqualTo(new[] { "<ref-1@example.com>", "<ref-2@example.com>" }));
                Assert.That(create.GetProperty("bodyValues").GetProperty("textBody").GetProperty("value").GetString(), Is.EqualTo("Plain text body"));
                Assert.That(create.GetProperty("bodyValues").GetProperty("htmlBody").GetProperty("value").GetString(), Is.EqualTo("<p>Html body</p>"));
                Assert.That(attachments.Select(attachment => attachment.GetProperty("blobId").GetString()), Is.EqualTo(new[] { "uploaded-blob-1", "existing-blob-2" }));
                Assert.That(attachments[0].GetProperty("disposition").GetString(), Is.EqualTo("inline"));
                Assert.That(attachments[0].GetProperty("cid").GetString(), Is.EqualTo("inline-image"));
                Assert.That(attachments[1].GetProperty("disposition").GetString(), Is.EqualTo("attachment"));
            });
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    [Test]
    public async Task DeleteDraftAsync_SendsDestroyRequest()
    {
        var handler = new MethodDispatchHttpMessageHandler(
            CreateSessionJson(),
            new Dictionary<string, IEnumerable<string>>
            {
                ["Email/set"] =
                [
                    CreateMethodResponse(
                        "Email/set",
                        """
                        {
                          "destroyed": ["draft-9"],
                          "notDestroyed": null,
                          "oldState": "email-state-1",
                          "newState": "email-state-2"
                        }
                        """)
                ]
            });
        var service = new JmapMailService(new HttpClient(handler));

        await service.DeleteDraftAsync(
            CreateCredentials(),
            new ProviderDraftReference
            {
                ProviderDraftId = "draft-9",
                StateToken = "email-state-1"
            });

        var emailSetRequest = ParseMethodRequest(handler.RequestBodiesByMethod["Email/set"].Single());
        Assert.Multiple(() =>
        {
            Assert.That(emailSetRequest.GetProperty("destroy").EnumerateArray().Select(item => item.GetString()), Is.EqualTo(new[] { "draft-9" }));
            Assert.That(emailSetRequest.GetProperty("ifInState").GetString(), Is.EqualTo("email-state-1"));
        });
    }

    [Test]
    public async Task SendAsync_UpdatesExistingDraft_AndCreatesSubmissionWithDraftCleanup()
    {
        var handler = new MethodDispatchHttpMessageHandler(
            CreateSessionJson(),
            new Dictionary<string, IEnumerable<string>>
            {
                ["Mailbox/get"] =
                [
                    CreateMethodResponse(
                        "Mailbox/get",
                        """
                        {
                          "list": [
                            { "id": "drafts-1", "role": "drafts" },
                            { "id": "sent-1", "role": "sent" }
                          ],
                          "notFound": []
                        }
                        """),
                    CreateMethodResponse(
                        "Mailbox/get",
                        """
                        {
                          "list": [
                            { "id": "drafts-1", "role": "drafts" },
                            { "id": "sent-1", "role": "sent" }
                          ],
                          "notFound": []
                        }
                        """)
                ],
                ["Email/set"] =
                [
                    CreateMethodResponse(
                        "Email/set",
                        """
                        {
                          "updated": {
                            "draft-42": null
                          },
                          "notUpdated": null,
                          "oldState": "email-state-1",
                          "newState": "email-state-2"
                        }
                        """)
                ],
                ["EmailSubmission/set"] =
                [
                    CreateMethodResponse(
                        "EmailSubmission/set",
                        """
                        {
                          "created": {
                            "submission": {
                              "id": "submission-1"
                            }
                          },
                          "notCreated": null,
                          "oldState": "submission-state-1",
                          "newState": "submission-state-2"
                        }
                        """)
                ]
            });
        var service = new JmapMailService(new HttpClient(handler));

        var result = await service.SendAsync(
            CreateCredentials(),
            CreateComposedMessage(contentPath: null, reusedAttachmentReferenceJson: null, includeAttachments: false),
            new ProviderDraftReference
            {
                ProviderDraftId = "draft-42",
                MessageExternalId = "draft-42",
                ThreadExternalId = "thread-42",
                MailboxExternalId = "drafts-1",
                IdentityId = "identity-1",
                StateToken = "email-state-1"
            });

        var emailSetRequest = ParseMethodRequest(handler.RequestBodiesByMethod["Email/set"].Single());
        var submissionRequest = ParseMethodRequest(handler.RequestBodiesByMethod["EmailSubmission/set"].Single());
        Assert.Multiple(() =>
        {
            Assert.That(emailSetRequest.GetProperty("ifInState").GetString(), Is.EqualTo("email-state-1"));
            Assert.That(emailSetRequest.GetProperty("update").TryGetProperty("draft-42", out _), Is.True);
            Assert.That(submissionRequest.GetProperty("create").GetProperty("submission").GetProperty("identityId").GetString(), Is.EqualTo("identity-1"));
            Assert.That(submissionRequest.GetProperty("create").GetProperty("submission").GetProperty("emailId").GetString(), Is.EqualTo("draft-42"));
            Assert.That(submissionRequest.GetProperty("onSuccessUpdateEmail").GetProperty("#submission").GetProperty("keywords/$draft").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(submissionRequest.GetProperty("onSuccessUpdateEmail").GetProperty("#submission").GetProperty("mailboxIds/sent-1").GetBoolean(), Is.True);
            Assert.That(submissionRequest.GetProperty("onSuccessUpdateEmail").GetProperty("#submission").GetProperty("mailboxIds/drafts-1").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(result.SentMessageExternalId, Is.EqualTo("draft-42"));
            Assert.That(result.SentThreadExternalId, Is.EqualTo("thread-42"));
        });
    }

    private static ProviderComposedMessage CreateComposedMessage(
        string? contentPath,
        string? reusedAttachmentReferenceJson,
        bool includeAttachments = true)
    {
        var attachments = new List<ProviderComposeAttachment>();
        if (includeAttachments)
        {
            attachments.Add(new ProviderComposeAttachment
            {
                AttachmentId = "attachment-inline",
                FileName = "inline.txt",
                MimeType = "text/plain",
                ContentPath = contentPath ?? string.Empty,
                Size = 4,
                IsInline = true,
                ContentId = "<inline-image>",
                ProviderReferenceJson = null
            });
            attachments.Add(new ProviderComposeAttachment
            {
                AttachmentId = "attachment-existing",
                FileName = "existing.png",
                MimeType = "image/png",
                ContentPath = contentPath ?? string.Empty,
                Size = 99,
                IsInline = false,
                ContentId = null,
                ProviderReferenceJson = reusedAttachmentReferenceJson
            });
        }

        return new ProviderComposedMessage
        {
            Kind = MailComposeKind.ReplyAll,
            SenderIdentity = new MailIdentity
            {
                Id = "identity-1",
                DisplayName = "Sender",
                Address = "sender@example.com",
                IsPrimary = true,
                CanSend = true
            },
            To = [new MailAddress { Name = "To", Address = "to@example.com" }],
            Cc = [new MailAddress { Name = "Cc", Address = "cc@example.com" }],
            Bcc = [new MailAddress { Name = "Bcc", Address = "bcc@example.com" }],
            Subject = "Re: Subject",
            PlainTextBody = "Plain text body",
            HtmlBody = "<p>Html body</p>",
            InReplyTo = "<reply@example.com>",
            References = ["<ref-1@example.com>", "<ref-2@example.com>"],
            ThreadExternalId = "thread-0",
            SourceMessageExternalId = "source-1",
            Attachments = attachments
        };
    }

    private static JmapCredentials CreateCredentials()
    {
        return new JmapCredentials
        {
            Type = AccountType.Jmap.ToString(),
            SessionUrl = "https://mail.example.com/.well-known/jmap",
            BearerToken = "token"
        };
    }

    private static string CreateSessionJson(
        bool includeSubmission = true,
        string accountId = "mail",
        string primaryAccountId = "mail",
        string username = "sender@example.com",
        string? accountCapabilities = null)
    {
        var submissionCapability = includeSubmission ? ",\n                \"urn:ietf:params:jmap:submission\": {}" : string.Empty;
        var uploadUrl = includeSubmission ? "\n              \"uploadUrl\": \"https://mail.example.com/jmap/upload/{accountId}/\"," : string.Empty;
        var resolvedAccountCapabilities = accountCapabilities
            ?? (includeSubmission
                ? """
                  {
                    "urn:ietf:params:jmap:mail": {
                      "maxMailboxesPerEmail": null,
                      "maxMailboxDepth": 10,
                      "maxSizeMailboxName": 255,
                      "maxSizeAttachmentsPerEmail": 10485760,
                      "emailQuerySortOptions": ["receivedAt"]
                    },
                    "urn:ietf:params:jmap:submission": {
                      "maxDelayedSend": 0,
                      "submissionExtensions": {}
                    }
                  }
                  """
                : """
                  {
                    "urn:ietf:params:jmap:mail": {
                      "maxMailboxesPerEmail": null,
                      "maxMailboxDepth": 10,
                      "maxSizeMailboxName": 255,
                      "maxSizeAttachmentsPerEmail": 10485760,
                      "emailQuerySortOptions": ["receivedAt"]
                    }
                  }
                  """);

        return $$"""
            {
              "apiUrl": "https://mail.example.com/jmap/api/",
              "downloadUrl": "https://mail.example.com/jmap/download/{accountId}/{blobId}/{name}?type={type}",{{uploadUrl}}
              "capabilities": {
                "urn:ietf:params:jmap:core": {
                  "maxSizeUpload": 50000000,
                  "maxConcurrentUpload": 4,
                  "maxSizeRequest": 10000000,
                  "maxConcurrentRequests": 4,
                  "maxCallsInRequest": 16,
                  "maxObjectsInGet": 256,
                  "maxObjectsInSet": 128,
                  "collationAlgorithms": ["i;ascii-casemap"]
                },
                "urn:ietf:params:jmap:mail": {}{{submissionCapability}}
              },
              "accounts": {
                "shared": {
                  "name": "shared@example.com",
                  "isPersonal": false,
                  "isReadOnly": true
                },
                "{{accountId}}": {
                  "name": "{{username}}",
                  "isPersonal": true,
                  "isReadOnly": false,
                  "accountCapabilities": {{resolvedAccountCapabilities}}
                }
              },
              "primaryAccounts": {
                "urn:ietf:params:jmap:mail": "{{primaryAccountId}}"
              },
              "username": "{{username}}",
              "state": "session-state-1"
            }
            """;
    }

    private static string CreateMethodResponse(string methodName, string payload)
    {
        return $$"""
            {
              "methodResponses": [
                [
                  "{{methodName}}",
                  {{payload}},
                  "c0"
                ]
              ]
            }
            """;
    }

    private static JsonElement ParseMethodRequest(string requestBody)
    {
        using var document = JsonDocument.Parse(requestBody);
        return document.RootElement.GetProperty("methodCalls")[0][1].Clone();
    }

    private static string CreateTempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"jmap-compose-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private sealed class StaticSessionHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RedirectingStubHttpMessageHandler(string authenticatedResponseBody, string unauthenticatedResponseBody)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                redirectResponse.Headers.Location = new Uri("/jmap/session", UriKind.Relative);
                return Task.FromResult(redirectResponse);
            }

            var hasAuthorization = string.Equals(request.Headers.Authorization?.ToString(), "Bearer token", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    hasAuthorization ? authenticatedResponseBody : unauthenticatedResponseBody,
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed class MethodDispatchHttpMessageHandler(
        string sessionResponseBody,
        IDictionary<string, IEnumerable<string>> methodResponses,
        IEnumerable<string>? uploadResponses = null)
        : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<string>> _methodResponses = methodResponses.ToDictionary(
            pair => pair.Key,
            pair => new Queue<string>(pair.Value),
            StringComparer.Ordinal);
        private readonly Queue<string> _uploadResponses = new(uploadResponses ?? []);

        public Dictionary<string, List<string>> RequestBodiesByMethod { get; } = new(StringComparer.Ordinal);
        public List<byte[]> UploadedBodies { get; } = [];
        public List<string> UploadedContentTypes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sessionResponseBody, Encoding.UTF8, "application/json")
                };
            }

            if (request.RequestUri?.AbsoluteUri.Contains("/upload/", StringComparison.Ordinal) == true)
            {
                UploadedBodies.Add(request.Content == null
                    ? []
                    : await request.Content.ReadAsByteArrayAsync(cancellationToken));
                UploadedContentTypes.Add(request.Content?.Headers.ContentType?.MediaType ?? string.Empty);
                if (_uploadResponses.Count == 0)
                    throw new InvalidOperationException("No upload response configured.");

                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(_uploadResponses.Dequeue(), Encoding.UTF8, "application/json")
                };
            }

            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var methodName = document.RootElement.GetProperty("methodCalls")[0][0].GetString()
                ?? throw new InvalidOperationException("Missing JMAP method name.");
            if (!_methodResponses.TryGetValue(methodName, out var responses) || responses.Count == 0)
                throw new InvalidOperationException($"No response configured for method '{methodName}'.");

            if (!RequestBodiesByMethod.TryGetValue(methodName, out var requests))
            {
                requests = [];
                RequestBodiesByMethod[methodName] = requests;
            }

            requests.Add(body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }
}
