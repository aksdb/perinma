using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(tests.TestAppBuilder))]

namespace tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
