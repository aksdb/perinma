using System.Threading.Tasks;
using AtomUI.Theme;
using Avalonia;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using CredentialStore;
using perinma.Services;
using perinma.Storage;

namespace tests;

[TestFixture]
public class ThemeServiceTests
{
    [AvaloniaTest]
    public void SetDarkTheme_UpdatesAvaloniaAndAtomUiThemeState()
    {
        ResetThemeState();
        var application = Application.Current!;

        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settings = new SettingsService(storage);
        var service = new ThemeService(settings);

        service.SetDarkTheme();

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTheme, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(application.GetValue(IThemeManager.IsDarkThemeModeProperty), Is.True);
            Assert.That(service.IsDarkTheme, Is.True);
            Assert.That(service.IsLightTheme, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task LoadThemeAsync_AppliesPersistedThemeMode()
    {
        ResetThemeState();
        var application = Application.Current!;

        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settings = new SettingsService(storage);
        await settings.SetThemeAsync("Dark");
        var service = new ThemeService(settings);

        await service.LoadThemeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTheme, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(application.GetValue(IThemeManager.IsDarkThemeModeProperty), Is.True);
        });
    }

    [AvaloniaTest]
    public async Task SaveThemeAsync_PersistsCurrentThemeSelection()
    {
        ResetThemeState();

        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settings = new SettingsService(storage);
        var service = new ThemeService(settings);

        service.SetDarkTheme();
        await service.SaveThemeAsync();
        var savedDarkTheme = await settings.GetThemeAsync();

        service.SetLightTheme();
        await service.SaveThemeAsync();
        var savedLightTheme = await settings.GetThemeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(savedDarkTheme, Is.EqualTo("Dark"));
            Assert.That(savedLightTheme, Is.EqualTo("Light"));
        });
    }

    private static void ResetThemeState()
    {
        Application.Current!.SetValue(IThemeManager.IsDarkThemeModeProperty, false);
    }
}
