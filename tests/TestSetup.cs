using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Theme;
using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(tests.TestAppBuilder))]

namespace tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions())
        .WithAtomUIDefaultOptions()
        .UseAtomUI(builder =>
        {
            builder.WithInitialTheme(IThemeManager.DEFAULT_THEME_ID);
            builder.UseDesktopControls();
        });
}
