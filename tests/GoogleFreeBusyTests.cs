using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CredentialStore;
using Google.Apis.Calendar.v3.Data;
using NodaTime;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage.Models;
using tests.Fakes;

namespace tests;

[TestFixture]
public class GoogleFreeBusyTests
{
    private CredentialManagerService _credentialManager = null!;
    private GoogleCalendarServiceStub _serviceStub = null!;
    private GoogleCalendarProvider _provider = null!;
    private string _accountId = null!;

    // 2024-05-14 07:00–22:00 UTC
    private static readonly Interval QueryInterval = new(
        Instant.FromUtc(2024, 5, 14, 7, 0),
        Instant.FromUtc(2024, 5, 14, 22, 0));

    [SetUp]
    public void SetUp()
    {
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        _serviceStub = new GoogleCalendarServiceStub();
        _provider = new GoogleCalendarProvider(_serviceStub, _credentialManager);
        _accountId = Guid.NewGuid().ToString();

        _credentialManager.StoreGoogleCredentials(_accountId, new GoogleCredentials
        {
            Type = "Google",
            AccessToken = "test_token",
            RefreshToken = "test_refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        });
    }

    [Test]
    public async Task GetFreeBusyAsync_NoCredentials_ReturnsUnknownForAllEmails()
    {
        // Fresh account with no stored credentials
        var noCredAccount = Guid.NewGuid().ToString();
        var emails = new List<string> { "alice@example.com", "bob@example.com" };

        var results = await _provider.GetFreeBusyAsync(noCredAccount, emails, QueryInterval);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.Select(r => r.Status), Is.All.EqualTo(FreeBusyStatus.Unknown));
    }

    [Test]
    public async Task GetFreeBusyAsync_SingleAttendeeWithBusy_ReturnsMappedSlots()
    {
        var busyStart = DateTime.SpecifyKind(new DateTime(2024, 5, 14, 9, 0, 0), DateTimeKind.Utc);
        var busyEnd = DateTime.SpecifyKind(new DateTime(2024, 5, 14, 10, 0, 0), DateTimeKind.Utc);

        _serviceStub.SetFreeBusyResponse(new FreeBusyResponse
        {
            Calendars = new Dictionary<string, FreeBusyCalendar>
            {
                ["alice@example.com"] = new FreeBusyCalendar
                {
                    Busy = new List<TimePeriod>
                    {
                        new() { StartDateTimeOffset = busyStart, EndDateTimeOffset = busyEnd }
                    }
                }
            }
        });

        var results = await _provider.GetFreeBusyAsync(
            _accountId, new List<string> { "alice@example.com" }, QueryInterval);

        Assert.That(results, Has.Count.EqualTo(1));
        var fb = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(fb.Email, Is.EqualTo("alice@example.com"));
            Assert.That(fb.Status, Is.EqualTo(FreeBusyStatus.Ok));
            Assert.That(fb.BusySlots, Has.Count.EqualTo(1));
            Assert.That(fb.BusySlots[0].Start, Is.EqualTo(Instant.FromUtc(2024, 5, 14, 9, 0)));
            Assert.That(fb.BusySlots[0].End, Is.EqualTo(Instant.FromUtc(2024, 5, 14, 10, 0)));
        });
    }

    [Test]
    public async Task GetFreeBusyAsync_AttendeeWithErrors_ReturnsUnavailable()
    {
        _serviceStub.SetFreeBusyResponse(new FreeBusyResponse
        {
            Calendars = new Dictionary<string, FreeBusyCalendar>
            {
                ["alice@example.com"] = new FreeBusyCalendar
                {
                    Errors = new List<Error>
                    {
                        new() { Domain = "calendar", Reason = "notFound" }
                    }
                }
            }
        });

        var results = await _provider.GetFreeBusyAsync(
            _accountId, new List<string> { "alice@example.com" }, QueryInterval);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Status, Is.EqualTo(FreeBusyStatus.Unavailable));
    }

    [Test]
    public async Task GetFreeBusyAsync_AttendeeNotInResponse_ReturnsUnknown()
    {
        // Empty calendars dict — bob is not present
        _serviceStub.SetFreeBusyResponse(new FreeBusyResponse
        {
            Calendars = new Dictionary<string, FreeBusyCalendar>()
        });

        var results = await _provider.GetFreeBusyAsync(
            _accountId, new List<string> { "bob@example.com" }, QueryInterval);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Status, Is.EqualTo(FreeBusyStatus.Unknown));
    }

    [Test]
    public async Task GetFreeBusyAsync_MultipleBusyPeriods_AllMapped()
    {
        var t = (int h, int m) => DateTime.SpecifyKind(new DateTime(2024, 5, 14, h, m, 0), DateTimeKind.Utc);

        _serviceStub.SetFreeBusyResponse(new FreeBusyResponse
        {
            Calendars = new Dictionary<string, FreeBusyCalendar>
            {
                ["alice@example.com"] = new FreeBusyCalendar
                {
                    Busy = new List<TimePeriod>
                    {
                        new() { StartDateTimeOffset = t(9, 0), EndDateTimeOffset = t(10, 0) },
                        new() { StartDateTimeOffset = t(13, 0), EndDateTimeOffset = t(14, 30) }
                    }
                }
            }
        });

        var results = await _provider.GetFreeBusyAsync(
            _accountId, new List<string> { "alice@example.com" }, QueryInterval);

        var fb = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(fb.Status, Is.EqualTo(FreeBusyStatus.Ok));
            Assert.That(fb.BusySlots, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task GetFreeBusyAsync_BusySlotsOrderedByStart()
    {
        // Provide periods in reverse order; expect result ordered ascending
        var t = (int h) => DateTime.SpecifyKind(new DateTime(2024, 5, 14, h, 0, 0), DateTimeKind.Utc);

        _serviceStub.SetFreeBusyResponse(new FreeBusyResponse
        {
            Calendars = new Dictionary<string, FreeBusyCalendar>
            {
                ["alice@example.com"] = new FreeBusyCalendar
                {
                    Busy = new List<TimePeriod>
                    {
                        new() { StartDateTimeOffset = t(15), EndDateTimeOffset = t(16) },
                        new() { StartDateTimeOffset = t(9), EndDateTimeOffset = t(10) }
                    }
                }
            }
        });

        var results = await _provider.GetFreeBusyAsync(
            _accountId, new List<string> { "alice@example.com" }, QueryInterval);

        var slots = results[0].BusySlots;
        Assert.That(slots, Has.Count.EqualTo(2));
        Assert.That(slots[0].Start, Is.LessThan(slots[1].Start));
    }

    [Test]
    public async Task GetFreeBusyAsync_MultipleAttendees_EachMappedIndependently()
    {
        var t = (int h) => DateTime.SpecifyKind(new DateTime(2024, 5, 14, h, 0, 0), DateTimeKind.Utc);

        _serviceStub.SetFreeBusyResponse(new FreeBusyResponse
        {
            Calendars = new Dictionary<string, FreeBusyCalendar>
            {
                ["alice@example.com"] = new FreeBusyCalendar
                {
                    Busy = new List<TimePeriod> { new() { StartDateTimeOffset = t(9), EndDateTimeOffset = t(10) } }
                },
                ["bob@example.com"] = new FreeBusyCalendar
                {
                    Errors = new List<Error> { new() { Domain = "calendar", Reason = "notFound" } }
                }
            }
        });

        var results = await _provider.GetFreeBusyAsync(
            _accountId,
            new List<string> { "alice@example.com", "bob@example.com" },
            QueryInterval);

        Assert.That(results, Has.Count.EqualTo(2));

        var alice = results.Single(r => r.Email == "alice@example.com");
        var bob = results.Single(r => r.Email == "bob@example.com");

        Assert.Multiple(() =>
        {
            Assert.That(alice.Status, Is.EqualTo(FreeBusyStatus.Ok));
            Assert.That(alice.BusySlots, Has.Count.EqualTo(1));
            Assert.That(bob.Status, Is.EqualTo(FreeBusyStatus.Unavailable));
        });
    }
}
