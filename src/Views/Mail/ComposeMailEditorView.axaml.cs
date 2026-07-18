using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace perinma.Views.Mail;

public partial class ComposeMailEditorView : UserControl
{
    public static readonly StyledProperty<string?> HtmlProperty =
        AvaloniaProperty.Register<ComposeMailEditorView, string?>(nameof(Html), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> PlainTextProperty =
        AvaloniaProperty.Register<ComposeMailEditorView, string?>(nameof(PlainText), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private NativeWebView? _webView;
    private bool _isReady;
    private bool _isApplyingHtml;
    private bool _focusPending;
    private bool _isAttachedToVisualTree;
    private bool _shellNavigationPending;
    private string? _shellMarkup;
    private string _lastAppliedHtml = string.Empty;

    public ComposeMailEditorView()
    {
        InitializeComponent();
        _webView = this.FindControl<NativeWebView>("EditorWebView");
        if (_webView != null)
        {
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.WebMessageReceived += OnWebMessageReceived;
        }
    }

    public string? Html
    {
        get => GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    public string? PlainText
    {
        get => GetValue(PlainTextProperty);
        set => SetValue(PlainTextProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttachedToVisualTree = true;
        _ = LoadShellAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        _isReady = false;
        _shellNavigationPending = !string.IsNullOrWhiteSpace(_shellMarkup);
        _lastAppliedHtml = string.Empty;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HtmlProperty && !_isApplyingHtml)
        {
            _ = ApplyHtmlAsync(Html ?? string.Empty);
            return;
        }

        if (change.Property == BoundsProperty || change.Property == IsVisibleProperty)
            _ = ActivatePendingWebViewAsync();
    }

    public async Task FocusEditorAsync()
    {
        _focusPending = true;
        await FlushPendingFocusAsync();
    }

    public async Task InsertImageAsync(string filePath, string? altText = null)
    {
        var fileUri = new Uri(Path.GetFullPath(filePath)).AbsoluteUri;
        await InvokeEditorAsync("insertImage", fileUri, altText ?? string.Empty);
    }

    private async Task LoadShellAsync()
    {
        if (_webView == null)
            return;

        if (_shellMarkup == null)
        {
            var assetUri = new Uri("avares://perinma/Assets/MailComposeEditor/editor.html");
            await using var assetStream = AssetLoader.Open(assetUri);
            using var reader = new StreamReader(assetStream);
            _shellMarkup = await reader.ReadToEndAsync();
        }

        _shellNavigationPending = true;
        await ActivatePendingWebViewAsync();
    }

    private async Task ActivatePendingWebViewAsync()
    {
        if (_webView == null || !_shellNavigationPending || string.IsNullOrWhiteSpace(_shellMarkup) || !CanActivateWebView())
            return;

        await Task.Yield();
        if (_webView == null || !_shellNavigationPending || string.IsNullOrWhiteSpace(_shellMarkup) || !CanActivateWebView())
            return;

        _isReady = false;
        _shellNavigationPending = false;
        _webView.NavigateToString(_shellMarkup);
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _isReady = e.IsSuccess;
        if (_isReady)
        {
            await ApplyHtmlAsync(Html ?? string.Empty);
            await FlushPendingFocusAsync();
        }
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Body))
            return;

        EditorMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<EditorMessage>(e.Body);
        }
        catch (JsonException)
        {
            return;
        }

        if (message == null || !string.Equals(message.Type, "contentChanged", StringComparison.Ordinal))
            return;

        _isApplyingHtml = true;
        try
        {
            _lastAppliedHtml = message.Html ?? string.Empty;
            SetCurrentValue(HtmlProperty, message.Html ?? string.Empty);
            SetCurrentValue(PlainTextProperty, message.PlainText ?? string.Empty);
        }
        finally
        {
            _isApplyingHtml = false;
        }
    }

    private async Task ApplyHtmlAsync(string html)
    {
        if (!_isReady || _webView == null)
            return;
        if (string.Equals(_lastAppliedHtml, html, StringComparison.Ordinal))
            return;

        _lastAppliedHtml = html;
        await InvokeEditorAsync("setHtml", html);
    }

    private async Task FlushPendingFocusAsync()
    {
        if (!_focusPending || !_isReady || _webView == null)
            return;

        await InvokeEditorAsync("focus");
        _focusPending = false;
    }

    private bool CanActivateWebView()
        => _isAttachedToVisualTree
           && IsVisible
           && Bounds.Width > 0
           && Bounds.Height > 0;

    private async Task InvokeEditorAsync(string methodName, params object?[] arguments)
    {
        if (!_isReady || _webView == null)
            return;

        var args = string.Join(", ", Array.ConvertAll(arguments, static argument => JsonSerializer.Serialize(argument)));
        await _webView.InvokeScript($"window.perinmaEditor?.{methodName}({args});");
    }

    private sealed class EditorMessage
    {
        public string? Type { get; init; }
        public string? Html { get; init; }
        public string? PlainText { get; init; }
    }
}
