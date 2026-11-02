using Avalonia;
using Avalonia.Markup.Xaml;

namespace tests;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
