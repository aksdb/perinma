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

    public ThemeVariant CurrentTheme => IsDarkThemeMode ? ThemeVariant.Dark : ThemeVariant.Light;

    public void SetTheme(ThemeVariant theme)
    {
        var application = Application.Current;
        if (application == null)
            return;

        var isDarkTheme = theme == ThemeVariant.Dark;
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

    public Task SaveThemeAsync() => _settingsService.SetThemeAsync(IsDarkThemeMode ? "Dark" : "Light");

    public void SetLightTheme() => SetTheme(ThemeVariant.Light);

    public void SetDarkTheme() => SetTheme(ThemeVariant.Dark);

    public bool IsLightTheme => !IsDarkThemeMode;

    public bool IsDarkTheme => IsDarkThemeMode;

    private static bool IsDarkThemeMode => Application.Current?.GetValue(IThemeManager.IsDarkThemeModeProperty) == true;
}
