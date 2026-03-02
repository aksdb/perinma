using System;
using System.Threading.Tasks;
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

    public ThemeVariant CurrentTheme => Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;

    public void SetTheme(ThemeVariant theme)
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = theme;
        }
    }

    public async Task LoadThemeAsync()
    {
        var savedTheme = await _settingsService.GetThemeAsync();
        var themeVariant = savedTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        SetTheme(themeVariant);
    }

    public async Task SaveThemeAsync()
    {
        var theme = CurrentTheme == ThemeVariant.Dark ? "Dark" : "Light";
        await _settingsService.SetThemeAsync(theme);
    }

    public void SetLightTheme() => SetTheme(ThemeVariant.Light);

    public void SetDarkTheme() => SetTheme(ThemeVariant.Dark);

    public bool IsLightTheme => CurrentTheme == ThemeVariant.Light;

    public bool IsDarkTheme => CurrentTheme == ThemeVariant.Dark;
}
