using AtomUI;
using AtomUI.Theme;
using Avalonia;
using System;

namespace perinma;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithAtomUIDefaultOptions()
            .UseAtomUI(builder =>
            {
                builder.WithInitialTheme(IThemeManager.DEFAULT_THEME_ID);
                AtomUI.Desktop.Controls.ThemeManagerBuilderExtensions.UseDesktopControls(builder);
                AlibabaSansThemeManagerBuilderExtensions.UseAlibabaSansFont(builder);
            })
            .LogToTrace();
}
