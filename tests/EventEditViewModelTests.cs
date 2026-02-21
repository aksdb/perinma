using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CredentialStore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using perinma.Models;
using perinma.Services;
using perinma.Services.Google;
using perinma.Storage;
using perinma.Storage.Models;
using tests.Fakes;
using perinma.Views.Calendar;

namespace tests;

[TestFixture]
public class EventEditViewModelTests
{
    private DatabaseService _database = null!;
    private SqliteStorage _storage = null!;
    private CredentialManagerService _credentialManager = null!;
    private GoogleCalendarServiceStub _serviceStub = null!;
    private ICalendarProvider _provider = null!;
    private Dictionary<AccountType, ICalendarProvider> _providers = null!;
    private Account _account = null!;
    private Calendar _calendar = null!;
    private string _completedEventId = string.Empty;

    [SetUp]
    public void Setup()
    {
        _database = new DatabaseService(inMemory: true);
        _credentialManager = new CredentialManagerService(new InMemoryCredentialStore());
        _storage = new SqliteStorage(_database, _credentialManager);
        _serviceStub = new GoogleCalendarServiceStub();
        _provider = new GoogleCalendarProvider(_serviceStub, _credentialManager);
        _providers = new Dictionary<AccountType, ICalendarProvider>
        {
            { AccountType.Google, _provider }
        };
        
        var services = new ServiceCollection();
        services.AddSingleton(_provider);
        services.AddSingleton(_providers);
        services.AddSingleton(_storage);
        services.AddSingleton(new SyncService(_storage, _credentialManager, _providers, null!));
        perinma.App.Services = services.BuildServiceProvider();

        var accountId = Guid.NewGuid();
        _account = new Account
        {
            Id = accountId,
            Name = "Test Account",
            Type = AccountType.Google,
            SortOrder = 0
        };

        // Add account to storage so calendar can reference it
        _storage.CreateAccountAsync(new AccountDbo
        {
            AccountId = accountId.ToString(),
            Name = "Test Account",
            Type = AccountType.Google.ToString(),
            SortOrder = 0
        }).Wait();

        var calendarId = Guid.NewGuid();
        _calendar = new Calendar
        {
            Account = _account,
            Id = calendarId,
            ExternalId = "test-calendar",
            Name = "Test Calendar",
            Color = "#ff0000",
            Enabled = true
        };

        // Add calendar to storage so events can be saved to it
        _storage.CreateOrUpdateCalendarAsync(new CalendarDbo
        {
            AccountId = _account.Id.ToString(),
            CalendarId = calendarId.ToString(),
            ExternalId = "test-calendar",
            Name = "Test Calendar",
            Color = "#ff0000",
            Enabled = 1
        }).Wait();

        _completedEventId = string.Empty;

        // Add mock Google credentials for the test account
        _credentialManager.StoreGoogleCredentials(accountId.ToString(), new GoogleCredentials
        {
            Type = "Google",
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        });
    }

    [TearDown]
    public void TearDown()
    {
        _storage?.Dispose();
        _database?.Dispose();
    }

    [Test]
    public void Constructor_CreateMode_SetsDefaults()
    {
        var viewModel = new EventEditViewModel(
            null,
            _calendar,
            id => _completedEventId = id);

        Assert.That(viewModel.IsEditMode, Is.False);
        Assert.That(viewModel.WindowTitle, Is.EqualTo("New Event"));
        Assert.That(viewModel.SelectedCalendar, Is.EqualTo(_calendar));
        Assert.That(viewModel.Title, Is.EqualTo(string.Empty));
        Assert.That(viewModel.Description, Is.EqualTo(string.Empty));
        Assert.That(viewModel.Location, Is.EqualTo(string.Empty));
        Assert.That(viewModel.Duration, Is.EqualTo(TimeSpan.FromMinutes(30)));
        Assert.That(viewModel.ErrorMessage, Is.EqualTo(string.Empty));
        Assert.That(viewModel.IsSaving, Is.False);
    }

    [Test]
    public void SaveAsync_CreateMode_NoTitle_ShowsError()
    {
        var viewModel = new EventEditViewModel(
            null,
            _calendar,
            id => _completedEventId = id);

        var task = viewModel.SaveCommand.ExecuteAsync(null);
        task.Wait();

        Assert.That(viewModel.ErrorMessage, Is.EqualTo("Please enter a title"));
        Assert.That(_completedEventId, Is.Empty);
    }

    [Test]
    public async Task SaveAsync_CreateMode_ValidData_CreatesEvent()
    {
        var viewModel = new EventEditViewModel(
            null,
            _calendar,
            id => _completedEventId = id);

        viewModel.Title = "New Meeting";
        viewModel.Description = "Team standup";
        viewModel.Location = "Conference Room A";
        viewModel.StartTime = DateTime.Now.AddHours(2);
        viewModel.Duration = TimeSpan.FromHours(1);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.That(viewModel.ErrorMessage, Is.EqualTo(string.Empty));
        Assert.That(_completedEventId, Is.EqualTo("stub_event_id"));
    }

    [Test]
    public void StartTimeChanged_UpdatesEndTimeToMaintainDuration()
    {
        var viewModel = new EventEditViewModel(
            null,
            _calendar,
            id => _completedEventId = id);

        var originalDuration = viewModel.Duration;
        var newStartTime = DateTime.Now.AddHours(5);

        viewModel.StartTime = newStartTime;

        Assert.That(viewModel.EndTime, Is.EqualTo(newStartTime.Add(originalDuration)));
        Assert.That(viewModel.Duration, Is.EqualTo(originalDuration));
    }

    [Test]
    public void EndTimeChanged_UpdatesDuration()
    {
        var viewModel = new EventEditViewModel(
            null,
            _calendar,
            id => _completedEventId = id);

        var startTime = viewModel.StartTime;
        var newEndTime = startTime.AddHours(2);

        viewModel.EndTime = newEndTime;

        Assert.That(viewModel.Duration, Is.EqualTo(TimeSpan.FromHours(2)));
    }

    [Test]
    public void EndTimeChanged_InvalidValue_ResetsToStartTimePlusDuration()
    {
        var viewModel = new EventEditViewModel(
            null,
            _calendar,
            id => _completedEventId = id);

        var startTime = viewModel.StartTime;
        var duration = viewModel.Duration;
        var invalidEndTime = startTime.AddHours(-1);

        viewModel.EndTime = invalidEndTime;

        Assert.That(viewModel.EndTime, Is.EqualTo(startTime.Add(duration)));
    }

    [Test]
    public void DurationChanged_UpdatesEndTime()
    {
        var viewModel = new EventEditViewModel(
            null,
            _calendar,
            id => _completedEventId = id);

        var startTime = viewModel.StartTime;
        var newDuration = TimeSpan.FromHours(2);

        viewModel.Duration = newDuration;

        Assert.That(viewModel.EndTime, Is.EqualTo(startTime.Add(newDuration)));
    }

    [Test]
    public void TitleChanged_NotifiesIsEditMode()
    {
        var viewModel = new EventEditViewModel(
            null,
            _calendar,
            id => _completedEventId = id);

        var propertyChanged = false;
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(viewModel.IsEditMode))
                propertyChanged = true;
        };

        viewModel.Title = "Test";

        Assert.That(propertyChanged, Is.True);
    }

    [Test]
    public void Cancel_InvokesOnCompletedWithEmptyString()
    {
        var viewModel = new EventEditViewModel(
            null,
            _calendar,
            id => _completedEventId = id);

        viewModel.CancelCommand.Execute(null);

        Assert.That(_completedEventId, Is.Empty);
    }

    [Test]
    public void Cancel_InvokesRequestClose()
    {
        var viewModel = new EventEditViewModel(
            null,
            _calendar,
            id => _completedEventId = id);

        var closeInvoked = false;
        viewModel.RequestClose += (s, e) => closeInvoked = true;

        viewModel.CancelCommand.Execute(null);

        Assert.That(closeInvoked, Is.True);
    }
}
