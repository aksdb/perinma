using System.Reflection;
using System.Threading.Tasks;
using AtomUI.Theme;
using Avalonia;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
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
        var themeManager = GetThemeManager();

        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settings = new SettingsService(storage);
        var service = new ThemeService(settings);

        service.SetDarkTheme();

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTheme, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(themeManager.IsDarkThemeMode, Is.True);
            Assert.That(GetBrushColor(application, GetThemeVariant(themeManager), "AppShellSurfaceBrush"),
                Is.EqualTo(Color.Parse("#FF141414")));
            Assert.That(application.GetValue(IThemeManager.IsDarkThemeModeProperty), Is.True);
            Assert.That(service.IsDarkTheme, Is.True);
            Assert.That(service.IsLightTheme, Is.False);
        });
    }

    [AvaloniaTest]
    public void SetLightTheme_AfterDarkTheme_UpdatesAvaloniaAndAtomUiThemeState()
    {
        ResetThemeState();
        var application = Application.Current!;
        var themeManager = GetThemeManager();

        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settings = new SettingsService(storage);
        var service = new ThemeService(settings);

        service.SetDarkTheme();
        service.SetLightTheme();

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTheme, Is.EqualTo(ThemeVariant.Light));
            Assert.That(themeManager.IsDarkThemeMode, Is.False);
            Assert.That(GetBrushColor(application, GetThemeVariant(themeManager), "AppShellSurfaceBrush"),
                Is.EqualTo(Color.Parse("#FFFFFFFF")));
            Assert.That(application.GetValue(IThemeManager.IsDarkThemeModeProperty), Is.False);
            Assert.That(service.IsDarkTheme, Is.False);
            Assert.That(service.IsLightTheme, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task LoadThemeAsync_AppliesPersistedThemeMode()
    {
        ResetThemeState();
        var application = Application.Current!;
        var themeManager = GetThemeManager();

        using var database = new DatabaseService(inMemory: true);
        using var storage = new SqliteStorage(database, new CredentialManagerService(new InMemoryCredentialStore()));
        var settings = new SettingsService(storage);
        await settings.SetThemeAsync("Dark");
        var service = new ThemeService(settings);

        await service.LoadThemeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTheme, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(themeManager.IsDarkThemeMode, Is.True);
            Assert.That(GetBrushColor(application, GetThemeVariant(themeManager), "AppShellSurfaceBrush"),
                Is.EqualTo(Color.Parse("#FF141414")));
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
        var themeManager = GetThemeManager();
        themeManager.IsDarkThemeMode = false;
        Application.Current!.SetValue(IThemeManager.IsDarkThemeModeProperty, false);
    }

    private static IThemeManager GetThemeManager()
    {
        var themeManagerType = typeof(IThemeManager).Assembly.GetType("AtomUI.Theme.ThemeManager");
        var currentProperty = themeManagerType!.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
        return (IThemeManager)currentProperty!.GetValue(null)!;
    }

    private static Color GetBrushColor(Application application, ThemeVariant themeVariant, string resourceKey)
    {
        var found = application.TryGetResource(resourceKey, themeVariant, out var resource);
        Assert.That(found, Is.True, $"Missing resource '{resourceKey}' for theme '{themeVariant}'.");
        Assert.That(resource, Is.InstanceOf<SolidColorBrush>(), $"Resource '{resourceKey}' should be a SolidColorBrush.");
        return ((SolidColorBrush)resource!).Color;
    }

    private static ThemeVariant GetThemeVariant(IThemeManager themeManager)
    {
        var themeVariantProperty = themeManager.GetType().GetProperty("ThemeVariant", BindingFlags.Public | BindingFlags.Instance);
        return (ThemeVariant)themeVariantProperty!.GetValue(themeManager)!;
    }
}
