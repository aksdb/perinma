using System;
using System.Threading.Tasks;
using AtomUI.Theme;
using Avalonia;
using Avalonia.Styling;

namespace perinma.Services;

public class ThemeService
{
    private readonly SettingsService _settingsService;

    public ThemeService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public ThemeVariant CurrentTheme => GetThemeManager()?.IsDarkThemeMode == true ? ThemeVariant.Dark : ThemeVariant.Light;

    public void SetTheme(ThemeVariant theme)
    {
        var isDarkTheme = theme == ThemeVariant.Dark;
        var application = Application.Current;
        if (application == null)
            return;

        var themeManager = GetThemeManager();
        if (themeManager != null)
        {
            themeManager.IsDarkThemeMode = isDarkTheme;
        }

        application.SetValue(IThemeManager.IsDarkThemeModeProperty, isDarkTheme);
    }

    public async Task LoadThemeAsync()
    {
        var savedTheme = await _settingsService.GetThemeAsync();
        var themeVariant = savedTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        SetTheme(themeVariant);
    }

    public Task SaveThemeAsync() => _settingsService.SetThemeAsync(CurrentTheme == ThemeVariant.Dark ? "Dark" : "Light");

    public void SetLightTheme() => SetTheme(ThemeVariant.Light);

    public void SetDarkTheme() => SetTheme(ThemeVariant.Dark);

    public bool IsLightTheme => CurrentTheme == ThemeVariant.Light;

    public bool IsDarkTheme => CurrentTheme == ThemeVariant.Dark;

    private static IThemeManager? GetThemeManager()
    {
        var themeManagerType = typeof(IThemeManager).Assembly.GetType("AtomUI.Theme.ThemeManager");
        var currentProperty = themeManagerType?.GetProperty(
            "Current",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return currentProperty?.GetValue(null) as IThemeManager;
    }
}
