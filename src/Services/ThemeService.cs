using System;
using AtomUI;
using System.Threading.Tasks;
using AtomUI.Theme;
using AtomUI.Theme.Algorithms;
using AtomUI.Theme.Configuration;
using Avalonia;
using Avalonia.Styling;

namespace perinma.Services;

public class ThemeService
{
    private readonly SettingsService _settingsService;

    private static readonly ThemeConfig LightThemeConfig =
        new ThemeConfigBuilder().WithAlgorithms(ThemeAlgorithm.Default).Build();

    private static readonly ThemeConfig DarkThemeConfig =
        new ThemeConfigBuilder().WithAlgorithms(ThemeAlgorithm.Default, ThemeAlgorithm.Dark).Build();

    public ThemeService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public ThemeVariant CurrentTheme =>
        GetThemeManager()?.CurrentTheme.Appearance == ThemeAppearance.Dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

    public async Task SetThemeAsync(ThemeVariant theme)
    {
        var themeManager = GetThemeManager();
        if (themeManager is null)
        {
            return;
        }

        var config = theme == ThemeVariant.Dark ? DarkThemeConfig : LightThemeConfig;
        await themeManager.ApplyThemeAsync(
            new ThemeRequest(IThemeManager.DEFAULT_THEME_ID, config, ThemeTransitionReason.UserRequest));
    }

    public async Task LoadThemeAsync()
    {
        var savedTheme = await _settingsService.GetThemeAsync();
        var themeVariant = savedTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        await SetThemeAsync(themeVariant);
    }

    public Task SaveThemeAsync() =>
        _settingsService.SetThemeAsync(CurrentTheme == ThemeVariant.Dark ? "Dark" : "Light");

    public Task SetLightThemeAsync() => SetThemeAsync(ThemeVariant.Light);

    public Task SetDarkThemeAsync() => SetThemeAsync(ThemeVariant.Dark);

    public bool IsLightTheme => CurrentTheme == ThemeVariant.Light;

    public bool IsDarkTheme => CurrentTheme == ThemeVariant.Dark;

    private static IThemeManager? GetThemeManager() => Application.Current?.GetThemeManager();
}
