using CredentialStore;
using perinma.Services;
using perinma.Storage;

namespace tests;

[TestFixture]
public class DebugFeaturesServiceTests
{
    [Test]
    public async Task LoadAsync_UsesPersistedDebugSetting()
    {
        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settingsService = new SettingsService(storage);
        await settingsService.SetDebuggingEnabledAsync(true);
        var debugFeatures = new DebugFeaturesService(settingsService);

        await debugFeatures.LoadAsync();

        Assert.That(debugFeatures.IsDebuggingEnabled, Is.True);
    }

    [Test]
    public async Task ToggleDebuggingAsync_UpdatesStateAndPersists()
    {
        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settingsService = new SettingsService(storage);
        var debugFeatures = new DebugFeaturesService(settingsService);

        await debugFeatures.ToggleDebuggingAsync();

        Assert.That(debugFeatures.IsDebuggingEnabled, Is.True);
        Assert.That(await settingsService.GetDebuggingEnabledAsync(), Is.True);
    }
}
