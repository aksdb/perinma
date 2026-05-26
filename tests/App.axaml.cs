using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Theme;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;

namespace tests;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        this.UseAtomUI(builder =>
        {
            builder.WithDefaultTheme(IThemeManager.DEFAULT_THEME_ID);
            builder.UseDesktopControls();
        });
    }
}
