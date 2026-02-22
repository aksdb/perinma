using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using perinma.Utils;

namespace perinma.Views.Common;

public partial class Hyperlink : UserControl
{
    public static readonly StyledProperty<string> UriProperty =
        AvaloniaProperty.Register<Hyperlink, string>(nameof(Uri));

    public static readonly StyledProperty<string> DisplayTextProperty =
        AvaloniaProperty.Register<Hyperlink, string>(nameof(DisplayText));

    public string Uri
    {
        get => GetValue(UriProperty);
        set => SetValue(UriProperty, value);
    }

    public string DisplayText
    {
        get => GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    public Hyperlink()
    {
        InitializeComponent();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            PlatformUtil.OpenBrowser(Uri);
            e.Handled = true;
        }
        
        base.OnPointerPressed(e);
    }
    
    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        PlatformUtil.OpenBrowser(Uri);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        PlatformUtil.Clipboard().SetTextAsync(Uri);
    }
    
}