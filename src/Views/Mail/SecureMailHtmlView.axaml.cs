using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace perinma.Views.Mail;

public partial class SecureMailHtmlView : UserControl
{
    public static readonly StyledProperty<string?> HtmlProperty =
        AvaloniaProperty.Register<SecureMailHtmlView, string?>(nameof(Html));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<SecureMailHtmlView, string?>(nameof(PlaceholderText));

    private NativeWebView? _webView;
    private Border? _placeholderBorder;
    private TextBlock? _placeholderTextBlock;
    private string? _lastRenderedHtml;
    private bool _allowNextNavigation;
    private bool _isAttachedToVisualTree;

    public SecureMailHtmlView()
    {
        InitializeComponent();

        _webView = this.FindControl<NativeWebView>("PreviewWebView");
        _placeholderBorder = this.FindControl<Border>("PlaceholderBorder");
        _placeholderTextBlock = this.FindControl<TextBlock>("PlaceholderTextBlock");

        if (_webView is not null)
        {
            _webView.NavigationStarted += OnNavigationStarted;
            _webView.NewWindowRequested += OnNewWindowRequested;
        }
    }

    public string? Html
    {
        get => GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttachedToVisualTree = true;
        UpdateView(forceReload: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _allowNextNavigation = false;
        _isAttachedToVisualTree = false;
        _lastRenderedHtml = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HtmlProperty || change.Property == PlaceholderTextProperty)
            UpdateView(forceReload: change.Property == HtmlProperty);
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (_allowNextNavigation)
        {
            _allowNextNavigation = false;
            return;
        }

        if (e.Request == null || !IsPreviewNavigation(e.Request))
            e.Cancel = true;
    }

    private void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        e.Handled = true;
    }

    private void UpdateView(bool forceReload)
    {
        if (_webView is null || _placeholderBorder is null || _placeholderTextBlock is null)
            return;

        var html = Html;
        var hasHtml = !string.IsNullOrWhiteSpace(html);
        var placeholderText = PlaceholderText ?? string.Empty;

        _placeholderTextBlock.Text = placeholderText;
        _placeholderBorder.IsVisible = !hasHtml && placeholderText.Length > 0;
        _webView.IsVisible = hasHtml;

        if (!_isAttachedToVisualTree || !hasHtml)
            return;

        if (!forceReload && string.Equals(_lastRenderedHtml, html, StringComparison.Ordinal))
            return;

        _lastRenderedHtml = html;
        _allowNextNavigation = true;
        _webView.NavigateToString(html!);
    }

    private static bool IsPreviewNavigation(Uri requestUri)
    {
        if (!requestUri.IsAbsoluteUri)
            return false;

        return requestUri.Scheme switch
        {
            "about" => true,
            "data" => true,
            _ => false
        };
    }
}
