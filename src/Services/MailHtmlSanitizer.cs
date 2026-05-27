using System;
using System.Collections.Generic;
using System.Text;
using AngleSharp.Dom;
using Ganss.Xss;

namespace perinma.Services;

public static class MailHtmlSanitizer
{
    private static readonly string[] SafeTags =
    [
        "a", "abbr", "acronym", "address", "article", "aside", "b", "bdi", "big", "blockquote", "br",
        "caption", "center", "cite", "code", "col", "colgroup", "data", "dd", "del", "dfn", "div", "dl",
        "dt", "em", "figcaption", "figure", "font", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "i", "img",
        "ins", "kbd", "li", "mark", "ol", "p", "pre", "q", "rp", "rt", "ruby", "s", "samp", "section",
        "small", "span", "strike", "strong", "sub", "sup", "table", "tbody", "td", "tfoot", "th", "thead",
        "time", "tr", "tt", "u", "ul", "var", "wbr"
    ];

    private static readonly string[] SafeAttributes =
    [
        "align", "alt", "bgcolor", "border", "cellpadding", "cellspacing", "char", "charoff", "cite", "color",
        "colspan", "datetime", "dir", "headers", "height", "href", "hreflang", "lang", "rel", "rowspan", "scope",
        "span", "src", "start", "style", "summary", "title", "valign", "width"
    ];

    private static readonly string[] SafeSchemes =
    [
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeMailto,
        "cid",
        "data"
    ];

    public static SanitizedMailHtml Sanitize(string? html, bool allowExternalResources)
    {
        if (string.IsNullOrWhiteSpace(html))
            return SanitizedMailHtml.Empty;

        var hasBlockedRemoteContent = false;
        var hasInlineContentReferences = false;
        var sanitizer = CreateSanitizer(
            allowExternalResources,
            onBlockedRemoteContent: () => hasBlockedRemoteContent = true,
            onInlineContentReference: () => hasInlineContentReferences = true);

        var sanitizedFragment = sanitizer.Sanitize(html);
        if (string.IsNullOrWhiteSpace(sanitizedFragment))
            return new SanitizedMailHtml(string.Empty, hasBlockedRemoteContent, hasInlineContentReferences);

        return new SanitizedMailHtml(
            BuildDocumentHtml(sanitizedFragment, allowExternalResources),
            hasBlockedRemoteContent,
            hasInlineContentReferences);
    }

    private static HtmlSanitizer CreateSanitizer(
        bool allowExternalResources,
        Action onBlockedRemoteContent,
        Action onInlineContentReference)
    {
        var sanitizer = new HtmlSanitizer
        {
            KeepChildNodes = true,
            AllowCssCustomProperties = false,
            AllowDataAttributes = false
        };

        ReplaceSet(sanitizer.AllowedTags, SafeTags);
        ReplaceSet(sanitizer.AllowedAttributes, SafeAttributes);
        ReplaceSet(sanitizer.AllowedSchemes, SafeSchemes);
        sanitizer.AllowedAtRules.Clear();

        sanitizer.FilterUrl += (_, args) =>
        {
            var originalUrl = args.OriginalUrl.Trim();
            if (originalUrl.Length == 0)
            {
                args.SanitizedUrl = null;
                return;
            }

            if (IsInlineContentReference(originalUrl))
            {
                onInlineContentReference();
                return;
            }

            if (!IsRemoteReference(originalUrl))
                return;

            if (IsAnchor(args.Tag))
                return;

            if (allowExternalResources)
                return;

            onBlockedRemoteContent();
            args.SanitizedUrl = null;
            ApplyBlockedResourceFallback(args.Tag);
        };

        sanitizer.PostProcessDom += (_, args) =>
        {
            foreach (var anchor in args.Document.QuerySelectorAll("a"))
            {
                anchor.SetAttribute("rel", "noreferrer noopener nofollow");
                anchor.RemoveAttribute("target");
            }
        };

        return sanitizer;
    }

    private static void ReplaceSet(ISet<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }

    private static bool IsAnchor(IElement element) => string.Equals(element.TagName, "A", StringComparison.OrdinalIgnoreCase);

    private static bool IsInlineContentReference(string url)
        => url.StartsWith("cid:", StringComparison.OrdinalIgnoreCase);

    private static bool IsRemoteReference(string url)
    {
        if (url.StartsWith('#'))
            return false;

        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
        {
            return string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        return !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyBlockedResourceFallback(IElement element)
    {
        if (!string.Equals(element.TagName, "IMG", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(element.GetAttribute("alt")))
            element.SetAttribute("alt", "Blocked external image");

        element.SetAttribute("title", "External content is blocked for this message preview.");
    }

    private static string BuildDocumentHtml(string sanitizedFragment, bool allowExternalResources)
    {
        var csp = allowExternalResources
            ? "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'; script-src 'none'; connect-src 'none'; manifest-src 'none'; img-src data: cid: https: http:; media-src data: https: http:; font-src data: https: http:; style-src 'unsafe-inline' https: http:;"
            : "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'; script-src 'none'; connect-src 'none'; manifest-src 'none'; img-src data: cid:; media-src data:; font-src data:; style-src 'unsafe-inline';";

        var builder = new StringBuilder(sanitizedFragment.Length + 512);
        builder.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><meta http-equiv=\"Content-Security-Policy\" content=\"");
        builder.Append(csp);
        builder.Append("\"><style>html,body{margin:0;padding:0;background:transparent;color:CanvasText;font-family:system-ui,-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;font-size:14px;line-height:1.5;}body{overflow-wrap:anywhere;word-break:break-word;}img{max-width:100%;height:auto;}pre{white-space:pre-wrap;}table{max-width:100%;border-collapse:collapse;}blockquote{margin:1em 0;padding-left:1em;border-left:3px solid rgba(127,127,127,.35);}a{color:#2563eb;text-decoration:underline;}</style></head><body>");
        builder.Append(sanitizedFragment);
        builder.Append("</body></html>");
        return builder.ToString();
    }
}

public readonly record struct SanitizedMailHtml(string DocumentHtml, bool HasBlockedRemoteContent, bool HasInlineContentReferences)
{
    public static SanitizedMailHtml Empty { get; } = new(string.Empty, false, false);
}
