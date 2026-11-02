using System.Web;
using Google.Apis.Calendar.v3;
using Google.Apis.PeopleService.v1;
using perinma.Models;
using perinma.Services.Google;

namespace tests;

public class GoogleOAuthServiceTests
{
    private static readonly string[] CalendarScopes =
    [
        CalendarService.Scope.Calendar,
        CalendarService.Scope.CalendarEvents,
    ];

    private static readonly string[] ContactScopes =
    [
        PeopleServiceService.Scope.Contacts,
        PeopleServiceService.Scope.DirectoryReadonly,
    ];

    private const string MailScope = "https://www.googleapis.com/auth/gmail.modify";

    private static readonly string[] AllScopes =
    [
        ..CalendarScopes,
        ..ContactScopes,
        MailScope,
    ];

    private static async Task<string[]> GetRequestedScopesAsync(AccountCapability capabilities)
    {
        using var cts = new CancellationTokenSource();
        var oauthService = new GoogleOAuthService(new GoogleCalendarService());
        var (oauthUrl, _) = await oauthService.StartAuthenticationAsync(capabilities, cts.Token);

        try
        {
            var query = HttpUtility.ParseQueryString(new Uri(oauthUrl).Query);
            Assert.That(query["scope"], Is.Not.Null, "OAuth URL must contain a scope parameter");
            return query["scope"]!.Split(' ');
        }
        finally
        {
            await cts.CancelAsync();
        }
    }

    [Test]
    public async Task StartAuthenticationAsync_WithCalendarCapability_RequestsOnlyCalendarScopes()
    {
        var scopes = await GetRequestedScopesAsync(AccountCapability.Calendar);

        Assert.That(scopes, Is.EquivalentTo(CalendarScopes));
    }

    [Test]
    public async Task StartAuthenticationAsync_WithContactsCapability_RequestsOnlyContactScopes()
    {
        var scopes = await GetRequestedScopesAsync(AccountCapability.Contacts);

        Assert.That(scopes, Is.EquivalentTo(ContactScopes));
    }

    [Test]
    public async Task StartAuthenticationAsync_WithMailCapability_RequestsOnlyMailScope()
    {
        var scopes = await GetRequestedScopesAsync(AccountCapability.Mail);

        Assert.That(scopes, Is.EquivalentTo([MailScope]));
    }

    [Test]
    public async Task StartAuthenticationAsync_WithCalendarAndMail_DoesNotRequestContactScopes()
    {
        var scopes = await GetRequestedScopesAsync(AccountCapability.Calendar | AccountCapability.Mail);

        Assert.That(scopes, Is.EquivalentTo([..CalendarScopes, MailScope]));
        Assert.That(scopes, Does.Not.Contain(PeopleServiceService.Scope.Contacts));
    }

    [Test]
    public async Task StartAuthenticationAsync_WithAllCapabilities_RequestsAllScopes()
    {
        var scopes = await GetRequestedScopesAsync(
            AccountCapability.Calendar | AccountCapability.Contacts | AccountCapability.Mail);

        Assert.That(scopes, Is.EquivalentTo(AllScopes));
    }

    [Test]
    public async Task StartAuthenticationAsync_WithNoCapabilities_FallsBackToAllScopes()
    {
        var scopes = await GetRequestedScopesAsync(AccountCapability.None);

        Assert.That(scopes, Is.EquivalentTo(AllScopes));
    }

    [Test]
    public async Task StartAuthenticationAsync_WithoutCapabilities_RequestsAllScopesForCompatibility()
    {
        using var cts = new CancellationTokenSource();
        var oauthService = new GoogleOAuthService(new GoogleCalendarService());
        var (oauthUrl, _) = await oauthService.StartAuthenticationAsync(cancellationToken: cts.Token);

        try
        {
            var query = HttpUtility.ParseQueryString(new Uri(oauthUrl).Query);
            Assert.That(query["scope"]!.Split(' '), Is.EquivalentTo(AllScopes));
        }
        finally
        {
            await cts.CancelAsync();
        }
    }
}
