using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CredentialStore;
using NodaTime;
using perinma.Services;
using perinma.Services.CalDAV;
using perinma.Storage;
using perinma.Storage.Models;
using tests.Fakes;

namespace tests;

[TestFixture]
public class CalDavFreeBusyTests
{
    private CredentialManagerService _credentialManager = null!;
    private CalDavServiceStub _serviceStub = null!;
    private CalDavCalendarProvider _provider = null!;
    private DatabaseService _database = null!;
    private SqliteStorage _storage = null!;
    private string _accountId = null!;

    private static readonly Interval QueryInterval = new(
        Instant.FromUtc(2024, 5, 14, 7, 0),
        Instant.FromUtc(2024, 5, 14, 22, 0));

    [SetUp]
    public void SetUp()
    {
        _database = new DatabaseService(inMemory: true);
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        _storage = new SqliteStorage(_database, _credentialManager);
        _serviceStub = new CalDavServiceStub();
        _provider = new CalDavCalendarProvider(_serviceStub, _credentialManager, _storage);
        _accountId = Guid.NewGuid().ToString();
    }

    [TearDown]
    public void TearDown()
    {
        _storage?.Dispose();
        _database?.Dispose();
    }


    private void StoreCredentials(string accountId)
    {
        _credentialManager.StoreCalDavCredentials(accountId, new CalDavCredentials
        {
            Type = "CalDav",
            ServerUrl = "https://caldav.example.com",
            Username = "organizer@example.com",
            Password = "testpass"
        });
    }

    [Test]
    public async Task GetFreeBusyAsync_NoCredentials_ReturnsUnknownForAll()
    {
        // Do NOT store credentials — any account that was never set up
        var noCredAccount = Guid.NewGuid().ToString();
        var emails = new List<string> { "alice@example.com", "bob@example.com" };

        var results = await _provider.GetFreeBusyAsync(noCredAccount, emails, QueryInterval);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.Select(r => r.Status), Is.All.EqualTo(FreeBusyStatus.Unknown));
    }

    [Test]
    public async Task GetFreeBusyAsync_StubReturnsOk_MappedThrough()
    {
        StoreCredentials(_accountId);

        var expectedSlot = new TimeSlot(
            Instant.FromUtc(2024, 5, 14, 9, 0),
            Instant.FromUtc(2024, 5, 14, 10, 0));

        _serviceStub.SetFreeBusyResult(new List<AttendeeFreeBusy>
        {
            new()
            {
                Email = "alice@example.com",
                Status = FreeBusyStatus.Ok,
                BusySlots = new List<TimeSlot> { expectedSlot }
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
            Assert.That(fb.BusySlots[0].Start, Is.EqualTo(expectedSlot.Start));
            Assert.That(fb.BusySlots[0].End, Is.EqualTo(expectedSlot.End));
        });
    }

    [Test]
    public async Task GetFreeBusyAsync_StubReturnsUnknown_PassedThrough()
    {
        StoreCredentials(_accountId);

        // Stub default returns Unknown for all — no need to configure explicitly
        var emails = new List<string> { "alice@example.com", "bob@example.com" };

        var results = await _provider.GetFreeBusyAsync(_accountId, emails, QueryInterval);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.Select(r => r.Status), Is.All.EqualTo(FreeBusyStatus.Unknown));
    }

    [Test]
    public async Task GetFreeBusyAsync_StubReturnsUnavailable_PassedThrough()
    {
        StoreCredentials(_accountId);

        _serviceStub.SetFreeBusyResult(new List<AttendeeFreeBusy>
        {
            new() { Email = "alice@example.com", Status = FreeBusyStatus.Unavailable }
        });

        var results = await _provider.GetFreeBusyAsync(
            _accountId, new List<string> { "alice@example.com" }, QueryInterval);

        Assert.That(results[0].Status, Is.EqualTo(FreeBusyStatus.Unavailable));
    }

    [Test]
    public async Task GetFreeBusyAsync_WithCredentials_EmailsForwardedToStub()
    {
        StoreCredentials(_accountId);

        var emails = new List<string> { "alice@example.com", "bob@example.com", "carol@example.com" };

        var results = await _provider.GetFreeBusyAsync(_accountId, emails, QueryInterval);

        // Stub returns one entry per email from the attendeeEmails list
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results.Select(r => r.Email), Is.EquivalentTo(emails));
    }
}
