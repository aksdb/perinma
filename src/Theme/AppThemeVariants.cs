using Avalonia.Styling;

namespace perinma.Theme;

public static class AppThemeVariants
{
    public static ThemeVariant DaybreakBlue { get; } = new("DaybreakBlue", ThemeVariant.Light);

    public static ThemeVariant DaybreakBlueDark { get; } = new("DaybreakBlue-Dark", ThemeVariant.Dark);
}
