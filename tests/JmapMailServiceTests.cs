using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

using System.Threading;
using System.Threading.Tasks;
using perinma.Models;
using NUnit.Framework;
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

        var service = new JmapMailService(new HttpClient(new StubHttpMessageHandler(sessionJson)));

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

        var service = new JmapMailService(new HttpClient(new StubHttpMessageHandler(sessionJson)));

        var session = await service.DiscoverSessionAsync(CreateCredentials());

        Assert.That(session.AccountId, Is.EqualTo("team-mail"));
    }

    [Test]
    public async Task DiscoverSessionAsync_FollowsSessionRedirect_AndPreservesAuthentication()
    {
        var authenticatedSessionJson = """
            {
              "apiUrl": "https://mail.example.com/jmap/api/",
              "capabilities": {
                "urn:ietf:params:jmap:mail": {}
              },
              "accounts": {
                "dh": {
                  "name": "test@example.com",
                  "isPersonal": true,
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
              },
              "primaryAccounts": {
                "urn:ietf:params:jmap:mail": "dh"
              },
              "username": "test@example.com"
            }
            """;
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
    public async Task GetMessageAsync_RequestsBodyValues_AndParsesFullBodies()
    {
        var sessionJson = """
            {
              "apiUrl": "https://mail.example.com/jmap/api/",
              "capabilities": {
                "urn:ietf:params:jmap:mail": {}
              },
              "accounts": {
                "mail": {
                  "name": "test@example.com",
                  "isPersonal": true,
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
              },
              "primaryAccounts": {
                "urn:ietf:params:jmap:mail": "mail"
              }
            }
            """;
        var messageResponseJson = """
            {
              "methodResponses": [
                [
                  "Email/get",
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
                  },
                  "c0"
                ]
              ]
            }
            """;
        var handler = new EmailGetStubHttpMessageHandler(sessionJson, messageResponseJson);
        var service = new JmapMailService(new HttpClient(handler));

        var message = await service.GetMessageAsync(CreateCredentials(), "message-1", fetchBodies: true);

        Assert.Multiple(() =>
        {
            Assert.That(handler.EmailGetRequestedBodyValues, Is.True);
            Assert.That(message.PlainTextBody, Is.EqualTo("Full plain text body"));
            Assert.That(message.HtmlBody, Does.Contain("Full html body"));
            Assert.That(message.HasPlainTextBody, Is.True);
            Assert.That(message.HasHtmlBody, Is.True);
        });
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

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
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
    private sealed class EmailGetStubHttpMessageHandler(string sessionResponseBody, string emailGetResponseBody)
        : HttpMessageHandler
    {
        public bool EmailGetRequestedBodyValues { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sessionResponseBody, Encoding.UTF8, "application/json")
                };
            }

            var requestBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(requestBody);
            var methodCall = document.RootElement.GetProperty("methodCalls")[0];
            var emailGetArguments = methodCall[1];
            EmailGetRequestedBodyValues = emailGetArguments.GetProperty("properties")
                .EnumerateArray()
                .Any(property => string.Equals(property.GetString(), "bodyValues", StringComparison.Ordinal));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(emailGetResponseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
