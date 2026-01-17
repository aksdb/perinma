using Avalonia;
using Avalonia.Styling;

namespace perinma.Services;

public class ThemeService
{
    public ThemeVariant CurrentTheme => Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;

    public void SetTheme(ThemeVariant theme)
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = theme;
        }
    }

    public void SetLightTheme() => SetTheme(ThemeVariant.Light);

    public void SetDarkTheme() => SetTheme(ThemeVariant.Dark);

    public bool IsLightTheme => CurrentTheme == ThemeVariant.Light;

    public bool IsDarkTheme => CurrentTheme == ThemeVariant.Dark;
}
