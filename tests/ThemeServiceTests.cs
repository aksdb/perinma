using System.Threading.Tasks;
using AtomUI;
using AtomUI.Theme;
using AtomUI.Theme.Algorithms;
using AtomUI.Theme.Configuration;
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
    public async Task SetDarkThemeAsync_UpdatesAtomUiThemeState()
    {
        await ResetThemeState();
        var application = Application.Current!;
        var themeManager = application.GetThemeManager()!;

        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settings = new SettingsService(storage);
        var service = new ThemeService(settings);

        await service.SetDarkThemeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTheme, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(themeManager.CurrentTheme.Appearance, Is.EqualTo(ThemeAppearance.Dark));
            Assert.That(application.ActualThemeVariant, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(service.IsDarkTheme, Is.True);
            Assert.That(service.IsLightTheme, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task SetLightThemeAsync_AfterDarkTheme_UpdatesAtomUiThemeState()
    {
        await ResetThemeState();
        var application = Application.Current!;
        var themeManager = application.GetThemeManager()!;

        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settings = new SettingsService(storage);
        var service = new ThemeService(settings);

        await service.SetDarkThemeAsync();
        await service.SetLightThemeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTheme, Is.EqualTo(ThemeVariant.Light));
            Assert.That(themeManager.CurrentTheme.Appearance, Is.EqualTo(ThemeAppearance.Light));
            Assert.That(application.ActualThemeVariant, Is.EqualTo(ThemeVariant.Light));
            Assert.That(service.IsDarkTheme, Is.False);
            Assert.That(service.IsLightTheme, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task LoadThemeAsync_AppliesPersistedThemeMode()
    {
        await ResetThemeState();
        var application = Application.Current!;
        var themeManager = application.GetThemeManager()!;

        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settings = new SettingsService(storage);
        await settings.SetThemeAsync("Dark");
        var service = new ThemeService(settings);

        await service.LoadThemeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTheme, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(themeManager.CurrentTheme.Appearance, Is.EqualTo(ThemeAppearance.Dark));
            Assert.That(application.ActualThemeVariant, Is.EqualTo(ThemeVariant.Dark));
        });
    }

    [AvaloniaTest]
    public async Task SaveThemeAsync_PersistsCurrentThemeSelection()
    {
        await ResetThemeState();

        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settings = new SettingsService(storage);
        var service = new ThemeService(settings);

        await service.SetDarkThemeAsync();
        await service.SaveThemeAsync();
        var savedDarkTheme = await settings.GetThemeAsync();

        await service.SetLightThemeAsync();
        await service.SaveThemeAsync();
        var savedLightTheme = await settings.GetThemeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(savedDarkTheme, Is.EqualTo("Dark"));
            Assert.That(savedLightTheme, Is.EqualTo("Light"));
        });
    }

    private static async Task ResetThemeState()
    {
        var themeManager = Application.Current!.GetThemeManager();
        if (themeManager is null)
        {
            Assert.Fail("Theme manager is not available in the headless application.");
            return;
        }

        var lightConfig = new ThemeConfigBuilder()
            .WithAlgorithms(ThemeAlgorithm.Default)
            .Build();
        await themeManager.ApplyThemeAsync(
            new ThemeRequest(IThemeManager.DEFAULT_THEME_ID, lightConfig, ThemeTransitionReason.UserRequest));
    }
}
