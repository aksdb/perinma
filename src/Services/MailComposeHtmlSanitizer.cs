using System;
using System.Collections.Generic;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace perinma.Services;

public static class MailComposeHtmlSanitizer
{
    private static readonly string[] SafeTags =
    [
        "a", "blockquote", "br", "code", "div", "em", "i", "img", "li", "ol", "p", "pre",
        "s", "span", "strong", "sub", "sup", "u", "ul"
    ];

    private static readonly string[] SafeAttributes =
    [
        "alt", "href", "src", "style", "title"
    ];

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "blockquote", "div", "li", "ol", "p", "pre", "ul"
    };

    private static readonly HtmlParser HtmlParser = new();

    public static SanitizedComposeBody Sanitize(string? html, bool allowLocalFileReferences = true)
    {
        if (string.IsNullOrWhiteSpace(html))
            return SanitizedComposeBody.Empty;

        var hasBlockedContent = false;
        var sanitizer = new HtmlSanitizer
        {
            KeepChildNodes = true,
            AllowCssCustomProperties = false,
            AllowDataAttributes = false
        };

        ReplaceSet(sanitizer.AllowedTags, SafeTags);
        ReplaceSet(sanitizer.AllowedAttributes, SafeAttributes);
        ReplaceSet(sanitizer.AllowedSchemes,
        [
            Uri.UriSchemeHttp,
            Uri.UriSchemeHttps,
            Uri.UriSchemeMailto,
            "cid",
            "data",
            allowLocalFileReferences ? Uri.UriSchemeFile : string.Empty
        ]);
        sanitizer.AllowedAtRules.Clear();
        sanitizer.FilterUrl += (_, args) =>
        {
            var url = args.OriginalUrl.Trim();
            if (url.Length == 0)
            {
                args.SanitizedUrl = null;
                return;
            }

            if (string.Equals(args.Tag.TagName, "IMG", StringComparison.OrdinalIgnoreCase)
                && IsRemoteReference(url))
            {
                hasBlockedContent = true;
                args.SanitizedUrl = null;
                return;
            }

            if (!allowLocalFileReferences && url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                hasBlockedContent = true;
                args.SanitizedUrl = null;
            }
        };

        sanitizer.PostProcessDom += (_, args) =>
        {
            foreach (var anchor in args.Document.QuerySelectorAll("a"))
            {
                anchor.SetAttribute("rel", "noreferrer noopener nofollow");
                anchor.RemoveAttribute("target");
            }
        };

        var sanitizedHtml = sanitizer.Sanitize(html).Trim();
        if (sanitizedHtml.Length == 0)
            return new SanitizedComposeBody(string.Empty, string.Empty, hasBlockedContent);

        return new SanitizedComposeBody(
            sanitizedHtml,
            BuildPlainText(sanitizedHtml),
            hasBlockedContent);
    }

    private static void ReplaceSet(ISet<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                target.Add(value);
        }
    }

    private static bool IsRemoteReference(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
        {
            return string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string BuildPlainText(string sanitizedHtml)
    {
        var document = HtmlParser.ParseDocument($"<body>{sanitizedHtml}</body>");
        if (document.Body == null)
            return string.Empty;

        var builder = new StringBuilder(sanitizedHtml.Length);
        AppendChildren(document.Body, builder, preserveWhitespace: false);
        return NormalizePlainText(builder.ToString());
    }

    private static void AppendChildren(IElement element, StringBuilder builder, bool preserveWhitespace)
    {
        foreach (var child in element.ChildNodes)
            AppendNode(child, builder, preserveWhitespace);
    }

    private static void AppendNode(INode node, StringBuilder builder, bool preserveWhitespace)
    {
        if (node is IText textNode)
        {
            AppendText(builder, textNode.Data, preserveWhitespace);
            return;
        }

        if (node is not IElement element)
            return;

        var tagName = element.TagName;
        if (string.Equals(tagName, "BR", StringComparison.OrdinalIgnoreCase))
        {
            AppendNewLine(builder, count: 1);
            return;
        }

        if (string.Equals(tagName, "PRE", StringComparison.OrdinalIgnoreCase))
        {
            AppendNewLine(builder, count: 1);
            builder.Append(element.TextContent.TrimEnd());
            AppendNewLine(builder, count: 2);
            return;
        }

        if (string.Equals(tagName, "LI", StringComparison.OrdinalIgnoreCase))
        {
            AppendNewLine(builder, count: 1);
            builder.Append("• ");
            AppendChildren(element, builder, preserveWhitespace);
            AppendNewLine(builder, count: 1);
            return;
        }

        var isBlock = BlockTags.Contains(tagName);
        if (isBlock)
            AppendNewLine(builder, count: 1);

        AppendChildren(element, builder, preserveWhitespace || string.Equals(tagName, "CODE", StringComparison.OrdinalIgnoreCase));

        if (isBlock)
            AppendNewLine(builder, count: string.Equals(tagName, "P", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
    }

    private static void AppendText(StringBuilder builder, string value, bool preserveWhitespace)
    {
        if (value.Length == 0)
            return;

        var text = preserveWhitespace ? value : NormalizeInlineWhitespace(value);
        if (text.Length == 0)
            return;

        if (!preserveWhitespace && builder.Length > 0 && NeedsLeadingSpace(builder, text))
            builder.Append(' ');

        builder.Append(text);
    }

    private static bool NeedsLeadingSpace(StringBuilder builder, string nextText)
    {
        var lastCharacter = builder[^1];
        return !char.IsWhiteSpace(lastCharacter)
            && !char.IsPunctuation(lastCharacter)
            && !char.IsWhiteSpace(nextText[0])
            && !char.IsPunctuation(nextText[0]);
    }

    private static string NormalizeInlineWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var sawWhitespace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                sawWhitespace = true;
                continue;
            }

            if (sawWhitespace && builder.Length > 0)
                builder.Append(' ');

            builder.Append(character);
            sawWhitespace = false;
        }

        return builder.ToString();
    }

    private static void AppendNewLine(StringBuilder builder, int count)
    {
        if (builder.Length == 0)
            return;

        var trailingNewLines = 0;
        for (var index = builder.Length - 1; index >= 0 && builder[index] == '\n'; index--)
            trailingNewLines++;

        for (var index = trailingNewLines; index < count; index++)
            builder.Append('\n');
    }

    private static string NormalizePlainText(string value)
    {
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        var builder = new StringBuilder(value.Length);
        var blankLineCount = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                if (blankLineCount == 0 && builder.Length > 0)
                    builder.AppendLine();
                blankLineCount++;
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '\n')
                builder.AppendLine();

            builder.Append(line);
            blankLineCount = 0;
        }

        return builder.ToString().Trim();
    }
}

public readonly record struct SanitizedComposeBody(string Html, string PlainText, bool HasBlockedContent)
{
    public static SanitizedComposeBody Empty { get; } = new(string.Empty, string.Empty, false);
}
