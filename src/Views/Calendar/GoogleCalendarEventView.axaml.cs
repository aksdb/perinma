using Avalonia.Controls;
using Avalonia.Input;
using perinma.Utils;
using TheArtOfDev.HtmlRenderer.Avalonia;
using TheArtOfDev.HtmlRenderer.Core.Entities;

namespace perinma.Views.Calendar;

public partial class GoogleCalendarEventView : UserControl
{
    public GoogleCalendarEventView()
    {
        InitializeComponent();
        DescriptionHtmlPanel.LinkClicked += OnHtmlLinkClicked;
    }

    private void OnGoogleMeetLinkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GoogleCalendarEventViewModel viewModel && !string.IsNullOrEmpty(viewModel.GoogleMeetLink))
        {
            PlatformUtil.OpenBrowser(viewModel.GoogleMeetLink);
            e.Handled = true;
        }
    }

    private void OnHtmlLinkClicked(object? sender, HtmlRendererRoutedEventArgs<HtmlLinkClickedEventArgs> e)
    {
        if (!string.IsNullOrEmpty(e.Event?.Link))
        {
            PlatformUtil.OpenBrowser(e.Event.Link);
            e.Event.Handled = true;
        }
    }
    
    private void OnAttachmentLinkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock { Tag: string fileUrl } && !string.IsNullOrEmpty(fileUrl))
        {
            PlatformUtil.OpenBrowser(fileUrl);
            e.Handled = true;
        }
    }
}
