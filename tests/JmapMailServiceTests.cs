using System.Net;
using System.Net.Http;
using System.Text;
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
}
